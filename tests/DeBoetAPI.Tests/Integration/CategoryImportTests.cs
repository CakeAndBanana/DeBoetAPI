using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using DeBoetAPI.Features.Categories;
using Col = DeBoetAPI.Features.Categories.CategoryExportService;

namespace DeBoetAPI.Tests.Integration;

/// <summary>
/// The whole loop: export, edit the workbook, post it back. These go through the real endpoints
/// with a real file rather than hand-built rows, because the format is half the feature — a
/// column that moves breaks the import just as surely as a bug in the planner.
/// </summary>
public class CategoryImportTests(PostgresFixture postgres) : CategoryTestBase(postgres)
{
    private const string ExportRoute = "/api/v1/categories/export";
    private const string PreviewRoute = "/api/v1/categories/import/preview";
    private const string ImportRoute = "/api/v1/categories/import";

    [Fact]
    public async Task Import_IsRefusedToAnyoneWithoutTheEditorRole()
    {
        var (root, _) = await BranchAsync("Import Auth");
        var workbook = await ExportAsync();

        foreach (var route in new[] { PreviewRoute, ImportRoute })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Post(Anonymous, route, workbook)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Post(User, route, workbook)).StatusCode);
        }

        var (editor, _, _, _) = await CreateUserAndSignInAsync(role: "Editor");
        using (editor)
        {
            Assert.Equal(HttpStatusCode.OK, (await Post(editor, PreviewRoute, workbook)).StatusCode);
        }

        Assert.NotEqual(Guid.Empty, root.Id);
    }

    [Fact]
    public async Task Preview_ReportsTheEditAndWritesNothing()
    {
        var (root, child) = await BranchAsync("Import Preview");
        var before = (await GetCategoryAsync(child.Id)).Category.UpdatedAt;

        var edited = Edit(await ExportAsync(), root.Name, child.Id, Col.ColName, "Renamed In Excel");
        var preview = await Read(await Post(Admin, PreviewRoute, edited));

        Assert.Equal(1, preview.Changed);
        var row = preview.Rows.Single(r => r.Id == child.Id);
        Assert.Equal("Changed", row.State);
        Assert.Contains(row.Changes, c => c.Contains("name:", StringComparison.Ordinal));
        Assert.Equal(0, preview.Applied);

        // The point of a preview: it is safe to run on anything.
        Assert.Equal(before, (await GetCategoryAsync(child.Id)).Category.UpdatedAt);
    }

    [Fact]
    public async Task Import_AppliesARenameAndRewritesTheRoutesBeneathIt()
    {
        var (root, child) = await BranchAsync("Import Rename");
        var grandchild = await CreateCategoryAsync("Deep", child.Id, slug: UniqueSlug("deep"));

        var edited = Edit(await ExportAsync(), root.Name, child.Id, Col.ColSlug, "renamed-branch");
        var result = await Read(await Post(Admin, ImportRoute, edited));

        Assert.Equal(1, result.Applied);

        var after = await GetCategoryAsync(child.Id);
        Assert.Equal("renamed-branch", after.Category.Slug);
        Assert.Equal($"{root.PathSlug}-renamed-branch", after.Category.PathSlug);

        // The subtree has to follow, or every page below it 404s.
        var below = await GetCategoryAsync(grandchild.Id);
        Assert.StartsWith($"{root.PathSlug}-renamed-branch-", below.Category.PathSlug, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_MovesACategoryWhenItsParentRouteChanges()
    {
        var (root, child) = await BranchAsync("Import Move");
        var elsewhere = await CreateCategoryAsync(Unique("Import Move Target"), slug: UniqueSlug("movetarget"));

        var edited = Edit(await ExportAsync(), root.Name, child.Id, Col.ColParent, elsewhere.PathSlug);
        var result = await Read(await Post(Admin, ImportRoute, edited));

        Assert.Equal(1, result.Applied);

        var after = await GetCategoryAsync(child.Id);
        Assert.Equal(elsewhere.Id, after.Category.ParentId);
        Assert.Equal($"{elsewhere.PathSlug}-{after.Category.Slug}", after.Category.PathSlug);
    }

    [Fact]
    public async Task Import_AppliesValuesAndAMoveTogether()
    {
        var (root, child) = await BranchAsync("Import Both");
        var elsewhere = await CreateCategoryAsync(Unique("Import Both Target"), slug: UniqueSlug("bothtarget"));

        var edited = await ExportAsync();
        edited = Edit(edited, root.Name, child.Id, Col.ColParent, elsewhere.PathSlug);
        edited = Edit(edited, root.Name, child.Id, Col.ColName, "Moved And Renamed");
        edited = Edit(edited, root.Name, child.Id, Col.ColActive, "No");

        Assert.Equal(1, (await Read(await Post(Admin, ImportRoute, edited))).Applied);

        var after = (await GetCategoryAsync(child.Id)).Category;
        Assert.Equal(elsewhere.Id, after.ParentId);
        Assert.Equal("Moved And Renamed", after.Name);
        Assert.False(after.IsActive);
    }

    [Fact]
    public async Task Import_RefusesARowThatIsOlderThanTheCategoryItDescribes()
    {
        var (root, child) = await BranchAsync("Import Stale");
        var sibling = await CreateCategoryAsync("Stale Sibling", root.Id, slug: UniqueSlug("stalesib"));

        var name = (await GetCategoryAsync(child.Id)).Category.Name;

        // What a workbook exported an hour ago looks like once someone has edited that category
        // in the panel: its timestamp is behind the stored one. Written into the cell rather than
        // staged with a real edit and a wait, so the test does not depend on how long an HTTP
        // round trip happens to take.
        var edited = Edit(await ExportAsync(), root.Name, child.Id, Col.ColUpdated,
            DateTime.UtcNow.AddHours(-1));
        edited = Edit(edited, root.Name, child.Id, Col.ColName, "Renamed In Excel");
        edited = Edit(edited, root.Name, sibling.Id, Col.ColName, "Sibling Renamed");

        var result = await Read(await Post(Admin, ImportRoute, edited));

        Assert.Equal("Stale", result.Rows.Single(r => r.Id == child.Id).State);

        // Refusing one row must not cost the others theirs.
        Assert.Equal(1, result.Applied);
        Assert.Equal(name, (await GetCategoryAsync(child.Id)).Category.Name);
        Assert.Equal("Sibling Renamed", (await GetCategoryAsync(sibling.Id)).Category.Name);
    }

    [Fact]
    public async Task Import_RefusesARouteThatIsAlreadyTaken()
    {
        var (root, child) = await BranchAsync("Import Clash");
        var sibling = await CreateCategoryAsync("Clash Sibling", root.Id, slug: UniqueSlug("clashsib"));

        var edited = Edit(await ExportAsync(), root.Name, child.Id, Col.ColSlug, sibling.Slug);
        var result = await Read(await Post(Admin, ImportRoute, edited));

        Assert.Equal(0, result.Applied);
        var row = result.Rows.Single(r => r.Id == child.Id);
        Assert.Equal("Refused", row.State);
        Assert.Contains("two categories at once", row.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_IgnoresTheReferenceRowsWrittenForASharedCategory()
    {
        var (root, child) = await BranchAsync("Import Shared");
        var borrower = await CreateCategoryAsync(Unique("Import Shared Borrow"), slug: UniqueSlug("sharedborrow"));

        var linked = await Admin.PostAsJsonAsync($"/api/v1/categories/{child.Id}/links", new { parentId = borrower.Id });
        linked.EnsureSuccessStatusCode();

        var workbook = await ExportAsync();

        using (var check = Open(workbook))
        {
            // The reference row is there, and carries no id — which is what keeps the importer
            // from treating it as a second copy of the category.
            var sheet = check.Worksheet(borrower.Name);
            var reference = RowOf(sheet, "↳ " + child.Name);
            Assert.True(reference > 0);
            Assert.Equal(string.Empty, sheet.Cell(reference, Col.ColId).GetString());
        }

        var result = await Read(await Post(Admin, PreviewRoute, workbook));
        Assert.Equal(0, result.Changed);
        Assert.DoesNotContain(result.Rows, r => r.State == "Refused");
    }

    [Fact]
    public async Task Import_RefusesAWorkbookWhoseColumnsDoNotMatch()
    {
        var (root, _) = await BranchAsync("Import Header");

        var workbook = await ExportAsync();
        using var book = Open(workbook);
        book.Worksheet(root.Name).Cell(1, Col.ColParent).Value = "Parent";

        var response = await Post(Admin, PreviewRoute, Save(book));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadAsStringAsync();
        Assert.Contains("Parent route", problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_WritesManyChangesInOneGo()
    {
        // Every one of these calls Move or Update, which each open a transaction of their own
        // when called alone. Running them together inside the import's transaction is what the
        // nesting guard in CategoryTreeService exists for — without it this throws on row one.
        var (root, child) = await BranchAsync("Import Batch");
        var second = await CreateCategoryAsync("Batch Two", root.Id, slug: UniqueSlug("batchtwo"));
        var third = await CreateCategoryAsync("Batch Three", child.Id, slug: UniqueSlug("batchthree"));
        var elsewhere = await CreateCategoryAsync(Unique("Import Batch Home"), slug: UniqueSlug("batchhome"));

        var edited = await ExportAsync();
        edited = Edit(edited, root.Name, child.Id, Col.ColName, "Batch One Renamed");
        edited = Edit(edited, root.Name, second.Id, Col.ColActive, "No");
        edited = Edit(edited, root.Name, third.Id, Col.ColParent, elsewhere.PathSlug);

        var result = await Read(await Post(Admin, ImportRoute, edited));

        Assert.Equal(3, result.Applied);
        Assert.Equal("Batch One Renamed", (await GetCategoryAsync(child.Id)).Category.Name);
        Assert.False((await GetCategoryAsync(second.Id)).Category.IsActive);
        Assert.Equal(elsewhere.Id, (await GetCategoryAsync(third.Id)).Category.ParentId);
    }

    [Fact]
    public async Task Import_MarksWhatItWroteAsAnImportInTheHistory()
    {
        var (root, child) = await BranchAsync("Import Audit");

        var edited = Edit(await ExportAsync(), root.Name, child.Id, Col.ColName, "Audited Rename");
        Assert.Equal(1, (await Read(await Post(Admin, ImportRoute, edited))).Applied);

        var history = await Admin.GetFromJsonAsync<PagedAudit>($"/api/v1/categories/{child.Id}/history");

        Assert.NotNull(history);
        Assert.Contains(history!.Items, e => e.Summary is not null
            && e.Summary.Contains("(import)", StringComparison.Ordinal));
    }

    // ---- helpers ----------------------------------------------------------------------------

    private sealed record PagedAudit(List<AuditRow> Items);

    private sealed record AuditRow(string Action, string? Summary);

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..Math.Min(prefix.Length + 9, 60)];

    /// <summary>A uniquely named root with one child, so a test never trips over another's data.</summary>
    private async Task<(CategoryResponse Root, CategoryResponse Child)> BranchAsync(string label)
    {
        var root = await CreateCategoryAsync(Unique(label), slug: UniqueSlug("imp"));
        var child = await CreateCategoryAsync($"{label} Child", root.Id, slug: UniqueSlug("impchild"));
        return (root, child);
    }

    private async Task<byte[]> ExportAsync()
    {
        var response = await Admin.GetAsync(ExportRoute);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    /// <summary>
    /// The stream is deliberately not disposed: ClosedXML keeps hold of the package it was opened
    /// from, and saving a workbook whose source stream has been closed throws.
    /// </summary>
    private static XLWorkbook Open(byte[] workbook) => new(new MemoryStream(workbook));

    private static byte[] Save(XLWorkbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Changes one cell, the way a person would in Excel.</summary>
    private static byte[] Edit(byte[] workbook, string sheetName, Guid id, int column, string value) =>
        Edit(workbook, sheetName, id, column, cell => cell.Value = value);

    private static byte[] Edit(byte[] workbook, string sheetName, Guid id, int column, DateTime value) =>
        Edit(workbook, sheetName, id, column, cell => cell.Value = value);

    private static byte[] Edit(byte[] workbook, string sheetName, Guid id, int column, Action<IXLCell> change)
    {
        using var book = Open(workbook);
        var sheet = book.Worksheet(sheetName);
        var row = RowOfId(sheet, id);
        Assert.True(row > 0, $"No row for {id} in sheet '{sheetName}'.");

        change(sheet.Cell(row, column));
        return Save(book);
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string route, byte[] workbook)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(workbook);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            CategoryExportService.ContentType);
        content.Add(file, "file", "categories.xlsx");

        return await client.PostAsync(route, content);
    }

    private static async Task<ImportResponse> Read(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ImportResponse>())!;
    }

    private static int RowOfId(IXLWorksheet sheet, Guid id)
    {
        var wanted = id.ToString();
        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;

        for (var row = 2; row <= last; row++)
        {
            if (string.Equals(sheet.Cell(row, Col.ColId).GetString().Trim(), wanted, StringComparison.OrdinalIgnoreCase))
            {
                return row;
            }
        }

        return -1;
    }

    private static int RowOf(IXLWorksheet sheet, string name)
    {
        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;

        for (var row = 2; row <= last; row++)
        {
            if (string.Equals(sheet.Cell(row, Col.ColName).GetString().Trim(), name, StringComparison.Ordinal))
            {
                return row;
            }
        }

        return -1;
    }
}
