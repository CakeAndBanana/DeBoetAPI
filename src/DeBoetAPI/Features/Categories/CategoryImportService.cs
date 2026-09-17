using ClosedXML.Excel;
using DeBoetAPI.Common;
using DeBoetAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace DeBoetAPI.Features.Categories;

/// <summary>
/// The outcome of reading a workbook. <paramref name="Problem"/> is set only when the file itself
/// is unusable — a wrong shape, not a wrong value. Anything a single row got wrong is reported on
/// that row instead, so one bad cell never costs you the other two hundred.
/// </summary>
public sealed record ImportOutcome(ImportPlan? Plan, string? Problem, int Applied = 0);

/// <summary>
/// Reads an edited export back in.
///
/// Deliberately thin: the judgement lives in <see cref="CategoryImportPlanner"/>, which is pure
/// and tested on its own, and the writing is done by <see cref="CategoryTreeService"/>, which
/// already knows how to keep routes and the audit trail straight. What is left here is turning
/// cells into rows and running the plan in one transaction.
/// </summary>
public sealed class CategoryImportService(AppDbContext db, CategoryTreeService tree, AuditLog audit)
{
    /// <summary>Written into every audit entry the import produces.</summary>
    private const string AuditContext = "import";

    private const string OverviewSheet = "Overview";

    /// <summary>Says what would happen, and writes nothing at all.</summary>
    public async Task<ImportOutcome> PreviewAsync(Stream file, CancellationToken ct)
    {
        var (rows, problem) = Read(file);

        return problem is not null
            ? new ImportOutcome(null, problem)
            : new ImportOutcome(CategoryImportPlanner.Plan(rows!, await SnapshotAsync(ct)), null);
    }

    /// <summary>
    /// Plans the file again from scratch and applies it, all or nothing.
    ///
    /// Re-planning rather than trusting a preview means there is no token to expire and no server
    /// state to keep — and it is the only way the staleness check can be honest, since it has to
    /// be made against the tree as it is now, not as it was when the preview was drawn.
    /// </summary>
    public async Task<ImportOutcome> ApplyAsync(Stream file, CancellationToken ct)
    {
        var (rows, problem) = Read(file);

        if (problem is not null)
        {
            return new ImportOutcome(null, problem);
        }

        var plan = CategoryImportPlanner.Plan(rows!, await SnapshotAsync(ct));

        if (plan.Steps.Count == 0)
        {
            return new ImportOutcome(plan, null);
        }

        audit.Context = AuditContext;

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            foreach (var step in plan.Steps)
            {
                var failure = await ApplyStepAsync(step, ct);

                if (failure is not null)
                {
                    await transaction.RollbackAsync(ct);
                    return new ImportOutcome(plan, failure);
                }

                // The subtree rewrites inside Move and Update are raw SQL the change tracker never
                // sees, so anything already loaded here would go stale the moment a parent moves.
                // Clearing between steps costs a re-read and removes the whole class of problem.
                db.ChangeTracker.Clear();
            }

            await transaction.CommitAsync(ct);
        }
        finally
        {
            audit.Context = null;
        }

        return new ImportOutcome(plan, null, plan.Steps.Count);
    }

    /// <summary>
    /// One category's worth of change. A row that both moves and is edited needs two calls —
    /// <c>UpdateAsync</c> cannot reparent and <c>MoveAsync</c> cannot rename — and the move goes
    /// first so the rename lands in the place it is meant for.
    /// </summary>
    private async Task<string?> ApplyStepAsync(ImportStep step, CancellationToken ct)
    {
        if (step.Reparents)
        {
            var moved = await tree.MoveAsync(step.Id, step.NewParentId, null, ct);

            if (!moved.Succeeded)
            {
                return $"Moving '{step.Name}' was refused: {Describe(moved.Error)} Nothing was imported.";
            }
        }

        if (!step.ChangesValues)
        {
            return null;
        }

        var updated = await tree.UpdateAsync(
            step.Id, step.Name, step.Slug, step.MetaTitle, step.MetaDescription,
            step.SortOrder, step.IsActive, step.IsMenu, step.RedirectToId, ct);

        return updated.Succeeded
            ? null
            : $"Updating '{step.Name}' was refused: {Describe(updated.Error)} Nothing was imported.";
    }

    /// <summary>
    /// The live tree, flattened to what the planner needs. Everything, not just the rows in the
    /// file: a rename can collide with a category nobody put in the spreadsheet.
    /// </summary>
    private async Task<List<ImportSnapshot>> SnapshotAsync(CancellationToken ct) =>
        await db.Categories.AsNoTracking()
            .Select(c => new ImportSnapshot(
                c.Id, c.Name, c.Slug, c.PathSlug, c.ParentId, c.RedirectToId,
                c.SortOrder, c.IsActive, c.IsMenu, c.MetaTitle, c.MetaDescription, c.UpdatedAt))
            .ToListAsync(ct);

    /// <summary>
    /// Turns a workbook into rows. The header is the format contract: a sheet whose columns do
    /// not match the export exactly is refused outright, because the alternative is reading one
    /// column as another and writing the result to the database.
    /// </summary>
    private static (List<ImportRow>? Rows, string? Problem) Read(Stream file)
    {
        XLWorkbook workbook;

        try
        {
            workbook = new XLWorkbook(file);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return (null, "That file could not be opened as a spreadsheet.");
        }

        using (workbook)
        {
            var rows = new List<ImportRow>();

            foreach (var sheet in workbook.Worksheets)
            {
                if (string.Equals(sheet.Name, OverviewSheet, StringComparison.Ordinal))
                {
                    continue;
                }

                var mismatch = CheckHeader(sheet);

                if (mismatch is not null)
                {
                    return (null, mismatch);
                }

                var last = sheet.LastRowUsed()?.RowNumber() ?? 1;

                for (var number = 2; number <= last; number++)
                {
                    var row = ReadRow(sheet, number);

                    if (row is not null)
                    {
                        rows.Add(row);
                    }
                }
            }

            return (rows, null);
        }
    }

    private static string? CheckHeader(IXLWorksheet sheet)
    {
        var expected = CategoryExportService.Headers;

        for (var column = 0; column < expected.Length; column++)
        {
            var actual = sheet.Cell(1, column + 1).GetString().Trim();

            if (!string.Equals(actual, expected[column], StringComparison.Ordinal))
            {
                return $"Sheet '{sheet.Name}' does not match the export: column {column + 1} should "
                       + $"be '{expected[column]}' but says '{actual}'. Export the tree again and "
                       + "edit that file.";
            }
        }

        return null;
    }

    /// <summary>
    /// One row, or null when it is not a category at all. The reference rows the export writes for
    /// a category borrowed from another branch carry no id, which is exactly the test — they
    /// describe somewhere else's category and must never be written from here.
    /// </summary>
    private static ImportRow? ReadRow(IXLWorksheet sheet, int number)
    {
        var idText = sheet.Cell(number, CategoryExportService.ColId).GetString().Trim();

        if (idText.Length == 0)
        {
            return null;
        }

        var name = sheet.Cell(number, CategoryExportService.ColName).GetString().Trim();

        if (!Guid.TryParse(idText, out var id))
        {
            // Reported rather than skipped: a mangled id means an edit that will not be applied,
            // and silently dropping it is how someone loses an afternoon's work.
            return new ImportRow(sheet.Name, number, Guid.Empty, name, null, null, null,
                true, true, 0, null, null, null,
                $"The Id cell says '{idText}', which is not a category id.");
        }

        var (isActive, activeProblem) = YesNo(sheet, number, CategoryExportService.ColActive, "Active");
        var (isMenu, menuProblem) = YesNo(sheet, number, CategoryExportService.ColMenu, "In menu");
        var (sortOrder, sortProblem) = Number(sheet, number, CategoryExportService.ColSort, "Sort order");

        return new ImportRow(
            sheet.Name,
            number,
            id,
            name,
            Text(sheet, number, CategoryExportService.ColSlug),
            Text(sheet, number, CategoryExportService.ColParent),
            Text(sheet, number, CategoryExportService.ColRedirect),
            isActive,
            isMenu,
            sortOrder,
            Text(sheet, number, CategoryExportService.ColMetaTitle),
            Text(sheet, number, CategoryExportService.ColMetaDescription),
            Moment(sheet, number, CategoryExportService.ColUpdated),
            activeProblem ?? menuProblem ?? sortProblem);
    }

    private static string? Text(IXLWorksheet sheet, int row, int column)
    {
        var value = sheet.Cell(row, column).GetString().Trim();
        return value.Length == 0 ? null : value;
    }

    /// <summary>
    /// Lenient about how someone writes a yes, strict about anything else — a cell reading
    /// "maybe" must not quietly become "no".
    /// </summary>
    private static (bool Value, string? Problem) YesNo(IXLWorksheet sheet, int row, int column, string label)
    {
        var value = sheet.Cell(row, column).GetString().Trim();

        return value.ToLowerInvariant() switch
        {
            "yes" or "y" or "true" or "1" or "ja" => (true, null),
            "no" or "n" or "false" or "0" or "nee" => (false, null),
            _ => (false, $"The '{label}' cell says '{value}'. It should say Yes or No."),
        };
    }

    private static (int Value, string? Problem) Number(IXLWorksheet sheet, int row, int column, string label)
    {
        var cell = sheet.Cell(row, column);

        if (cell.DataType == XLDataType.Number)
        {
            return ((int)cell.GetDouble(), null);
        }

        var text = cell.GetString().Trim();

        if (text.Length == 0)
        {
            return (0, null);
        }

        return int.TryParse(text, out var parsed)
            ? (parsed, null)
            : (0, $"The '{label}' cell says '{text}', which is not a number.");
    }

    /// <summary>The export writes these as UTC, so that is how they are read back.</summary>
    private static DateTimeOffset? Moment(IXLWorksheet sheet, int row, int column)
    {
        var cell = sheet.Cell(row, column);

        if (cell.DataType == XLDataType.DateTime)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(cell.GetDateTime(), DateTimeKind.Utc));
        }

        var text = cell.GetString().Trim();

        return DateTimeOffset.TryParse(text, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static string Describe(CategoryError error) => error switch
    {
        CategoryError.NotFound => "it no longer exists.",
        CategoryError.ParentNotFound => "the parent no longer exists.",
        CategoryError.DuplicateSlug => "another category already uses that route.",
        CategoryError.Cycle => "that would place it inside its own subtree.",
        CategoryError.SelfReference => "a category cannot be its own parent.",
        CategoryError.RedirectNotFound => "the category it redirects to no longer exists.",
        CategoryError.RedirectChain => "redirects are one hop only.",
        CategoryError.ParentInactive => "a category above it is inactive, so it cannot be active.",
        _ => "the API refused it.",
    };
}
