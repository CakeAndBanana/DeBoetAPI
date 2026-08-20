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
}

public sealed record CategoryResult(CategoryError Error, Category? Category = null)
{
    public bool Succeeded => Error == CategoryError.None;

    public static CategoryResult Ok(Category category) => new(CategoryError.None, category);

    public static CategoryResult Fail(CategoryError error) => new(error);
}

/// <summary>
/// Owns every mutation of the category hierarchy. <see cref="Category.Path"/> and
/// <see cref="Category.Depth"/> are derived state — they are only ever written here, always
/// inside a transaction, so a half-applied move cannot leave the tree inconsistent.
/// </summary>
public sealed class CategoryTreeService(AppDbContext db, AuditLog audit)
{
    /// <summary>Path segment separator. Ids are wrapped in it so prefix matching cannot straddle two ids.</summary>
    private const char Separator = '/';

    /// <summary>Level delimiter in <see cref="Category.PathSlug"/>. Never appears inside a segment.</summary>
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

        var finalSlug = Slugify(string.IsNullOrWhiteSpace(slug) ? name : slug);
        var pathSlug = BuildPathSlug(parent, finalSlug);

        if (await db.Categories.AnyAsync(c => c.PathSlug == pathSlug, ct))
        {
            return CategoryResult.Fail(CategoryError.DuplicateSlug);
        }

        var category = new Category
        {
            Name = name,
            Slug = finalSlug,
            PathSlug = pathSlug,
            // Blank stays blank. A meta title that merely repeats the name tells a search
            // engine nothing it cannot already read from the page, so there is no fallback —
            // same reasoning as MetaDescription below.
            MetaTitle = string.IsNullOrWhiteSpace(metaTitle) ? null : metaTitle,
            MetaDescription = metaDescription,
            ParentId = parentId,
            SortOrder = sortOrder,
            IsActive = isActive,
            IsMenu = isMenu,
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
        CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null)
        {
            return CategoryResult.Fail(CategoryError.NotFound);
        }

        var finalSlug = Slugify(string.IsNullOrWhiteSpace(slug) ? name : slug);

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

        // Captured before mutation so the trail records what actually changed.
        var summary = AuditLog.Diff(
            ("name", category.Name, name),
            ("slug", category.Slug, finalSlug),
            ("metaTitle", category.MetaTitle, string.IsNullOrWhiteSpace(metaTitle) ? null : metaTitle),
            ("metaDescription", category.MetaDescription, metaDescription),
            ("sortOrder", category.SortOrder, sortOrder),
            ("isActive", category.IsActive, isActive),
            ("isMenu", category.IsMenu, isMenu));

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
    public async Task<CategoryResult> MoveAsync(Guid id, Guid? newParentId, CancellationToken ct)
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

            // The target sitting inside this subtree would detach the branch from every root.
            if (IsDescendantOf(newParent, category))
            {
                return CategoryResult.Fail(CategoryError.Cycle);
            }
        }

        if (category.ParentId == newParentId)
        {
            return CategoryResult.Ok(category);
        }

        var oldPath = category.Path;
        var oldPathSlug = category.PathSlug;
        var newPath = BuildPath(newParent, category.Id);
        var newPathSlug = BuildPathSlug(newParent, category.Slug);
        var depthDelta = (newParent is null ? 0 : newParent.Depth + 1) - category.Depth;

        // Moving relocates the whole route, so the destination may already hold that name.
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

        // Scoped by the new id path: the statement above has already rewritten descendants.
        if (oldPathSlug != newPathSlug)
        {
            await RewriteSubtreeSlugsAsync(newPath, oldPathSlug, newPathSlug, ct);
        }

        await transaction.CommitAsync(ct);
        return CategoryResult.Ok(category);
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
        // Loaded before the delete so the audit entry can name both ends of the link.
        var child = await db.Categories.FirstOrDefaultAsync(c => c.Id == childId, ct);
        var parent = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == parentId, ct);

        // ExecuteDeleteAsync bypasses the change tracker and commits on its own, so it is
        // wrapped alongside the audit write rather than left to run independently.
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

    /// <summary>
    /// Re-roots the slug routes of everything below a node whose own route just changed.
    /// Scoped by the id path rather than by the slug prefix, so a sibling called
    /// <c>christmasx</c> can never be swept up by a prefix match on <c>christmas</c>.
    /// </summary>
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
    /// Lowercases and strips everything that is not a letter or digit, so "Christmas Lights"
    /// becomes "christmaslights". Separators are removed rather than replaced with a dash,
    /// because the dash delimits levels in <see cref="Category.PathSlug"/> — a dash inside a
    /// segment would make <c>christmas-christmaslights-icelights</c> impossible to split back.
    /// </summary>
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
