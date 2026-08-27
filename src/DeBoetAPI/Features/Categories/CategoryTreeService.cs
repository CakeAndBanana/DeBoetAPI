using DeBoetAPI.Common;
using DeBoetAPI.Data;
using DeBoetAPI.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeBoetAPI.Features.Categories;

public enum CategoryError
{
    None,
    NotFound,
    ParentNotFound,
    DuplicateSlug,
    Cycle,
    SelfReference,
    DuplicateLink,
    LinkDuplicatesMainParent,
    RedirectNotFound,
    RedirectChain,
}

public sealed record CategoryResult(CategoryError Error, Category? Category = null)
{
    public bool Succeeded => Error == CategoryError.None;

    public static CategoryResult Ok(Category category) => new(CategoryError.None, category);

    public static CategoryResult Fail(CategoryError error) => new(error);
}

public sealed class CategoryTreeService(AppDbContext db, AuditLog audit)
{
    private const char Separator = '/';

    public const char PathSlugSeparator = '-';

    public async Task<CategoryResult> CreateAsync(
        string name,
        string? slug,
        string? metaTitle,
        string? metaDescription,
        Guid? parentId,
        int sortOrder,
        bool isActive,
        bool isMenu,
        Guid? redirectToId,
        CancellationToken ct)
    {
        Category? parent = null;
        if (parentId is not null)
        {
            parent = await db.Categories.FirstOrDefaultAsync(c => c.Id == parentId, ct);
            if (parent is null)
            {
                return CategoryResult.Fail(CategoryError.ParentNotFound);
            }
        }

        var finalSlug = ResolveSlug(slug, name);
        var pathSlug = BuildPathSlug(parent, finalSlug);

        if (await db.Categories.AnyAsync(c => c.PathSlug == pathSlug, ct))
        {
            return CategoryResult.Fail(CategoryError.DuplicateSlug);
        }

        // Guid.Empty stands for "nothing yet": the category has no id of its own to clash with
        // until it is created, and a brand new one cannot already be a redirect destination.
        var redirectError = await ValidateRedirectAsync(Guid.Empty, redirectToId, ct);
        if (redirectError != CategoryError.None)
        {
            return CategoryResult.Fail(redirectError);
        }

        var category = new Category
        {
            Name = name,
            Slug = finalSlug,
            PathSlug = pathSlug,
            MetaTitle = string.IsNullOrWhiteSpace(metaTitle) ? null : metaTitle,
            MetaDescription = metaDescription,
            ParentId = parentId,
            SortOrder = sortOrder,
            IsActive = isActive,
            IsMenu = isMenu,
            RedirectToId = redirectToId,
            CreatedById = audit.ActorId,
            UpdatedById = audit.ActorId,
        };

        category.Path = BuildPath(parent, category.Id);
        category.Depth = parent is null ? 0 : parent.Depth + 1;

        db.Categories.Add(category);
        audit.RecordCategory(category, AuditAction.Created, $"created at {category.PathSlug}");
        await db.SaveChangesAsync(ct);

        return CategoryResult.Ok(category);
    }

    public async Task<CategoryResult> UpdateAsync(
        Guid id,
        string name,
        string? slug,
        string? metaTitle,
        string? metaDescription,
        int sortOrder,
        bool isActive,
        bool isMenu,
        Guid? redirectToId,
        CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null)
        {
            return CategoryResult.Fail(CategoryError.NotFound);
        }

        var finalSlug = ResolveSlug(slug, name);

        var parent = category.ParentId is null
            ? null
            : await db.Categories.FirstOrDefaultAsync(c => c.Id == category.ParentId, ct);

        var oldPathSlug = category.PathSlug;
        var newPathSlug = BuildPathSlug(parent, finalSlug);

        if (newPathSlug != oldPathSlug
            && await db.Categories.AnyAsync(c => c.PathSlug == newPathSlug && c.Id != id, ct))
        {
            return CategoryResult.Fail(CategoryError.DuplicateSlug);
        }

        var redirectError = await ValidateRedirectAsync(id, redirectToId, ct);
        if (redirectError != CategoryError.None)
        {
            return CategoryResult.Fail(redirectError);
        }

        // Captured before mutation so the trail records what actually changed.
        var summary = AuditLog.Diff(
            ("name", category.Name, name),
            ("slug", category.Slug, finalSlug),
            ("metaTitle", category.MetaTitle, string.IsNullOrWhiteSpace(metaTitle) ? null : metaTitle),
            ("metaDescription", category.MetaDescription, metaDescription),
            ("sortOrder", category.SortOrder, sortOrder),
            ("isActive", category.IsActive, isActive),
            ("isMenu", category.IsMenu, isMenu),
            ("redirectTo", category.RedirectToId, redirectToId));

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        category.Name = name;
        category.Slug = finalSlug;
        category.PathSlug = newPathSlug;
        category.MetaTitle = string.IsNullOrWhiteSpace(metaTitle) ? null : metaTitle;
        // Neither field falls back to Name: a one-word meta description is worse for SEO than
        // none at all, and search engines synthesise a better snippet.
        category.MetaDescription = metaDescription;
        category.SortOrder = sortOrder;
        category.IsActive = isActive;
        category.IsMenu = isMenu;
        category.RedirectToId = redirectToId;
        category.UpdatedAt = DateTimeOffset.UtcNow;
        category.UpdatedById = audit.ActorId;

        if (summary is not null)
        {
            audit.RecordCategory(category, AuditAction.Updated, summary);
        }

        await db.SaveChangesAsync(ct);

        if (oldPathSlug != newPathSlug)
        {
            await RewriteSubtreeSlugsAsync(category.Path, oldPathSlug, newPathSlug, ct);
        }

        await transaction.CommitAsync(ct);
        return CategoryResult.Ok(category);
    }

    /// <summary>Re-parents a category and rewrites its whole subtree's paths and depths.</summary>
    public async Task<CategoryResult> MoveAsync(
        Guid id,
        Guid? newParentId,
        int? position,
        CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null)
        {
            return CategoryResult.Fail(CategoryError.NotFound);
        }

        if (newParentId == id)
        {
            return CategoryResult.Fail(CategoryError.SelfReference);
        }

        Category? newParent = null;
        if (newParentId is not null)
        {
            newParent = await db.Categories.FirstOrDefaultAsync(c => c.Id == newParentId, ct);
            if (newParent is null)
            {
                return CategoryResult.Fail(CategoryError.ParentNotFound);
            }

            if (IsDescendantOf(newParent, category))
            {
                return CategoryResult.Fail(CategoryError.Cycle);
            }
        }

        if (category.ParentId == newParentId)
        {
            if (position is null)
            {
                return CategoryResult.Ok(category);
            }

            await PlaceAsync(category, position.Value, ct);
            await db.SaveChangesAsync(ct);
            return CategoryResult.Ok(category);
        }

        var oldPath = category.Path;
        var oldPathSlug = category.PathSlug;
        var newPath = BuildPath(newParent, category.Id);
        var newPathSlug = BuildPathSlug(newParent, category.Slug);
        var depthDelta = (newParent is null ? 0 : newParent.Depth + 1) - category.Depth;

        if (newPathSlug != oldPathSlug
            && await db.Categories.AnyAsync(c => c.PathSlug == newPathSlug && c.Id != id, ct))
        {
            return CategoryResult.Fail(CategoryError.DuplicateSlug);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        category.ParentId = newParentId;
        category.Path = newPath;
        category.PathSlug = newPathSlug;
        category.Depth += depthDelta;
        category.UpdatedAt = DateTimeOffset.UtcNow;
        category.UpdatedById = audit.ActorId;
        audit.RecordCategory(category, AuditAction.Moved, $"route: {oldPathSlug} -> {newPathSlug}");
        await db.SaveChangesAsync(ct);

        await RewriteSubtreeAsync(oldPath, newPath, depthDelta, ct);

        if (oldPathSlug != newPathSlug)
        {
            await RewriteSubtreeSlugsAsync(newPath, oldPathSlug, newPathSlug, ct);
        }

        await PlaceAsync(category, position ?? int.MaxValue, ct);
        await db.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);
        return CategoryResult.Ok(category);
    }

    /// <summary>
    /// Puts a category at a given position among its siblings and renumbers all of them from
    /// zero, so the order stays gapless however many times it is rearranged.
    /// </summary>
    private async Task PlaceAsync(Category category, int position, CancellationToken ct)
    {
        var siblings = await db.Categories
            .Where(c => c.ParentId == category.ParentId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(ct);

        var from = siblings.FindIndex(c => c.Id == category.Id);
        siblings.RemoveAll(c => c.Id == category.Id);

        var to = Math.Clamp(position, 0, siblings.Count);
        siblings.Insert(to, category);

        for (var i = 0; i < siblings.Count; i++)
        {
            siblings[i].SortOrder = i;
        }

        if (from != to)
        {
            category.UpdatedAt = DateTimeOffset.UtcNow;
            category.UpdatedById = audit.ActorId;
            audit.RecordCategory(category, AuditAction.Reordered,
                from < 0 ? $"placed at position {to}" : $"position: {from} -> {to}");
        }
    }

    /// <summary>
    /// Deletes a category, promoting its direct children to the deleted node's parent so no
    /// branch is lost. Returns the ids of the promoted children.
    /// </summary>
    public async Task<(CategoryError Error, IReadOnlyList<Guid> PromotedChildIds)> DeleteAsync(
        Guid id,
        CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null)
        {
            return (CategoryError.NotFound, []);
        }

        var newParent = category.ParentId is null
            ? null
            : await db.Categories.FirstOrDefaultAsync(c => c.Id == category.ParentId, ct);

        var children = await db.Categories.Where(c => c.ParentId == id).ToListAsync(ct);

        foreach (var child in children)
        {
            var candidate = BuildPathSlug(newParent, child.Slug);
            if (await db.Categories.AnyAsync(c => c.PathSlug == candidate && c.Id != child.Id, ct))
            {
                return (CategoryError.DuplicateSlug, []);
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        foreach (var child in children)
        {
            var oldPath = child.Path;
            var oldPathSlug = child.PathSlug;
            var newPath = BuildPath(newParent, child.Id);
            var newPathSlug = BuildPathSlug(newParent, child.Slug);
            var depthDelta = (newParent is null ? 0 : newParent.Depth + 1) - child.Depth;

            child.ParentId = category.ParentId;
            child.Path = newPath;
            child.PathSlug = newPathSlug;
            child.Depth += depthDelta;
            child.UpdatedAt = DateTimeOffset.UtcNow;
            child.UpdatedById = audit.ActorId;
            audit.RecordCategory(child, AuditAction.ChildPromoted,
                $"promoted to {(newParent is null ? "root" : newParent.Name)} after '{category.Name}' was deleted");
            await db.SaveChangesAsync(ct);

            await RewriteSubtreeAsync(oldPath, newPath, depthDelta, ct);

            if (oldPathSlug != newPathSlug)
            {
                await RewriteSubtreeSlugsAsync(newPath, oldPathSlug, newPathSlug, ct);
            }
        }

        // Recorded before removal so the snapshot still has the name and route, and committed
        // in the same transaction - the deletion is the event most worth being able to look up.
        audit.RecordCategory(category, AuditAction.Deleted,
            children.Count == 0 ? null : $"{children.Count} child(ren) promoted");
        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);
        return (CategoryError.None, children.Select(c => c.Id).ToList());
    }

    public async Task<CategoryError> AddLinkAsync(Guid childId, Guid parentId, CancellationToken ct)
    {
        if (childId == parentId)
        {
            return CategoryError.SelfReference;
        }

        var child = await db.Categories.FirstOrDefaultAsync(c => c.Id == childId, ct);
        if (child is null)
        {
            return CategoryError.NotFound;
        }

        var parent = await db.Categories.FirstOrDefaultAsync(c => c.Id == parentId, ct);
        if (parent is null)
        {
            return CategoryError.ParentNotFound;
        }

        if (child.ParentId == parentId)
        {
            return CategoryError.LinkDuplicatesMainParent;
        }

        if (IsDescendantOf(parent, child))
        {
            return CategoryError.Cycle;
        }

        if (await db.CategoryLinks.AnyAsync(l => l.ChildId == childId && l.ParentId == parentId, ct))
        {
            return CategoryError.DuplicateLink;
        }

        db.CategoryLinks.Add(new CategoryLink { ChildId = childId, ParentId = parentId });
        audit.RecordCategory(child, AuditAction.LinkAdded, $"also placed under '{parent.Name}'");
        await db.SaveChangesAsync(ct);
        return CategoryError.None;
    }

    public async Task<CategoryError> RemoveLinkAsync(Guid childId, Guid parentId, CancellationToken ct)
    {
        var child = await db.Categories.FirstOrDefaultAsync(c => c.Id == childId, ct);
        var parent = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == parentId, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var removed = await db.CategoryLinks
            .Where(l => l.ChildId == childId && l.ParentId == parentId)
            .ExecuteDeleteAsync(ct);

        if (removed == 0)
        {
            await transaction.RollbackAsync(ct);
            return CategoryError.NotFound;
        }

        if (child is not null)
        {
            audit.RecordCategory(child, AuditAction.LinkRemoved,
                $"no longer placed under '{parent?.Name ?? parentId.ToString()}'");
            await db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return CategoryError.None;
    }

    /// <summary>Ancestors of a category, root first. Read straight out of the path — no recursion.</summary>
    public async Task<IReadOnlyList<Category>> GetAncestorsAsync(Category category, CancellationToken ct)
    {
        var ids = ParsePath(category.Path).Where(i => i != category.Id).ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var ancestors = await db.Categories
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToListAsync(ct);

        return [.. ancestors.OrderBy(c => c.Depth)];
    }

    /// <summary>Every descendant of a category, via a single indexed prefix scan.</summary>
    public IQueryable<Category> DescendantsOf(Category category, bool includeSelf) =>
        includeSelf
            ? db.Categories.Where(c => c.Path.StartsWith(category.Path))
            : db.Categories.Where(c => c.Path.StartsWith(category.Path) && c.Id != category.Id);

    /// <summary>Rewrites the paths and depths of everything strictly below a moved node.</summary>
    private async Task RewriteSubtreeAsync(string oldPath, string newPath, int depthDelta, CancellationToken ct)
    {
        // One statement for the whole subtree — the alternative is loading and saving every
        // descendant, which turns a deep branch into hundreds of round-trips.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE categories
             SET path = {newPath} || substring(path from {oldPath.Length + 1}),
                 depth = depth + {depthDelta},
                 updated_at = now()
             WHERE path LIKE {oldPath + "%"} AND path <> {oldPath}
             """, ct);
    }

    private async Task RewriteSubtreeSlugsAsync(
        string subtreeIdPath,
        string oldPathSlug,
        string newPathSlug,
        CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE categories
             SET path_slug = {newPathSlug} || substring(path_slug from {oldPathSlug.Length + 1}),
                 updated_at = now()
             WHERE path LIKE {subtreeIdPath + "%"} AND path <> {subtreeIdPath}
             """, ct);
    }

    private static bool IsDescendantOf(Category candidate, Category ancestor) =>
        candidate.Path.StartsWith(ancestor.Path, StringComparison.Ordinal);

    private static string BuildPath(Category? parent, Guid id) =>
        parent is null ? $"{Separator}{id}{Separator}" : $"{parent.Path}{id}{Separator}";

    private static IEnumerable<Guid> ParsePath(string path) =>
        path.Split(Separator, StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => Guid.TryParse(segment, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty);

    /// <summary>
    /// Checks a proposed redirect.
    /// <para>
    /// Redirects are kept to a single hop, enforced from both ends: a category may not point at
    /// one that already forwards, and one that is already a destination may not be given a
    /// redirect of its own. That is what makes a cycle unbuildable rather than merely detected,
    /// and it means a consumer reads one field instead of walking a chain.
    /// </para>
    /// </summary>
    private async Task<CategoryError> ValidateRedirectAsync(
        Guid categoryId,
        Guid? redirectToId,
        CancellationToken ct)
    {
        if (redirectToId is null)
        {
            return CategoryError.None;
        }

        if (redirectToId == categoryId)
        {
            return CategoryError.SelfReference;
        }

        var target = await db.Categories.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == redirectToId, ct);

        if (target is null)
        {
            return CategoryError.RedirectNotFound;
        }

        if (target.RedirectToId is not null)
        {
            return CategoryError.RedirectChain;
        }

        var isItselfADestination = categoryId != Guid.Empty
            && await db.Categories.AnyAsync(c => c.RedirectToId == categoryId, ct);

        return isItselfADestination ? CategoryError.RedirectChain : CategoryError.None;
    }

    private static string ResolveSlug(string? slug, string name) =>
        string.IsNullOrWhiteSpace(slug) ? Slugify(name) : NormalizeSlug(slug);

    public static string NormalizeSlug(string value)
    {
        var cleaned = new string([.. value
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : PathSlugSeparator)]);

        var segments = cleaned.Split(PathSlugSeparator, StringSplitOptions.RemoveEmptyEntries);
        var slug = string.Join(PathSlugSeparator, segments);

        return slug.Length == 0
            ? Guid.CreateVersion7().ToString("N")[..12]
            : slug[..Math.Min(slug.Length, 200)];
    }

    /// <summary>Derives a segment from a name. Separators are stripped, never turned into
    /// hyphens, because a hyphen delimits levels in the full route.</summary>
    public static string Slugify(string value)
    {
        var slug = new string([.. value
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)]);

        return slug.Length == 0
            ? Guid.CreateVersion7().ToString("N")[..12]
            : slug[..Math.Min(slug.Length, 200)];
    }

    /// <summary>Joins a parent's route with a child segment: <c>christmas</c> + <c>icelights</c>.</summary>
    private static string BuildPathSlug(Category? parent, string slug) =>
        parent is null ? slug : $"{parent.PathSlug}{PathSlugSeparator}{slug}";
}
