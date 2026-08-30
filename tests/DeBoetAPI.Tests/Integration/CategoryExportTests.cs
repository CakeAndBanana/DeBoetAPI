using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using DeBoetAPI.Features.Categories;

namespace DeBoetAPI.Tests.Integration;

/// <summary>
/// The spreadsheet export. Every test works against a uniquely named branch, because the whole
/// suite shares one database and the workbook contains every category in it.
/// </summary>
public class CategoryExportTests(PostgresFixture postgres) : CategoryTestBase(postgres)
{
    private const string ExportRoute = "/api/v1/categories/export";

    private const string SpreadsheetContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [Fact]
    public async Task Export_IsRefusedToAnyoneWithoutTheEditorRole()
    {
        // The one decision here that is not cosmetic. Every other category read is public, but
        // the workbook also carries inactive categories, SEO copy and who touched what.
        var anonymous = await Anonymous.GetAsync(ExportRoute);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var ordinary = await User.GetAsync(ExportRoute);
        Assert.Equal(HttpStatusCode.Forbidden, ordinary.StatusCode);

        var (editor, _, _, _) = await CreateUserAndSignInAsync(role: "Editor");
        using (editor)
        {
            var allowed = await editor.GetAsync(ExportRoute);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }
    }

    [Fact]
    public async Task Export_ComesBackAsASpreadsheetDownload()
    {
        var response = await Admin.GetAsync(ExportRoute);
        response.EnsureSuccessStatusCode();

        Assert.Equal(SpreadsheetContentType, response.Content.Headers.ContentType?.MediaType);

        var disposition = response.Content.Headers.ContentDisposition;
        Assert.NotNull(disposition);
        Assert.Equal("attachment", disposition!.DispositionType);

        // The filename is what the browser saves; if it stops ending in .xlsx, Windows stops
        // opening it in Excel.
        var name = disposition.FileNameStar ?? disposition.FileName;
        Assert.NotNull(name);
        Assert.EndsWith(".xlsx", name!.Trim('"'), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_GivesEachRootCategoryItsOwnSheet()
    {
        var name = Unique("Export Roots");
        await CreateCategoryAsync(name, slug: UniqueSlug("exportroots"));

        using var workbook = await ExportAsync();

        Assert.True(workbook.Worksheets.Contains("Overview"));
        Assert.True(workbook.Worksheets.Contains(name));
    }

    [Fact]
    public async Task Export_FoldsChildrenUnderTheirParent()
    {
        var rootName = Unique("Export Fold");
        var root = await CreateCategoryAsync(rootName, slug: UniqueSlug("exportfold"));
        var child = await CreateCategoryAsync("Fold Child", root.Id, slug: UniqueSlug("foldchild"));
        var grandchild = await CreateCategoryAsync("Fold Grandchild", child.Id, slug: UniqueSlug("foldgrand"));

        using var workbook = await ExportAsync();
        var sheet = workbook.Worksheet(rootName);

        // The outline levels are the fold controls. If these are flat, the +/- buttons in the
        // margin do nothing and the export is just a list.
        Assert.Equal(1, sheet.Row(RowOf(sheet, rootName)).OutlineLevel);
        Assert.Equal(2, sheet.Row(RowOf(sheet, "Fold Child")).OutlineLevel);
        Assert.Equal(3, sheet.Row(RowOf(sheet, "Fold Grandchild")).OutlineLevel);

        // Excel's default puts the summary row below its group, which would read as the parent
        // belonging to the branch beneath it.
        Assert.Equal(XLOutlineSummaryVLocation.Top, sheet.Outline.SummaryVLocation);

        // Depth-first: the grandchild sits directly under its parent, not after the branch.
        Assert.Equal(RowOf(sheet, "Fold Child") + 1, RowOf(sheet, "Fold Grandchild"));
        Assert.Equal(grandchild.ParentId, child.Id);
    }

    [Fact]
    public async Task Export_ShowsASharedCategoryUnderItsSecondaryParentAsALinkToTheRealRow()
    {
        var homeName = Unique("Export Home");
        var borrowName = Unique("Export Borrow");

        var home = await CreateCategoryAsync(homeName, slug: UniqueSlug("exporthome"));
        var shared = await CreateCategoryAsync("Shared Branch", home.Id, slug: UniqueSlug("sharedbranch"));
        var borrower = await CreateCategoryAsync(borrowName, slug: UniqueSlug("exportborrow"));

        var linked = await Admin.PostAsJsonAsync($"/api/v1/categories/{shared.Id}/links", new
        {
            parentId = borrower.Id,
        });
        linked.EnsureSuccessStatusCode();

        using var workbook = await ExportAsync();

        var homeSheet = workbook.Worksheet(homeName);
        var borrowSheet = workbook.Worksheet(borrowName);

        var realRow = RowOf(homeSheet, "Shared Branch");
        Assert.True(realRow > 0);

        // The reference row is marked, so it cannot be mistaken for a second copy of the record.
        var referenceRow = RowOf(borrowSheet, "↳ Shared Branch");
        Assert.True(referenceRow > 0);

        var hyperlink = borrowSheet.Cell(referenceRow, 1).GetHyperlink();
        Assert.False(hyperlink.IsExternal);
        Assert.Contains(homeName, hyperlink.InternalAddress, StringComparison.Ordinal);
        Assert.EndsWith($"!A{realRow}", hyperlink.InternalAddress, StringComparison.Ordinal);

        // And the branch it really lives in says so from its own side.
        Assert.Contains(borrowName, homeSheet.Cell(realRow, AlsoUnderColumn).GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_CountsOnlyRealPlacementsInTheOverview()
    {
        var homeName = Unique("Export Count Home");
        var borrowName = Unique("Export Count Borrow");

        var home = await CreateCategoryAsync(homeName, slug: UniqueSlug("countinghome"));
        var shared = await CreateCategoryAsync("Counted Child", home.Id, slug: UniqueSlug("countedchild"));
        var borrower = await CreateCategoryAsync(borrowName, slug: UniqueSlug("countingborrow"));

        var linked = await Admin.PostAsJsonAsync($"/api/v1/categories/{shared.Id}/links", new { parentId = borrower.Id });
        linked.EnsureSuccessStatusCode();

        using var workbook = await ExportAsync();
        var overview = workbook.Worksheet("Overview");

        // The borrowing branch shows a reference row but owns only itself: counting the borrowed
        // row would double it in the totals.
        Assert.Equal(2, overview.Cell(RowOf(overview, homeName), 3).GetDouble());
        Assert.Equal(1, overview.Cell(RowOf(overview, borrowName), 3).GetDouble());
    }

    [Fact]
    public async Task Export_CarriesTheWholeRecordIncludingInactiveCategories()
    {
        var rootName = Unique("Export Record");
        var root = await CreateCategoryAsync(rootName, slug: UniqueSlug("exportrecord"));

        var created = await Admin.PostAsJsonAsync("/api/v1/categories", new
        {
            name = "Hidden Child",
            slug = UniqueSlug("hiddenchild"),
            parentId = root.Id,
            metaTitle = "Hidden meta title",
            metaDescription = "Hidden meta description",
            isActive = false,
            isMenu = false,
            sortOrder = 3,
        });
        created.EnsureSuccessStatusCode();
        var child = (await created.Content.ReadFromJsonAsync<CategoryResponse>())!;

        using var workbook = await ExportAsync();
        var sheet = workbook.Worksheet(rootName);

        // Inactive is exactly the kind of category you open a spreadsheet to find, so its
        // absence would be the bug.
        var row = RowOf(sheet, "Hidden Child");
        Assert.True(row > 0);

        Assert.Equal(child.Slug, sheet.Cell(row, 3).GetString());
        Assert.Equal(child.PathSlug, sheet.Cell(row, 4).GetString());
        Assert.EndsWith($"/{child.PathSlug}", sheet.Cell(row, 5).GetString(), StringComparison.Ordinal);
        Assert.Equal("No", sheet.Cell(row, 6).GetString());
        Assert.Equal("No", sheet.Cell(row, 7).GetString());
        Assert.Equal(3, sheet.Cell(row, 8).GetDouble());
        Assert.Equal("Hidden meta title", sheet.Cell(row, 12).GetString());
        Assert.Equal("Hidden meta description", sheet.Cell(row, 13).GetString());
        Assert.Equal(child.Id.ToString(), sheet.Cell(row, 18).GetString());

        // A real date cell, not text, so the column sorts.
        Assert.Equal(XLDataType.DateTime, sheet.Cell(row, 14).DataType);

        // Resolved from Identity, which the panel cannot do for an editor.
        Assert.Equal("Administrator", sheet.Cell(row, 15).GetString());
    }

    [Fact]
    public async Task Export_MakesALegalUniqueSheetNameOutOfAnyCategoryName()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // Excel forbids these characters in a sheet name outright.
        var illegal = $"Ex/port [{suffix}]";
        await CreateCategoryAsync(illegal, slug: UniqueSlug("exportillegal"));

        // Two names that are identical for the first 31 characters, which is all a sheet name
        // gets. Without de-duplication the second AddWorksheet throws and no file is produced.
        var longA = $"Export {suffix} Very Long Category Name A";
        var longB = $"Export {suffix} Very Long Category Name B";
        await CreateCategoryAsync(longA, slug: UniqueSlug("exportlonga"));
        await CreateCategoryAsync(longB, slug: UniqueSlug("exportlongb"));

        using var workbook = await ExportAsync();

        var names = workbook.Worksheets.Select(w => w.Name).ToList();
        Assert.All(names, n => Assert.True(n.Length <= 31, $"'{n}' is {n.Length} characters"));
        Assert.All(names, n => Assert.Equal(-1, n.IndexOfAny(IllegalSheetCharacters)));

        var truncated = longA[..31];
        Assert.Contains(truncated, names, StringComparer.Ordinal);
        Assert.Contains(names, n => n.StartsWith(truncated[..25], StringComparison.Ordinal) && n.EndsWith("(2)", StringComparison.Ordinal));
    }

    /// <summary>Where the secondary parents are listed, one-based as Excel counts.</summary>
    private const int AlsoUnderColumn = 11;

    private static readonly char[] IllegalSheetCharacters = [':', '\\', '/', '?', '*', '[', ']'];

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..Math.Min(prefix.Length + 9, 60)];

    private async Task<XLWorkbook> ExportAsync()
    {
        var response = await Admin.GetAsync(ExportRoute);
        response.EnsureSuccessStatusCode();

        // Read into a stream of our own rather than handing over the response's: the workbook is
        // parsed in full by the constructor, so the stream is finished with the moment it returns.
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var stream = new MemoryStream(bytes);
        return new XLWorkbook(stream);
    }

    /// <summary>
    /// The row a category occupies, found by its name in the first column. Names are indented by
    /// depth, so the comparison trims.
    /// </summary>
    private static int RowOf(IXLWorksheet sheet, string name)
    {
        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;

        for (var row = 1; row <= last; row++)
        {
            if (string.Equals(sheet.Cell(row, 1).GetString().Trim(), name, StringComparison.Ordinal))
            {
                return row;
            }
        }

        return -1;
    }
}
