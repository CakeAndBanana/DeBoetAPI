using DeBoetAPI.Features.Categories;

namespace DeBoetAPI.Tests.Unit;

/// <summary>
/// The planner decides what an edited workbook would do before anything is written, so these are
/// the tests that matter most: everything here is a mistake that would otherwise reach the
/// database, or a valid change that would otherwise be refused. No database — the planner is a
/// pure function over a snapshot, which is exactly why it was written that way.
///
/// The forest, shared by every test:
///
///   Christmas            christmas
///     Lighting           christmas-lighting
///       Indoor           christmas-lighting-indoor
///     Trees              christmas-trees
///   Halloween            halloween
/// </summary>
public class CategoryImportPlannerTests
{
    private static readonly DateTimeOffset Exported = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Christmas = new("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid Lighting = new("00000000-0000-0000-0000-0000000000c2");
    private static readonly Guid Indoor = new("00000000-0000-0000-0000-0000000000c3");
    private static readonly Guid Trees = new("00000000-0000-0000-0000-0000000000c4");
    private static readonly Guid Halloween = new("00000000-0000-0000-0000-0000000000c5");

    private static List<ImportSnapshot> Forest() =>
    [
        Snap(Christmas, "Christmas", "christmas", "christmas", null),
        Snap(Lighting, "Lighting", "lighting", "christmas-lighting", Christmas),
        Snap(Indoor, "Indoor", "indoor", "christmas-lighting-indoor", Lighting),
        Snap(Trees, "Trees", "trees", "christmas-trees", Christmas),
        Snap(Halloween, "Halloween", "halloween", "halloween", null),
    ];

    private static ImportSnapshot Snap(Guid id, string name, string slug, string route, Guid? parent) =>
        new(id, name, slug, route, parent, null, 0, true, true, null, null, Exported);

    /// <summary>A row exactly as exported — every test then edits one cell of it.</summary>
    private static ImportRow Row(
        ImportSnapshot from,
        int number = 2,
        string? name = null,
        string? slug = null,
        string? parentRoute = null,
        string? redirectRoute = null,
        bool? isActive = null,
        int? sortOrder = null,
        string? metaTitle = null,
        DateTimeOffset? updatedAt = null,
        bool clearUpdatedAt = false)
    {
        var live = Forest();
        var parent = from.ParentId is { } p ? live.First(c => c.Id == p).PathSlug : string.Empty;

        return new ImportRow(
            "Christmas", number, from.Id,
            name ?? from.Name,
            slug ?? from.Slug,
            parentRoute ?? parent,
            redirectRoute ?? string.Empty,
            isActive ?? from.IsActive,
            from.IsMenu,
            sortOrder ?? from.SortOrder,
            metaTitle ?? from.MetaTitle,
            from.MetaDescription,
            clearUpdatedAt ? null : updatedAt ?? from.UpdatedAt);
    }

    private static ImportSnapshot Live(Guid id) => Forest().First(c => c.Id == id);

    private static ImportRowPlan Only(ImportPlan plan) => plan.Rows.Single();

    // ---- reading a row on its own -----------------------------------------------------------

    [Fact]
    public void AnUntouchedRowChangesNothing()
    {
        var plan = CategoryImportPlanner.Plan([Row(Live(Lighting))], Forest());

        Assert.Equal(ImportRowState.Unchanged, Only(plan).State);
        Assert.Empty(plan.Steps);
    }

    [Fact]
    public void AnUntouchedRowIsStillUntouchedWhenTheStoredValueIsAnEmptyString()
    {
        // MetaDescription is stored exactly as given, so it can hold "" where the export writes
        // an empty cell and the reader gives back null. Compared naively that is a change on
        // every import, of every such row, for ever — which is how a three-row import quietly
        // becomes a four-row one.
        var live = Forest();
        live[1] = live[1] with { MetaDescription = string.Empty, Name = "Lighting " };

        var plan = CategoryImportPlanner.Plan([Row(Live(Lighting))], live);

        Assert.Equal(ImportRowState.Unchanged, Only(plan).State);
        Assert.Empty(plan.Steps);
    }

    [Fact]
    public void ARenameIsReportedAsOneChange()
    {
        var plan = CategoryImportPlanner.Plan([Row(Live(Lighting), name: "Lights")], Forest());

        var row = Only(plan);
        Assert.Equal(ImportRowState.Changed, row.State);
        Assert.Equal(["name: Lighting -> Lights"], row.Changes);
        Assert.Single(plan.Steps);
    }

    [Fact]
    public void AChangedParentIsDescribedByRouteRatherThanId()
    {
        // What the person pressing Apply actually reads. A raw guid here would be useless.
        var plan = CategoryImportPlanner.Plan(
            [Row(Live(Trees), parentRoute: "christmas-lighting")], Forest());

        var row = Only(plan);
        Assert.Equal(ImportRowState.Changed, row.State);
        Assert.Equal(["parent: christmas -> christmas-lighting"], row.Changes);
        Assert.True(plan.Steps.Single().Reparents);
        Assert.Equal(Lighting, plan.Steps.Single().NewParentId);
    }

    [Fact]
    public void MovingToTheTopLevelIsSaidWithAnEmptyParent()
    {
        var plan = CategoryImportPlanner.Plan([Row(Live(Lighting), parentRoute: "")], Forest());

        Assert.Equal(ImportRowState.Changed, Only(plan).State);
        Assert.Null(plan.Steps.Single().NewParentId);
        Assert.True(plan.Steps.Single().Reparents);
    }

    [Fact]
    public void ARowWhoseCategoryIsGoneIsRefused()
    {
        var stranger = Snap(Guid.NewGuid(), "Ghost", "ghost", "ghost", null);
        var plan = CategoryImportPlanner.Plan([Row(stranger)], Forest());

        Assert.Equal(ImportRowState.Refused, Only(plan).State);
        Assert.Contains("deleted", Only(plan).Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheSameCategoryTwiceRefusesTheSecondCopy()
    {
        // A pasted row would otherwise be applied twice, with whichever came last quietly winning.
        var plan = CategoryImportPlanner.Plan(
            [Row(Live(Lighting), number: 2, name: "First"), Row(Live(Lighting), number: 9, name: "Second")],
            Forest());

        Assert.Equal(ImportRowState.Changed, plan.Rows[0].State);
        Assert.Equal(ImportRowState.Refused, plan.Rows[1].State);
        Assert.Contains("row 2", plan.Rows[1].Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void ARowEditedInThePanelSinceTheExportIsRefusedOnItsOwn()
    {
        var live = Forest();
        live[1] = live[1] with { UpdatedAt = Exported.AddMinutes(5) };

        var plan = CategoryImportPlanner.Plan(
            [Row(Live(Lighting), name: "Lights"), Row(Live(Trees), number: 3, name: "Firs")],
            live);

        Assert.Equal(ImportRowState.Stale, plan.Rows[0].State);

        // The neighbour still goes through — that is the whole point of refusing per row.
        Assert.Equal(ImportRowState.Changed, plan.Rows[1].State);
        Assert.Single(plan.Steps);
    }

    [Fact]
    public void EncodingDriftIsNotTreatedAsAnEdit()
    {
        // A timestamp that has been through a workbook is a float and comes back a hair off.
        var live = Forest();
        live[1] = live[1] with { UpdatedAt = Exported.AddMilliseconds(80) };

        var plan = CategoryImportPlanner.Plan([Row(Live(Lighting), name: "Lights")], live);

        Assert.Equal(ImportRowState.Changed, Only(plan).State);
    }

    [Fact]
    public void ARowWithNoTimestampIsRefusedRatherThanAssumedCurrent()
    {
        var plan = CategoryImportPlanner.Plan(
            [Row(Live(Lighting), name: "Lights", clearUpdatedAt: true)], Forest());

        Assert.Equal(ImportRowState.Refused, Only(plan).State);
    }

    [Fact]
    public void AParentRouteThatMatchesNothingIsRefused()
    {
        var plan = CategoryImportPlanner.Plan(
            [Row(Live(Trees), parentRoute: "easter")], Forest());

        Assert.Equal(ImportRowState.Refused, Only(plan).State);
        Assert.Contains("easter", Only(plan).Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOverLongNameIsRefusedRatherThanLeftToTheDatabase()
    {
        // The length limits live on the DTOs, which a workbook never passes through.
        var plan = CategoryImportPlanner.Plan(
            [Row(Live(Trees), name: new string('x', 201))], Forest());

        Assert.Equal(ImportRowState.Refused, Only(plan).State);
    }

    [Fact]
    public void ABlankSlugIsDerivedFromTheNameTheSameWayTheApiWouldDeriveIt()
    {
        var plan = CategoryImportPlanner.Plan(
            [Row(Live(Trees), name: "Christmas Light's", slug: "")], Forest());

        Assert.Equal(ImportRowState.Changed, Only(plan).State);
        Assert.Equal("christmas_lights", plan.Steps.Single().Slug);
    }

    // ---- judging the batch as a whole -------------------------------------------------------

    [Fact]
    public void MovingACategoryUnderItsOwnChildIsRefused()
    {
        var plan = CategoryImportPlanner.Plan(
            [Row(Live(Lighting), parentRoute: "christmas-lighting-indoor")], Forest());

        Assert.Equal(ImportRowState.Refused, Only(plan).State);
        Assert.Contains("own subtree", Only(plan).Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoRowsClaimingOneRouteAreNotBothApplied()
    {
        var plan = CategoryImportPlanner.Plan(
            [Row(Live(Trees), number: 2, slug: "conifers"), Row(Live(Lighting), number: 3, slug: "conifers")],
            Forest());

        Assert.Contains(plan.Rows, r => r.State == ImportRowState.Refused);
        Assert.Single(plan.Steps);
    }

    [Fact]
    public void ACollisionBlamesTheRowThatMovedOntoTheRouteNotTheOneAlreadyThere()
    {
        // Halloween takes Christmas's route while Christmas is edited for something unrelated.
        // Blaming whichever came first in the file would cost Christmas its meta title for a
        // mistake made two sheets away.
        var plan = CategoryImportPlanner.Plan(
            [Row(Live(Christmas), number: 2, metaTitle: "Rewritten in Excel"),
             Row(Live(Halloween), number: 9, slug: "christmas")],
            Forest());

        Assert.Equal(ImportRowState.Changed, plan.Rows[0].State);
        Assert.Equal(ImportRowState.Refused, plan.Rows[1].State);
        Assert.Equal(Christmas, plan.Steps.Single().Id);
    }

    [Fact]
    public void ARenameThatCollidesADescendantIsCaughtBeforeAnythingIsWritten()
    {
        // The failure this exists for. Renaming Halloween to "christmas" makes its own route
        // clash — but the nastier version is a rename that only collides three levels down, which
        // UpdateAsync never checks and which surfaces as a 500 from inside a raw SQL UPDATE.
        var live = Forest();
        live.Add(Snap(new Guid("00000000-0000-0000-0000-0000000000c6"),
            "Indoor", "indoor", "halloween-indoor", Halloween));

        var plan = CategoryImportPlanner.Plan(
            [Row(Live(Lighting), slug: "halloween", parentRoute: "")], live);

        // christmas-lighting-indoor would become halloween-indoor, which already exists.
        Assert.Equal(ImportRowState.Refused, Only(plan).State);
        Assert.Contains("two categories at once", Only(plan).Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void ARedirectToACategoryThatItselfRedirectsIsRefused()
    {
        var live = Forest();
        live[3] = live[3] with { RedirectToId = Halloween };

        var plan = CategoryImportPlanner.Plan(
            [Row(Live(Lighting), redirectRoute: "christmas-trees")], live);

        Assert.Equal(ImportRowState.Refused, Only(plan).State);
        Assert.Contains("one hop", Only(plan).Reason!, StringComparison.Ordinal);
    }

    // ---- getting there from here ------------------------------------------------------------

    [Fact]
    public void ParentsAreWrittenBeforeTheirChildren()
    {
        // A child's route is built on its parent's, so a parent settled second would have the
        // child computing its route from a value that is about to change.
        var plan = CategoryImportPlanner.Plan(
            [Row(Live(Indoor), number: 4, name: "Inside"), Row(Live(Lighting), number: 3, name: "Lights")],
            Forest());

        Assert.Equal(2, plan.Steps.Count);
        Assert.Equal(Lighting, plan.Steps[0].Id);
        Assert.Equal(Indoor, plan.Steps[1].Id);
    }

    [Fact]
    public void ARowWaitsForTheOneOccupyingTheRouteItWants()
    {
        // Trees moves out to the top level and Lighting takes its old route. Both are fine, but
        // only in that order — the other way round the rename lands on an occupied route.
        var plan = CategoryImportPlanner.Plan(
            [Row(Live(Lighting), number: 2, slug: "trees"), Row(Live(Trees), number: 3, parentRoute: "")],
            Forest());

        Assert.All(plan.Rows, r => Assert.Equal(ImportRowState.Changed, r.State));
        Assert.Equal(2, plan.Steps.Count);
        Assert.Equal(Trees, plan.Steps[0].Id);
        Assert.Equal(Lighting, plan.Steps[1].Id);
    }

    [Fact]
    public void TwoSiblingsTradingRoutesIsReportedRatherThanHalfApplied()
    {
        // No ordering can do this in one pass: whichever moves first lands on the other. Applying
        // one of them would leave the tree in a state nobody asked for, so neither is attempted.
        var plan = CategoryImportPlanner.Plan(
            [Row(Live(Lighting), number: 2, slug: "trees"), Row(Live(Trees), number: 3, slug: "lighting")],
            Forest());

        Assert.All(plan.Rows, r => Assert.Equal(ImportRowState.Blocked, r.State));
        Assert.Empty(plan.Steps);
        Assert.Contains("Import the other change first", plan.Rows[0].Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnchangedRowNeverBecomesAStep()
    {
        var plan = CategoryImportPlanner.Plan(
            [Row(Live(Christmas)), Row(Live(Lighting), number: 3, name: "Lights"), Row(Live(Trees), number: 4)],
            Forest());

        Assert.Equal(2, plan.Unchanged);
        Assert.Equal(1, plan.Changed);
        Assert.Equal(Lighting, plan.Steps.Single().Id);
    }

    [Fact]
    public void AStepSaysWhetherItMovesTheCategoryOrJustEditsIt()
    {
        var plan = CategoryImportPlanner.Plan(
            [Row(Live(Trees), name: "Firs", parentRoute: "halloween")], Forest());

        var step = plan.Steps.Single();
        Assert.True(step.Reparents);
        Assert.True(step.ChangesValues);
        Assert.Equal(Halloween, step.NewParentId);
        Assert.Equal("Firs", step.Name);
    }

    [Fact]
    public void AnEmptyWorkbookIsAnEmptyPlan()
    {
        var plan = CategoryImportPlanner.Plan([], Forest());

        Assert.Empty(plan.Rows);
        Assert.Empty(plan.Steps);
    }
}
