using ClosedXML.Excel;
using DeBoetAPI.Data;
using DeBoetAPI.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeBoetAPI.Features.Categories;

/// <summary>
/// Renders the whole taxonomy as a workbook: one sheet per root category, rows folded into
/// Excel's row outline so a branch collapses the way it does in the panel.
///
/// It reads the tables directly rather than reusing <see cref="CategoryTreeService"/>, for two
/// reasons: the tree DTO drops the timestamps and authorship the export is supposed to carry,
/// and it ignores <see cref="CategoryLink"/> entirely, which is the whole point of the
/// cross-links below.
/// </summary>
public sealed class CategoryExportService(AppDbContext db, IConfiguration config)
{
    public const string ContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>
    /// Excel's own limits. A longer sheet name is rejected outright; an outline deeper than
    /// seven is silently dropped, so it is clamped rather than left to disappear.
    /// </summary>
    private const int MaxSheetNameLength = 31;
    private const int MaxOutlineLevel = 7;

    private const string OverviewSheet = "Overview";

    private static readonly char[] IllegalSheetNameChars = [':', '\\', '/', '?', '*', '[', ']'];

    // Column numbers, so the writer reads as prose instead of as a row of magic integers.
    private const int ColName = 1;
    private const int ColLevel = 2;
    private const int ColSlug = 3;
    private const int ColRoute = 4;
    private const int ColUrl = 5;
    private const int ColActive = 6;
    private const int ColMenu = 7;
    private const int ColSort = 8;
    private const int ColChildren = 9;
    private const int ColRedirect = 10;
    private const int ColAlsoUnder = 11;
    private const int ColMetaTitle = 12;
    private const int ColMetaDescription = 13;
    private const int ColCreated = 14;
    private const int ColCreatedBy = 15;
    private const int ColUpdated = 16;
    private const int ColUpdatedBy = 17;
    private const int ColId = 18;

    private static readonly string[] Headers =
    [
        "Name", "Level", "Slug", "Full route", "URL", "Active", "In menu", "Sort order",
        "Children", "Redirects to", "Also under", "Meta title", "Meta description",
        "Created (UTC)", "Created by", "Updated (UTC)", "Updated by", "Id",
    ];

    /// <summary>
    /// Widths in Excel's character units, in the order of <see cref="Headers"/>. Set explicitly
    /// rather than by AdjustToContents, which measures every string against a real font and is
    /// by far the most expensive thing this could do.
    /// </summary>
    private static readonly double[] Widths =
        [38, 6, 22, 34, 42, 8, 9, 10, 9, 28, 28, 30, 46, 17, 16, 17, 16, 38];

    private static readonly string[] OverviewHeaders = ["Category", "Route", "Categories in branch"];
    private static readonly double[] OverviewWidths = [38, 34, 22];

    private const string DateFormat = "yyyy-mm-dd hh:mm";

    private static readonly XLColor Muted = XLColor.FromArgb(0x88, 0x88, 0x88);
    private static readonly XLColor HeaderFill = XLColor.FromArgb(0xEF, 0xEF, 0xEF);

    /// <summary>Builds the workbook and hands back the bytes ready to be sent.</summary>
    public async Task<byte[]> BuildAsync(CancellationToken ct)
    {
        var categories = await db.Categories.AsNoTracking().ToListAsync(ct);
        var links = await db.CategoryLinks.AsNoTracking()
            .Select(l => new { l.ParentId, l.ChildId })
            .ToListAsync(ct);

        // The panel cannot do this — resolving an author needs the Admin-only /users route — but
        // the API is already on the right side of that fence, so the file gets real names.
        var authors = await db.Users.AsNoTracking()
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var site = (config["Site:Url"] ?? "https://www.deboet.nl").TrimEnd('/');

        using var workbook = new XLWorkbook();
        Render(workbook, categories, links.Select(l => (l.ParentId, l.ChildId)).ToList(), authors, site);

        using var buffer = new MemoryStream();
        workbook.SaveAs(buffer);
        return buffer.ToArray();
    }

    /// <summary>The name a file gets when it is downloaded.</summary>
    public static string FileName(DateTimeOffset now) =>
        $"deboet-categories-{now:yyyy-MM-dd}.xlsx";

    private static void Render(
        XLWorkbook workbook,
        List<Category> categories,
        List<(Guid ParentId, Guid ChildId)> links,
        Dictionary<Guid, string> authors,
        string site)
    {
        var byId = categories.ToDictionary(c => c.Id);

        var childrenOf = categories
            .Where(c => c.ParentId is not null)
            .GroupBy(c => c.ParentId!.Value)
            .ToDictionary(g => g.Key, Ordered);

        var roots = Ordered(categories.Where(c => c.ParentId is null));

        // Both directions of the secondary-placement table: what hangs under a category, and
        // where a category additionally shows up.
        var linkedChildrenOf = links
            .GroupBy(l => l.ParentId)
            .ToDictionary(g => g.Key, g => g.Select(l => l.ChildId).ToList());
        var linkedParentsOf = links
            .GroupBy(l => l.ChildId)
            .ToDictionary(g => g.Key, g => g.Select(l => l.ParentId).ToList());

        // Pass one records where every category really lives; pass two turns the reference rows
        // into hyperlinks. Two passes because a link can point into a sheet that does not exist
        // yet when the row referring to it is written.
        var placed = new Dictionary<Guid, IXLCell>();
        var pending = new List<(IXLCell From, Guid To)>();

        var overview = workbook.AddWorksheet(OverviewSheet);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            OverviewSheet,
            // Reserved by Excel for change tracking; a sheet by that name makes the file
            // unopenable rather than merely odd.
            "History",
        };

        var overviewRow = 2;

        foreach (var root in roots)
        {
            var sheet = workbook.AddWorksheet(SheetName(root.Name, taken));
            WriteHeader(sheet, Headers, Widths);
            sheet.Outline.SummaryVLocation = XLOutlineSummaryVLocation.Top;

            // Counted from the rows that were actually placed, not from the row number: a sheet
            // also holds reference rows for categories that live elsewhere, and counting those
            // would inflate every branch that borrows one.
            var before = placed.Count;
            var next = WriteBranch(sheet, 2, root, 0);
            Finish(sheet, next - 1, Headers.Length);

            overview.Cell(overviewRow, 1).Value = root.Name;
            overview.Cell(overviewRow, 1).SetHyperlink(new XLHyperlink(sheet.Cell(2, ColName)));
            overview.Cell(overviewRow, 2).Value = root.PathSlug;
            overview.Cell(overviewRow, 3).Value = placed.Count - before;
            overviewRow++;
        }

        WriteHeader(overview, OverviewHeaders, OverviewWidths);
        Finish(overview, overviewRow - 1, OverviewHeaders.Length);

        foreach (var (from, to) in pending)
        {
            if (placed.TryGetValue(to, out var target))
            {
                from.SetHyperlink(new XLHyperlink(target));
            }
        }

        int WriteBranch(IXLWorksheet sheet, int row, Category node, int depth)
        {
            var here = row++;
            placed[node.Id] = sheet.Cell(here, ColName);
            WriteRow(sheet, here, node, depth);

            foreach (var child in childrenOf.GetValueOrDefault(node.Id) ?? [])
            {
                row = WriteBranch(sheet, row, child, depth + 1);
            }

            // A category linked in from elsewhere gets a single reference row and is not
            // recursed into: its subtree belongs to the branch it actually lives in, and
            // repeating it here would double every count in the file.
            foreach (var linkedId in linkedChildrenOf.GetValueOrDefault(node.Id) ?? [])
            {
                if (!byId.TryGetValue(linkedId, out var linked))
                {
                    continue;
                }

                WriteReference(sheet, row, linked, depth + 1);
                pending.Add((sheet.Cell(row, ColName), linkedId));
                row++;
            }

            return row;
        }

        void WriteRow(IXLWorksheet sheet, int row, Category c, int depth)
        {
            sheet.Row(row).OutlineLevel = Math.Min(depth + 1, MaxOutlineLevel);

            // Indented as well as outlined: LibreOffice and Google Sheets render outlines
            // inconsistently, and without the indent the shape would be lost there.
            sheet.Cell(row, ColName).Value = new string(' ', depth * 4) + c.Name;
            sheet.Cell(row, ColLevel).Value = depth + 1;
            sheet.Cell(row, ColSlug).Value = c.Slug;
            sheet.Cell(row, ColRoute).Value = c.PathSlug;
            sheet.Cell(row, ColUrl).Value = $"{site}/{c.PathSlug}";
            sheet.Cell(row, ColActive).Value = YesNo(c.IsActive);
            sheet.Cell(row, ColMenu).Value = YesNo(c.IsMenu);
            sheet.Cell(row, ColSort).Value = c.SortOrder;
            sheet.Cell(row, ColChildren).Value = (childrenOf.GetValueOrDefault(c.Id) ?? []).Count;

            sheet.Cell(row, ColRedirect).Value =
                c.RedirectToId is { } target && byId.TryGetValue(target, out var to)
                    ? $"{to.Name} ({to.PathSlug})"
                    : string.Empty;

            sheet.Cell(row, ColAlsoUnder).Value = string.Join(", ",
                (linkedParentsOf.GetValueOrDefault(c.Id) ?? [])
                    .Select(id => byId.TryGetValue(id, out var p) ? p.Name : null)
                    .Where(name => name is not null));

            sheet.Cell(row, ColMetaTitle).Value = c.MetaTitle ?? string.Empty;
            sheet.Cell(row, ColMetaDescription).Value = c.MetaDescription ?? string.Empty;

            WriteDate(sheet.Cell(row, ColCreated), c.CreatedAt);
            sheet.Cell(row, ColCreatedBy).Value = Author(c.CreatedById);
            WriteDate(sheet.Cell(row, ColUpdated), c.UpdatedAt);
            sheet.Cell(row, ColUpdatedBy).Value = Author(c.UpdatedById);

            sheet.Cell(row, ColId).Value = c.Id.ToString();

            if (!c.IsActive)
            {
                sheet.Range(row, 1, row, Headers.Length).Style.Font.FontColor = Muted;
            }
        }

        // Deliberately not a second copy of the record: a reference row carries only enough to
        // recognise the category and jump to it, so nobody mistakes it for a separate entry or
        // edits the wrong one of two identical-looking rows.
        void WriteReference(IXLWorksheet sheet, int row, Category c, int depth)
        {
            sheet.Row(row).OutlineLevel = Math.Min(depth + 1, MaxOutlineLevel);

            sheet.Cell(row, ColName).Value = new string(' ', depth * 4) + "↳ " + c.Name;
            sheet.Cell(row, ColLevel).Value = depth + 1;
            sheet.Cell(row, ColRoute).Value = c.PathSlug;

            var home = c.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent)
                ? parent.Name
                : "the top level";
            sheet.Cell(row, ColAlsoUnder).Value = $"also placed here — lives under {home}";

            var style = sheet.Range(row, 1, row, Headers.Length).Style;
            style.Font.Italic = true;
            style.Font.FontColor = Muted;
        }

        string Author(Guid? id) =>
            id is { } who && authors.TryGetValue(who, out var name) ? name : string.Empty;
    }

    private static List<Category> Ordered(IEnumerable<Category> categories) =>
        [.. categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)];

    private static string YesNo(bool value) => value ? "Yes" : "No";

    private static void WriteDate(IXLCell cell, DateTimeOffset value)
    {
        // A real date cell, not a formatted string, so the column sorts and filters as a date.
        cell.Value = value.UtcDateTime;
        cell.Style.DateFormat.Format = DateFormat;
    }

    private static void WriteHeader(IXLWorksheet sheet, string[] headers, double[] widths)
    {
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Column(i + 1).Width = widths[i];
        }

        var row = sheet.Range(1, 1, 1, headers.Length);
        row.Style.Font.Bold = true;
        row.Style.Fill.BackgroundColor = HeaderFill;
    }

    private static void Finish(IXLWorksheet sheet, int lastRow, int columns)
    {
        sheet.SheetView.FreezeRows(1);

        if (lastRow >= 1)
        {
            sheet.Range(1, 1, lastRow, columns).SetAutoFilter();
        }
    }

    /// <summary>
    /// A legal, unique sheet name for a category. Excel caps a name at 31 characters, forbids
    /// <c>: \ / ? * [ ]</c>, will not take a blank one, and refuses a workbook with two sheets
    /// sharing a name — and a category name is free text, so all four are reachable.
    /// </summary>
    internal static string SheetName(string name, ISet<string> taken)
    {
        var cleaned = new string([.. name.Select(c => IllegalSheetNameChars.Contains(c) ? ' ' : c)])
            // A leading or trailing apostrophe is rejected too, because a sheet reference is
            // quoted with them.
            .Trim()
            .Trim('\'');

        if (cleaned.Length == 0)
        {
            cleaned = "Category";
        }

        if (cleaned.Length > MaxSheetNameLength)
        {
            cleaned = cleaned[..MaxSheetNameLength].TrimEnd();
        }

        var candidate = cleaned;

        for (var n = 2; !taken.Add(candidate); n++)
        {
            var suffix = $" ({n})";
            var room = MaxSheetNameLength - suffix.Length;
            candidate = (cleaned.Length > room ? cleaned[..room].TrimEnd() : cleaned) + suffix;
        }

        return candidate;
    }
}
