using System.Globalization;
using System.Text;
using DeBoetAPI.Common;
using DeBoetAPI.Data;
using DeBoetAPI.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DeBoetAPI.Features.Categories;

/// <summary>
/// Errors that can occur when performing operations on categories, such as creating, updating, moving, or deleting them.
/// </summary>
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

/// <summary>
/// Catergory result returned from operations on categories, such as creating, updating, moving, or deleting them.
/// </summary>
/// <param name="Error">The error that occurred, if any.</param>
/// <param name="Category">The category, if the operation was successful.</param>
public sealed record CategoryResult(CategoryError Error, Category? Category = null)
{
    public bool Succeeded => Error == CategoryError.None;

    public static CategoryResult Ok(Category category) => new(CategoryError.None, category);

    public static CategoryResult Fail(CategoryError error) => new(error);
}

/// <summary>
/// Path slug separator used to delimit segments in the full route of a category.
/// </summary>
/// <param name="db">The database context.</param>
/// <param name="audit">The audit log.</param>
public sealed class CategoryTreeService(AppDbContext db, AuditLog audit)
{
    private const char Separator = '/';

    public const char PathSlugSeparator = '-';

    /// <summary>
    /// Joins the words of a derived slug: "Christmas Light" becomes <c>christmas_light</c>.
    /// Not a hyphen, because that separates levels in the full route.
    /// </summary>
    public const char WordSeparator = '_';


    /// <summary>
    /// Creates a new category with the specified properties. Validates the parent category, slug uniqueness, and redirect rules before creating the category.
    /// </summary>
    /// <param name="name">The name of the category.</param>
    /// <param name="slug">The slug of the category.</param>
    /// <param name="metaTitle">The meta title of the category.</param>
    /// <param name="metaDescription">The meta description of the category.</param>
    /// <param name="parentId">The ID of the parent category.</param>
    /// <param name="sortOrder">The sort order of the category.</param>
    /// <param name="isActive">Indicates whether the category is active.</param>
    /// <param name="isMenu">Indicates whether the category is included in the menu.</param>
    /// <param name="redirectToId">The ID of the category to redirect to.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The result of the operation.</returns>
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

    /// <summary>
    /// Updates an existing category with the specified properties. Validates the parent category, slug uniqueness, and redirect rules before updating the category.
    /// </summary>
    /// <param name="id">The ID of the category to update.</param>
    /// <param name="name">The name of the category.</param>
    /// <param name="slug">The slug of the category.</param>
    /// <param name="metaTitle">The meta title of the category.</param>
    /// <param name="metaDescription">The meta description of the category.</param>
    /// <param name="sortOrder">The sort order of the category.</param>
    /// <param name="isActive">Indicates whether the category is active.</param>
    /// <param name="isMenu">Indicates whether the category is included in the menu.</param>
    /// <param name="redirectToId">The ID of the category to redirect to.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The result of the operation.</returns>
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

        // Creates a summary of the changes made to the category for auditing purposes.
        var summary = AuditLog.Diff(
            ("name", category.Name, name),
            ("slug", category.Slug, finalSlug),
            ("metaTitle", category.MetaTitle, string.IsNullOrWhiteSpace(metaTitle) ? null : metaTitle),
            ("metaDescription", category.MetaDescription, metaDescription),
            ("sortOrder", category.SortOrder, sortOrder),
            ("isActive", category.IsActive, isActive),
            ("isMenu", category.IsMenu, isMenu),
            ("redirectTo", category.RedirectToId, redirectToId));

        await using var transaction = await BeginUnlessNestedAsync(ct);

        category.Name = name;
        category.Slug = finalSlug;
        category.PathSlug = newPathSlug;
        category.MetaTitle = string.IsNullOrWhiteSpace(metaTitle) ? null : metaTitle;
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

        await CommitAsync(transaction, ct);
        return CategoryResult.Ok(category);
    }

    /// <summary>
    /// Reparents a category to a new parent and optionally reorders it among its siblings. 
    /// Validates the new parent category, slug uniqueness, and cycle prevention before moving the category.
    /// </summary>
    /// <param name="id">The ID of the category to move.</param>
    /// <param name="newParentId">The ID of the new parent category.</param>
    /// <param name="position">The position of the category among its siblings.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The result of the operation.</returns>
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

        await using var transaction = await BeginUnlessNestedAsync(ct);

        category.ParentId = newParentId;
        category.Path = newPath;
        category.PathSlug = newPathSlug;
        category.Depth += depthDelta;
        category.UpdatedAt = DateTimeOffset.UtcNow;
        category.UpdatedById = audit.ActorId;

        // Record the move action in the audit log, including the old and new path slugs for reference.
        audit.RecordCategory(category, AuditAction.Moved, $"route: {oldPathSlug} -> {newPathSlug}");
        await db.SaveChangesAsync(ct);

        await RewriteSubtreeAsync(oldPath, newPath, depthDelta, ct);

        if (oldPathSlug != newPathSlug)
        {
            await RewriteSubtreeSlugsAsync(newPath, oldPathSlug, newPathSlug, ct);
        }

        await PlaceAsync(category, position ?? int.MaxValue, ct);
        await db.SaveChangesAsync(ct);

        await CommitAsync(transaction, ct);
        return CategoryResult.Ok(category);
    }

    /// <summary>
    /// Places a category at a specific position among its siblings. Updates the sort order of all siblings accordingly. This method does not change the parent of the category; it only reorders it among its current siblings.
    /// </summary>
    /// <param name="category">The category to place.</param>
    /// <param name="position">The position at which to place the category.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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
    /// Deletes a category and promotes its children to the deleted category's parent. Validates that the new paths and slugs of the promoted children do not conflict with existing categories.
    /// </summary>
    /// <param name="id">The ID of the category to delete.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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

        await using var transaction = await BeginUnlessNestedAsync(ct);

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

        // Record the deletion of the category and the promotion of its children in the audit log.
        audit.RecordCategory(category, AuditAction.Deleted,
            children.Count == 0 ? null : $"{children.Count} child(ren) promoted");
        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);

        await CommitAsync(transaction, ct);
        return (CategoryError.None, children.Select(c => c.Id).ToList());
    }

    /// <summary>
    /// Adds an additional parent-child link between two categories, allowing a category to have multiple parents. 
    /// Validates that the link does not create a cycle and that it does not duplicate an existing link.
    /// </summary>
    /// <param name="childId">The ID of the child category.</param>
    /// <param name="parentId">The ID of the parent category.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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

        // Add the link and record the audit entry before saving changes, so the snapshot still has the names and routes.
        db.CategoryLinks.Add(new CategoryLink { ChildId = childId, ParentId = parentId });
        audit.RecordCategory(child, AuditAction.LinkAdded, $"also placed under '{parent.Name}'");
        await db.SaveChangesAsync(ct);
        return CategoryError.None;
    }

    /// <summary>
    /// Removes an existing link between a child category and a parent category.
    /// </summary>
    /// <param name="childId">The ID of the child category.</param>
    /// <param name="parentId">The ID of the parent category.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task<CategoryError> RemoveLinkAsync(Guid childId, Guid parentId, CancellationToken ct)
    {
        var child = await db.Categories.FirstOrDefaultAsync(c => c.Id == childId, ct);
        var parent = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == parentId, ct);

        await using var transaction = await BeginUnlessNestedAsync(ct);

        var removed = await db.CategoryLinks
            .Where(l => l.ChildId == childId && l.ParentId == parentId)
            .ExecuteDeleteAsync(ct);

        if (removed == 0)
        {
            await RollbackAsync(transaction, ct);
            return CategoryError.NotFound;
        }

        if (child is not null)
        {
            audit.RecordCategory(child, AuditAction.LinkRemoved,
                $"no longer placed under '{parent?.Name ?? parentId.ToString()}'");
            await db.SaveChangesAsync(ct);
        }

        await CommitAsync(transaction, ct);
        return CategoryError.None;
    }

    /// <summary>
    /// Gets all ancestors of a category, ordered from the root down to the immediate parent.
    /// </summary>
    /// <param name="category">The category for which to find ancestors.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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

    /// <summary>
    /// Gets all descendants of a category, optionally including the category itself. 
    /// This is done by checking if the descendant's path starts with the ancestor's path.
    /// </summary>
    /// <param name="category">The category for which to find descendants.</param>
    /// <param name="includeSelf">Indicates whether to include the category itself in the results.</param>
    /// <returns>A queryable collection of descendant categories.</returns>
    public IQueryable<Category> DescendantsOf(Category category, bool includeSelf) =>
        includeSelf
            ? db.Categories.Where(c => c.Path.StartsWith(category.Path))
            : db.Categories.Where(c => c.Path.StartsWith(category.Path) && c.Id != category.Id);

    /// <summary>
    /// Write the new path and depth to everything strictly below a moved node. 
    /// This is necessary when a category is moved to a new parent, as all descendants' paths and depths must be updated to reflect the new location in the tree.
    /// </summary>
    /// <param name="oldPath">The old path of the category.</param>
    /// <param name="newPath">The new path of the category.</param>
    /// <param name="depthDelta">The change in depth.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <summary>
    /// Starts a transaction unless the caller already opened one.
    ///
    /// A bulk import applies many of these operations as a single unit, and EF throws rather
    /// than nesting transactions — so each operation owns one only when nothing else does. Called
    /// on its own, every method behaves exactly as it did before.
    /// </summary>
    private async Task<IDbContextTransaction?> BeginUnlessNestedAsync(CancellationToken ct) =>
        db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;

    /// <summary>Commits only what we started; an outer transaction commits itself.</summary>
    private static async Task CommitAsync(IDbContextTransaction? transaction, CancellationToken ct)
    {
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
        }
    }

    /// <summary>
    /// Rolls back only what we started. Nested, the failure is reported to the caller instead —
    /// one refused row in a batch is not grounds for this method to abandon the caller's work.
    /// </summary>
    private static async Task RollbackAsync(IDbContextTransaction? transaction, CancellationToken ct)
    {
        if (transaction is not null)
        {
            await transaction.RollbackAsync(ct);
        }
    }

    private async Task RewriteSubtreeAsync(string oldPath, string newPath, int depthDelta, CancellationToken ct)
    {
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
    /// Rewrites the path slugs of everything strictly below a moved node. 
    /// This is necessary when a category's slug changes, as all descendants' path slugs must be updated to reflect the new path.
    /// </summary>
    /// <param name="subtreeIdPath">The ID path of the subtree whose slugs to update.</param>
    /// <param name="oldPathSlug">The old path slug.</param>
    /// <param name="newPathSlug">The new path slug.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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

    /// <summary>
    /// Checks if the candidate category is a descendant of the ancestor category by comparing their paths.
    /// </summary>
    /// <param name="candidate">The category to check.</param>
    /// <param name="ancestor">The potential ancestor category.</param>
    /// <returns>true if the candidate is a descendant of the ancestor; otherwise, false.</returns>
    private static bool IsDescendantOf(Category candidate, Category ancestor) =>
        candidate.Path.StartsWith(ancestor.Path, StringComparison.Ordinal);

    /// <summary>
    /// Builds an ID path for a category based on its parent and its own ID. 
    /// The path is in the format "/{parentId}/{id}/" for non-root categories, or "/{id}/" for root categories.
    /// </summary>
    /// <param name="parent">The parent category.</param>
    /// <param name="id">The ID of the category.</param>
    /// <returns>The ID path.</returns>
    private static string BuildPath(Category? parent, Guid id) =>
        parent is null ? $"{Separator}{id}{Separator}" : $"{parent.Path}{id}{Separator}";

    /// <summary>
    /// Parses an ID path into a sequence of GUIDs. 
    /// The path is expected to be in the format "/{id1}/{id2}/{id3}/", where each segment is a GUID.
    /// </summary>
    /// <param name="path">The path to parse.</param>
    /// <returns>The sequence of GUIDs.</returns>
    private static IEnumerable<Guid> ParsePath(string path) =>
        path.Split(Separator, StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => Guid.TryParse(segment, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty);

    /// <summary>
    /// Validates the redirect rules for a category. 
    /// Ensures that the redirect target exists, is not the same as the category itself, and does not create a redirect chain.
    /// </summary>
    /// <param name="categoryId">The ID of the category for which to validate the redirect.</param>
    /// <param name="redirectToId">The ID of the category to which this category redirects.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The result of the validation.</returns>
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

    /// <summary>
    /// Resolves the final slug for a category. 
    /// If a slug is provided, it normalizes it; otherwise, it generates a slug from the category name.
    /// </summary>
    /// <param name="slug">The slug to resolve.</param>
    /// <param name="name">The name of the category.</param>
    /// <returns>The resolved slug.</returns>
    private static string ResolveSlug(string? slug, string name) =>
        string.IsNullOrWhiteSpace(slug) ? Slugify(name) : NormalizeSlug(slug);

    /// <summary>
    /// Tidies a slug someone typed by hand.
    /// <para>
    /// Letters, digits, hyphens and underscores survive; anything else becomes a hyphen. The
    /// underscore has to survive: a derived slug is full of them, and the panel submits the
    /// stored slug on every save — rewriting them to hyphens here would silently change a
    /// category's route each time somebody edited its name.
    /// </para>
    /// </summary>
    public static string NormalizeSlug(string value)
    {
        var mapped = new string([.. ToAscii(value)
            .Select(c => IsPlain(c) || c == WordSeparator ? c : PathSlugSeparator)]);

        return Tidy(mapped);
    }

    /// <summary>
    /// Derives a slug from a name.
    /// <para>
    /// Spaces become underscores and everything else that is not a letter or digit is dropped,
    /// so "Christmas Light" gives <c>christmas_light</c> and "Christmas Light's" gives
    /// <c>christmas_lights</c> — the apostrophe disappears rather than splitting the word.
    /// </para>
    /// The hyphen is left well alone: it delimits levels in the full route, so a derived
    /// segment must never contain one.
    /// </summary>
    public static string Slugify(string value)
    {
        // Filter first, then map: everything that is not a letter, digit or space simply is
        // not there, which is what closes "Light's" up into "lights".
        var mapped = new string([.. ToAscii(value)
            .Where(c => IsPlain(c) || char.IsWhiteSpace(c))
            .Select(c => char.IsWhiteSpace(c) ? WordSeparator : c)]);

        return Tidy(mapped);
    }

    /// <summary>A character a URL can carry without being escaped.</summary>
    private static bool IsPlain(char c) => c is (>= 'a' and <= 'z') or (>= '0' and <= '9');

    /// <summary>
    /// Lowercases and folds a name down to plain ASCII.
    /// <para>
    /// Accents are dropped rather than kept, because a slug ends up in a URL: left alone,
    /// "Café Déjà Vu" travels as <c>caf%C3%A9_d%C3%A9j%C3%A0_vu</c>, which nobody can read,
    /// type or share. The letters that have no accent to strip — ß, ø, æ — are spelled out
    /// first, since decomposition leaves them untouched and they would otherwise vanish.
    /// </para>
    /// </summary>
    private static string ToAscii(string value)
    {
        var spelled = value
            .ToLowerInvariant()
            .Replace("ß", "ss")
            .Replace("æ", "ae")
            .Replace("œ", "oe")
            .Replace("ø", "o")
            .Replace("đ", "d")
            .Replace("ð", "d")
            .Replace("þ", "th")
            .Replace("ł", "l");

        // FormD splits a letter from its accent; the accent is a non-spacing mark, so dropping
        // those marks leaves the bare letter behind.
        var decomposed = spelled.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Collapses runs of separators, trims them from both ends, caps the length, and falls back
    /// to something unique when nothing usable is left — a name of only punctuation still has
    /// to produce an addressable category.
    /// </summary>
    private static string Tidy(string mapped)
    {
        var builder = new StringBuilder(mapped.Length);

        foreach (var c in mapped)
        {
            var isSeparator = c == WordSeparator || c == PathSlugSeparator;

            // Skips a separator at the very start, and any repeat of the one before it.
            if (isSeparator && (builder.Length == 0 || builder[^1] == c))
            {
                continue;
            }

            builder.Append(c);
        }

        while (builder.Length > 0
            && (builder[^1] == WordSeparator || builder[^1] == PathSlugSeparator))
        {
            builder.Length--;
        }

        var slug = builder.ToString();

        return slug.Length == 0
            ? Guid.CreateVersion7().ToString("N")[..12]
            : slug[..Math.Min(slug.Length, 200)];
    }

    /// <summary>
    /// Builds an path of slugs for a category based on its parent and its own slug.
    /// </summary>
    /// <param name="parent">The parent category.</param>
    /// <param name="slug">The slug of the category.</param>
    /// <returns>The built path slug.</returns>
    private static string BuildPathSlug(Category? parent, string slug) =>
        parent is null ? slug : $"{parent.PathSlug}{PathSlugSeparator}{slug}";
}
