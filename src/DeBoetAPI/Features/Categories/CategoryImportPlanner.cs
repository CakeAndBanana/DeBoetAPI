namespace DeBoetAPI.Features.Categories;

/// <summary>What the importer decided about one row.</summary>
public enum ImportRowState
{
    /// <summary>Not a category row at all — a reference row, or one with no id.</summary>
    Ignored,

    /// <summary>Read, understood, and identical to what is already stored.</summary>
    Unchanged,

    /// <summary>Will be applied.</summary>
    Changed,

    /// <summary>The category has been edited since the workbook was exported.</summary>
    Stale,

    /// <summary>Cannot be applied at all — see the reason.</summary>
    Refused,

    /// <summary>Valid, but not reachable in one pass because other rows are in the way.</summary>
    Blocked,
}

/// <summary>One row as read out of the workbook, before any judgement is made about it.</summary>
public sealed record ImportRow(
    string Sheet,
    int Number,
    Guid Id,
    string Name,
    string? Slug,
    string? ParentRoute,
    string? RedirectRoute,
    bool IsActive,
    bool IsMenu,
    int SortOrder,
    string? MetaTitle,
    string? MetaDescription,
    DateTimeOffset? UpdatedAt,
    /// <summary>
    /// Set when the cell could not be read at all — a Yes/No column saying something else, a
    /// number column holding text. Carried rather than thrown so one unreadable cell refuses its
    /// own row instead of the whole workbook.
    /// </summary>
    string? Problem = null);

/// <summary>The live state of one category, as much of it as the planner needs.</summary>
public sealed record ImportSnapshot(
    Guid Id,
    string Name,
    string Slug,
    string PathSlug,
    Guid? ParentId,
    Guid? RedirectToId,
    int SortOrder,
    bool IsActive,
    bool IsMenu,
    string? MetaTitle,
    string? MetaDescription,
    DateTimeOffset UpdatedAt);

/// <summary>What the planner decided about one row, and why.</summary>
public sealed record ImportRowPlan(
    string Sheet,
    int Number,
    Guid Id,
    string Name,
    ImportRowState State,
    IReadOnlyList<string> Changes,
    string? Reason);

/// <summary>One category to write, and what about it changed.</summary>
public sealed record ImportStep(
    Guid Id,
    bool Reparents,
    Guid? NewParentId,
    string Name,
    string? Slug,
    string? MetaTitle,
    string? MetaDescription,
    int SortOrder,
    bool IsActive,
    bool IsMenu,
    Guid? RedirectToId,
    bool ChangesValues);

/// <summary>Everything that would happen, in the order it has to happen.</summary>
public sealed record ImportPlan(IReadOnlyList<ImportRowPlan> Rows, IReadOnlyList<ImportStep> Steps)
{
    public int Changed => Rows.Count(r => r.State == ImportRowState.Changed);
    public int Unchanged => Rows.Count(r => r.State == ImportRowState.Unchanged);
    public int Stale => Rows.Count(r => r.State == ImportRowState.Stale);
    public int Refused => Rows.Count(r => r.State == ImportRowState.Refused);
    public int Blocked => Rows.Count(r => r.State == ImportRowState.Blocked);
}

/// <summary>
/// Works out what an edited workbook would do to the tree, without touching the database.
///
/// It exists as a pure function because the interesting part is not the writing but the
/// judgement: the existing validation in <see cref="CategoryTreeService"/> is pairwise and
/// evaluated against committed state, so a batch that is collectively fine can still fail
/// half-way depending on which row happens to go first. Everything here is decided over the
/// whole batch instead, up front, where it can be reported rather than thrown.
/// </summary>
public static class CategoryImportPlanner
{
    private const int MaxName = 200;
    private const int MaxSlug = 200;
    private const int MaxMetaTitle = 200;
    private const int MaxMetaDescription = 2000;

    /// <summary>
    /// How much a timestamp may drift before it counts as an edit.
    ///
    /// Two sources, both small. Excel stores a moment as a float, which costs microseconds; and
    /// the subtree rewrites use Postgres' clock where the rest of the API uses the application's,
    /// so a descendant's timestamp carries whatever skew there is between the two. A quarter of a
    /// second covers both and is still far below anything a person could do in between.
    /// </summary>
    private static readonly TimeSpan ClockSlack = TimeSpan.FromMilliseconds(250);

    /// <summary>The deepest a parent chain may be walked before it is called a cycle.</summary>
    private const int MaxDepth = 64;

    public static ImportPlan Plan(IReadOnlyList<ImportRow> rows, IReadOnlyList<ImportSnapshot> live)
    {
        var byId = live.ToDictionary(c => c.Id);
        var byRoute = live
            .GroupBy(c => c.PathSlug, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);

        var verdicts = new Dictionary<int, ImportRowPlan>();
        var candidates = new List<Candidate>();
        var seen = new Dictionary<Guid, int>();

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];

            if (!byId.TryGetValue(row.Id, out var current))
            {
                verdicts[i] = Refuse(row, "No category with that id — it may have been deleted.");
                continue;
            }

            // A copied row would otherwise be applied twice, with the second silently winning.
            if (seen.TryGetValue(row.Id, out var first))
            {
                verdicts[i] = Refuse(row, $"The same category also appears on row {rows[first].Number}.");
                continue;
            }

            seen[row.Id] = i;

            var problem = row.Problem ?? Validate(row);
            if (problem is not null)
            {
                verdicts[i] = Refuse(row, problem);
                continue;
            }

            if (row.UpdatedAt is null)
            {
                verdicts[i] = Refuse(row, "The 'Updated (UTC)' cell is empty, so I cannot tell whether this row is still current.");
                continue;
            }

            if (current.UpdatedAt - row.UpdatedAt.Value > ClockSlack)
            {
                verdicts[i] = new ImportRowPlan(row.Sheet, row.Number, row.Id, row.Name,
                    ImportRowState.Stale, [],
                    "This category has changed since the file was exported — possibly by an earlier import of this same file. Export again to pick up the newer version.");
                continue;
            }

            Guid? parentId = null;
            if (!string.IsNullOrWhiteSpace(row.ParentRoute))
            {
                if (!byRoute.TryGetValue(row.ParentRoute.Trim(), out var found))
                {
                    verdicts[i] = Refuse(row, $"No category has the route '{row.ParentRoute.Trim()}', so there is nothing to sit under.");
                    continue;
                }

                parentId = found;
            }

            Guid? redirectId = null;
            if (!string.IsNullOrWhiteSpace(row.RedirectRoute))
            {
                if (!byRoute.TryGetValue(row.RedirectRoute.Trim(), out var found))
                {
                    verdicts[i] = Refuse(row, $"No category has the route '{row.RedirectRoute.Trim()}' to redirect to.");
                    continue;
                }

                redirectId = found;
            }

            if (parentId == row.Id)
            {
                verdicts[i] = Refuse(row, "A category cannot sit under itself.");
                continue;
            }

            candidates.Add(new Candidate(i, row, current, parentId, redirectId, ResolveSlug(row)));
        }

        // Everything above judged a row on its own. What follows judges them together: the shape
        // of the tree they add up to, and whether that shape is reachable from where we are now.
        var accepted = Settle(candidates, live, verdicts);
        var steps = Order(accepted, live, byId, verdicts);

        foreach (var candidate in accepted)
        {
            if (verdicts.ContainsKey(candidate.Index))
            {
                continue;
            }

            var changes = Describe(candidate, byId);
            verdicts[candidate.Index] = new ImportRowPlan(
                candidate.Row.Sheet, candidate.Row.Number, candidate.Row.Id, candidate.Row.Name,
                changes.Count == 0 ? ImportRowState.Unchanged : ImportRowState.Changed,
                changes,
                null);
        }

        var ordered = Enumerable.Range(0, rows.Count)
            .Select(i => verdicts.TryGetValue(i, out var v)
                ? v
                : new ImportRowPlan(rows[i].Sheet, rows[i].Number, rows[i].Id, rows[i].Name,
                    ImportRowState.Ignored, [], null))
            .ToList();

        return new ImportPlan(ordered, steps);
    }

    /// <summary>
    /// Drops candidates until what remains is a tree that could actually exist: no duplicate
    /// routes anywhere — including among descendants nobody edited — no cycles, and a redirect
    /// graph that is still one hop deep.
    ///
    /// It loops because refusing one row can make another legal again: two rows swapping onto
    /// each other's route are both at fault, but once one is gone the other is fine.
    /// </summary>
    private static List<Candidate> Settle(
        List<Candidate> candidates,
        IReadOnlyList<ImportSnapshot> live,
        Dictionary<int, ImportRowPlan> verdicts)
    {
        var accepted = candidates.ToList();

        while (accepted.Count > 0)
        {
            var world = World.From(live, accepted);

            var fault = world.FirstCycle()
                ?? world.FirstDuplicateRoute()
                ?? world.FirstRedirectFault();

            if (fault is null)
            {
                break;
            }

            // A clash usually surfaces on a category nobody touched — a descendant carried onto
            // someone else's route by an ancestor's rename. So a fault names everyone involved,
            // ancestors included, and the row refused is one that actually asked for a change.
            //
            // Of those, the one that moved its own route goes first: when a renamed category
            // lands on a settled one, the settled one is not at fault, and refusing it would
            // throw away an unrelated edit — its meta description, say — for someone else's
            // mistake. If nothing moved, the stored tree is already inconsistent and dropping
            // rows cannot help.
            var implicated = accepted.Where(c => fault.Implicates(c.Row.Id)).ToList();
            var blamed = implicated.FirstOrDefault(MovesItsRoute) ?? implicated.FirstOrDefault();

            if (blamed is null)
            {
                break;
            }

            verdicts[blamed.Index] = Refuse(blamed.Row, fault.Reason);
            accepted.Remove(blamed);
        }

        return accepted;
    }

    /// <summary>
    /// Puts the surviving changes into an order that can actually be executed.
    ///
    /// Two rows can each be perfectly valid and still collide on the way: renaming A to B's route
    /// only works once B has moved off it. Parents go before children so a child's route is built
    /// on a settled parent, and anything still blocked is deferred and retried. A pass that frees
    /// nothing means the remaining rows are waiting on each other — two siblings trading routes,
    /// say — which no ordering can fix, so they are reported rather than half-applied.
    /// </summary>
    private static List<ImportStep> Order(
        List<Candidate> accepted,
        IReadOnlyList<ImportSnapshot> live,
        IReadOnlyDictionary<Guid, ImportSnapshot> byId,
        Dictionary<int, ImportRowPlan> verdicts)
    {
        var changing = accepted.Where(c => Describe(c, byId).Count > 0).ToList();
        if (changing.Count == 0)
        {
            return [];
        }

        var world = World.From(live, []);
        var target = World.From(live, accepted);

        var pending = changing
            .OrderBy(c => target.DepthOf(c.Row.Id))
            .ThenBy(c => c.Row.Number)
            .ToList();

        var steps = new List<ImportStep>();

        while (pending.Count > 0)
        {
            var progressed = false;

            for (var i = 0; i < pending.Count; i++)
            {
                var candidate = pending[i];

                if (!world.TryApply(candidate))
                {
                    continue;
                }

                steps.Add(ToStep(candidate));
                pending.RemoveAt(i--);
                progressed = true;
            }

            if (!progressed)
            {
                break;
            }
        }

        foreach (var stuck in pending)
        {
            verdicts[stuck.Index] = new ImportRowPlan(
                stuck.Row.Sheet, stuck.Row.Number, stuck.Row.Id, stuck.Row.Name,
                ImportRowState.Blocked, Describe(stuck, byId),
                "Its route is taken by another category that is also being changed. Import the other change first, then this one.");
        }

        return steps;
    }

    /// <summary>Whether this change is what puts the category on a different route.</summary>
    private static bool MovesItsRoute(Candidate c) =>
        c.Slug != c.Current.Slug || c.ParentId != c.Current.ParentId;

    private static ImportStep ToStep(Candidate c) => new(
        c.Row.Id,
        c.ParentId != c.Current.ParentId,
        c.ParentId,
        c.Row.Name.Trim(),
        c.Slug,
        Blank(c.Row.MetaTitle),
        Blank(c.Row.MetaDescription),
        c.Row.SortOrder,
        c.Row.IsActive,
        c.Row.IsMenu,
        c.RedirectId,
        ChangesValues(c));

    /// <summary>
    /// Whether anything other than the parent differs — the parent is <c>MoveAsync</c>'s business
    /// and everything else is <c>UpdateAsync</c>'s, so a row that only moves must not drag a
    /// pointless update along behind it.
    ///
    /// Both sides are tidied identically, for the reason given in <see cref="Describe"/>.
    /// </summary>
    private static bool ChangesValues(Candidate c) =>
        c.Current.Name.Trim() != c.Row.Name.Trim()
        || c.Current.Slug != c.Slug
        || Blank(c.Current.MetaTitle) != Blank(c.Row.MetaTitle)
        || Blank(c.Current.MetaDescription) != Blank(c.Row.MetaDescription)
        || c.Current.SortOrder != c.Row.SortOrder
        || c.Current.IsActive != c.Row.IsActive
        || c.Current.IsMenu != c.Row.IsMenu
        || c.Current.RedirectToId != c.RedirectId;

    /// <summary>
    /// The changed fields, worded the way the audit log words them — so a preview and the history
    /// that follows it read the same.
    /// </summary>
    private static List<string> Describe(Candidate c, IReadOnlyDictionary<Guid, ImportSnapshot> byId)
    {
        var changes = new List<string>();

        // Both sides go through the same tidying, which is what makes an untouched row read as
        // untouched. The export writes a name indented by depth and an absent value as an empty
        // cell, so comparing a trimmed cell against an untrimmed column — or a null against a
        // stored empty string, which MetaDescription can hold — would report a change on every
        // row of every import, for ever.
        Note("name", c.Current.Name.Trim(), c.Row.Name.Trim());
        Note("slug", c.Current.Slug, c.Slug);
        Note("parent", Route(c.Current.ParentId), Route(c.ParentId));
        Note("metaTitle", Blank(c.Current.MetaTitle), Blank(c.Row.MetaTitle));
        Note("metaDescription", Blank(c.Current.MetaDescription), Blank(c.Row.MetaDescription));
        Note("sortOrder", c.Current.SortOrder, c.Row.SortOrder);
        Note("isActive", c.Current.IsActive, c.Row.IsActive);
        Note("isMenu", c.Current.IsMenu, c.Row.IsMenu);
        Note("redirectTo", Route(c.Current.RedirectToId), Route(c.RedirectId));

        return changes;

        // A route, never a raw id: this text is read by a person deciding whether to press Apply.
        string? Route(Guid? id) =>
            id is { } known && byId.TryGetValue(known, out var found) ? found.PathSlug : null;

        void Note(string field, object? before, object? after)
        {
            if (!Equals(before, after))
            {
                changes.Add($"{field}: {Render(before)} -> {Render(after)}");
            }
        }

        static string Render(object? value) => value switch
        {
            null => "(none)",
            string s when s.Length == 0 => "(empty)",
            _ => value.ToString() ?? "(none)",
        };
    }

    private static string? Validate(ImportRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Name))
        {
            return "A category must have a name.";
        }

        // These limits live on the DTOs, which a workbook never passes through — without checking
        // them here an over-long cell is a database error and a bare 500.
        if (row.Name.Trim().Length > MaxName)
        {
            return $"The name is longer than {MaxName} characters.";
        }

        if ((row.Slug?.Trim().Length ?? 0) > MaxSlug)
        {
            return $"The slug is longer than {MaxSlug} characters.";
        }

        if ((row.MetaTitle?.Length ?? 0) > MaxMetaTitle)
        {
            return $"The meta title is longer than {MaxMetaTitle} characters.";
        }

        return (row.MetaDescription?.Length ?? 0) > MaxMetaDescription
            ? $"The meta description is longer than {MaxMetaDescription} characters."
            : null;
    }

    /// <summary>Mirrors what the API does with a hand-typed slug versus a blank one.</summary>
    private static string ResolveSlug(ImportRow row) =>
        string.IsNullOrWhiteSpace(row.Slug)
            ? CategoryTreeService.Slugify(row.Name)
            : CategoryTreeService.NormalizeSlug(row.Slug);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static ImportRowPlan Refuse(ImportRow row, string reason) =>
        new(row.Sheet, row.Number, row.Id, row.Name, ImportRowState.Refused, [], reason);

    /// <summary>
    /// Something wrong with a proposed tree, and every category whose change could be the cause.
    /// </summary>
    private sealed record Fault(string Reason, HashSet<Guid> Culprits)
    {
        public bool Implicates(Guid id) => Culprits.Contains(id);
    }

    private sealed record Candidate(
        int Index,
        ImportRow Row,
        ImportSnapshot Current,
        Guid? ParentId,
        Guid? RedirectId,
        string Slug);

    /// <summary>
    /// The tree as it would be, cheap enough to rebuild whenever a candidate is dropped. Holding
    /// parents and slugs rather than routes is what lets a route be recomputed after a move —
    /// which is the whole difficulty, since moving one category rewrites the route of everything
    /// beneath it.
    /// </summary>
    private sealed class World
    {
        private readonly Dictionary<Guid, Guid?> _parent = [];
        private readonly Dictionary<Guid, string> _slug = [];
        private readonly Dictionary<Guid, Guid?> _redirect = [];

        public static World From(IReadOnlyList<ImportSnapshot> live, IReadOnlyList<Candidate> changes)
        {
            var world = new World();

            foreach (var c in live)
            {
                world._parent[c.Id] = c.ParentId;
                world._slug[c.Id] = c.Slug;
                world._redirect[c.Id] = c.RedirectToId;
            }

            foreach (var c in changes)
            {
                world._parent[c.Row.Id] = c.ParentId;
                world._slug[c.Row.Id] = c.Slug;
                world._redirect[c.Row.Id] = c.RedirectId;
            }

            return world;
        }

        public int DepthOf(Guid id)
        {
            var depth = 0;

            for (var at = _parent.GetValueOrDefault(id); at is not null && depth < MaxDepth; depth++)
            {
                at = _parent.GetValueOrDefault(at.Value);
            }

            return depth;
        }

        /// <summary>The first category that is its own ancestor, if any.</summary>
        public Fault? FirstCycle()
        {
            foreach (var id in _parent.Keys)
            {
                var ring = new HashSet<Guid> { id };
                var at = _parent.GetValueOrDefault(id);

                for (var step = 0; at is not null && step <= MaxDepth; step++)
                {
                    if (at == id)
                    {
                        return new Fault("That would put the category inside its own subtree.", ring);
                    }

                    ring.Add(at.Value);
                    at = _parent.GetValueOrDefault(at.Value);
                }
            }

            return null;
        }

        /// <summary>
        /// The first route claimed twice. Checked across every category, not just the edited
        /// ones: renaming a parent rewrites the route of its whole subtree, and it is a
        /// descendant that lands on someone else.
        /// </summary>
        public Fault? FirstDuplicateRoute()
        {
            var taken = new Dictionary<string, Guid>(StringComparer.Ordinal);

            foreach (var id in _parent.Keys)
            {
                var route = RouteOf(id);

                if (taken.TryGetValue(route, out var other))
                {
                    // Either colliding category, or any ancestor of one, could be the change that
                    // caused it — a rename three levels up moves this route just as surely.
                    var culprits = Lineage(id);
                    culprits.UnionWith(Lineage(other));

                    return new Fault($"The route '{route}' would be used by two categories at once.", culprits);
                }

                taken[route] = id;
            }

            return null;
        }

        /// <summary>Redirects stay one hop, in both directions.</summary>
        public Fault? FirstRedirectFault()
        {
            foreach (var (id, target) in _redirect)
            {
                if (target is null)
                {
                    continue;
                }

                if (target == id)
                {
                    return new Fault("A category cannot redirect to itself.", [id]);
                }

                if (_redirect.GetValueOrDefault(target.Value) is not null)
                {
                    return new Fault(
                        "Redirects are one hop only: this points at a category that itself redirects.",
                        [id, target.Value]);
                }

                foreach (var (other, otherTarget) in _redirect)
                {
                    if (other != id && otherTarget == id)
                    {
                        return new Fault(
                            "Redirects are one hop only: something already redirects here, so this cannot redirect on.",
                            [id, other]);
                    }
                }
            }

            return null;
        }

        /// <summary>A category and everything above it, since any of them can move its route.</summary>
        private HashSet<Guid> Lineage(Guid id)
        {
            var chain = new HashSet<Guid>();

            for (var at = (Guid?)id; at is not null && chain.Count <= MaxDepth;)
            {
                chain.Add(at.Value);
                at = _parent.GetValueOrDefault(at.Value);
            }

            return chain;
        }

        /// <summary>
        /// Applies one change if the tree can take it right now, and says whether it could. A
        /// refusal is not final — it means some other row has to go first.
        /// </summary>
        public bool TryApply(Candidate candidate)
        {
            var id = candidate.Row.Id;
            var parentWas = _parent[id];
            var slugWas = _slug[id];

            _parent[id] = candidate.ParentId;
            _slug[id] = candidate.Slug;

            if (FirstCycle() is null && FirstDuplicateRoute() is null)
            {
                return true;
            }

            _parent[id] = parentWas;
            _slug[id] = slugWas;
            return false;
        }

        private string RouteOf(Guid id)
        {
            var parts = new List<string>();

            for (var at = (Guid?)id; at is not null && parts.Count <= MaxDepth;)
            {
                parts.Add(_slug.GetValueOrDefault(at.Value, at.Value.ToString()));
                at = _parent.GetValueOrDefault(at.Value);
            }

            parts.Reverse();
            return string.Join(CategoryTreeService.PathSlugSeparator, parts);
        }
    }
}
