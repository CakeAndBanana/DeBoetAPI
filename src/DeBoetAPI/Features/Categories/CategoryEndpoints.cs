using DeBoetAPI.Common;
using DeBoetAPI.Data;
using DeBoetAPI.Data.Entities;
using DeBoetAPI.Features.Auth;
using DeBoetAPI.Features.Items;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace DeBoetAPI.Features.Categories;

/// <summary>
/// Reads are anonymous; every mutation requires the Editor role, which Admins also satisfy.
/// </summary>
public static class CategoryEndpoints
{
    /// <summary>
    /// Maps the category endpoints to the specified EndpointRouteBuilder.
    /// Read endpoints are anonymous. Every write — including move, delete and secondary links —
    /// requires the Editor policy, which an Admin also satisfies.
    /// </summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The updated endpoint route builder.</returns>
    public static IEndpointRouteBuilder MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var reads = app.MapGroup("/api/v1/categories")
            .WithTags("Categories")
            .AllowAnonymous();

        reads.MapGet("/", ListAsync).WithName("ListCategories");
        reads.MapGet("/tree", GetTreeAsync).WithName("GetCategoryTree");
        reads.MapGet("/{idOrSlug}", GetAsync).WithName("GetCategory");
        reads.MapGet("/{id:guid}/items", GetItemsAsync).WithName("GetCategoryItems");

        var edits = app.MapGroup("/api/v1/categories")
            .WithTags("Categories")
            .RequireAuthorization(AuthPolicies.Editor);

        edits.MapPost("/", CreateAsync).WithName("CreateCategory");
        edits.MapPut("/{id:guid}", UpdateAsync).WithName("UpdateCategory");
        edits.MapPost("/{id:guid}/move", MoveAsync).WithName("MoveCategory");
        edits.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteCategory");
        edits.MapPost("/{id:guid}/links", AddLinkAsync).WithName("AddCategoryLink");
        edits.MapDelete("/{id:guid}/links/{parentId:guid}", RemoveLinkAsync).WithName("RemoveCategoryLink");

        var exports = app.MapGroup("/api/v1/categories")
            .WithTags("Categories")
            .RequireAuthorization(AuthPolicies.Editor);

        exports.MapGet("/export", ExportAsync)
            .WithName("ExportCategories")
            .RequireRateLimiting(RateLimitPolicies.Export)
            .ExcludeFromDescription();

        return app;
    }

    /// <summary>
    /// Renders the whole tree as a workbook: one sheet per root category, rows folded into
    /// Excel's outline.
    /// </summary>
    /// <param name="export">The workbook builder.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>An .xlsx file, as a download.</returns>
    private static async Task<IResult> ExportAsync(CategoryExportService export, CancellationToken ct)
    {
        var workbook = await export.BuildAsync(ct);

        return Results.File(
            workbook,
            CategoryExportService.ContentType,
            CategoryExportService.FileName(DateTimeOffset.UtcNow));
    }


    /// <summary>
    /// Gets a paginated list of categories, optionally filtered by parent, search term, and active status. Returns 200 OK with the paginated result.
    /// </summary>
    /// <param name="db">The database context.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <param name="parentId">The ID of the parent category.</param>
    /// <param name="rootsOnly">Whether to include only root categories.</param>
    /// <param name="search">The search term.</param>
    /// <param name="includeInactive">Whether to include inactive categories.</param>
    /// <param name="page">The page number.</param>
    /// <param name="pageSize">The number of items per page.</param>
    /// <returns>The result of the operation.</returns>
    private static async Task<Ok<PagedResult<CategoryResponse>>> ListAsync(
        AppDbContext db,
        CancellationToken ct,
        Guid? parentId = null,
        bool rootsOnly = false,
        string? search = null,
        bool includeInactive = false,
        bool menuOnly = false,
        int? page = null,
        int? pageSize = null)
    {
        var (p, size) = Paging.Normalize(page, pageSize);

        var query = db.Categories.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        if (menuOnly)
        {
            query = query.Where(c => c.IsMenu);
        }

        if (rootsOnly)
        {
            query = query.Where(c => c.ParentId == null);
        }
        else if (parentId is not null)
        {
            query = query.Where(c => c.ParentId == parentId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(c => EF.Functions.ILike(c.Name, $"%{search}%"));
        }

        var total = await query.CountAsync(ct);

        var results = await query
            .OrderBy(c => c.Depth).ThenBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Skip((p - 1) * size)
            .Take(size)
            .Select(c => ToResponse(c))
            .ToListAsync(ct);

        return TypedResults.Ok(new PagedResult<CategoryResponse>(results, p, size, total));
    }

    /// <summary>
    /// Gets the entire category tree, optionally starting from a specific root and limiting depth. Returns 404 if the specified root does not exist.
    /// </summary>
    /// <param name="db">The database context.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <param name="rootId">The ID of the root category.</param>
    /// <param name="maxDepth">The maximum depth to include.</param>
    /// <param name="includeInactive">Whether to include inactive categories.</param>
    /// <returns>The result of the operation.</returns>
    private static async Task<Results<Ok<IReadOnlyList<CategoryNode>>, NotFound>> GetTreeAsync(
        AppDbContext db,
        CancellationToken ct,
        Guid? rootId = null,
        int? maxDepth = null,
        bool includeInactive = false,
        bool menuOnly = false)
    {
        var query = db.Categories.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        // Building a nav menu is the whole reason the flag exists, so the tree endpoint —
        // which is what a menu is rendered from — has to be able to filter on it.
        if (menuOnly)
        {
            query = query.Where(c => c.IsMenu);
        }

        if (rootId is not null)
        {
            var root = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == rootId, ct);
            if (root is null)
            {
                return TypedResults.NotFound();
            }

            query = query.Where(c => c.Path.StartsWith(root.Path));

            if (maxDepth is not null)
            {
                query = query.Where(c => c.Depth <= root.Depth + maxDepth);
            }
        }
        else if (maxDepth is not null)
        {
            query = query.Where(c => c.Depth <= maxDepth);
        }

        var flat = await query
            .OrderBy(c => c.Depth).ThenBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(ct);

        return TypedResults.Ok(BuildTree(flat, rootId));
    }

    /// <summary>
    /// Gets a single category by ID or slug, along with its breadcrumb, children, and secondary parents.
    /// </summary>
    /// <param name="idOrSlug">The ID or slug of the category.</param>
    /// <param name="db">The database context.</param>
    /// <param name="tree">The category tree service.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    private static async Task<Results<Ok<CategoryDetailResponse>, NotFound>> GetAsync(
        string idOrSlug,
        AppDbContext db,
        CategoryTreeService tree,
        CancellationToken ct)
    {
        var category = await FindAsync(db, idOrSlug, ct);
        if (category is null)
        {
            return TypedResults.NotFound();
        }

        var breadcrumb = (await tree.GetAncestorsAsync(category, ct)).Select(ToResponse).ToList();

        var children = await db.Categories.AsNoTracking()
            .Where(c => c.ParentId == category.Id)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => ToResponse(c))
            .ToListAsync(ct);

        var secondaryParents = await db.CategoryLinks.AsNoTracking()
            .Where(l => l.ChildId == category.Id)
            .Select(l => ToResponse(l.Parent))
            .ToListAsync(ct);

        // Resolved here so a site rendering this route can redirect straight away rather than
        // fetching the destination separately just to learn its address.
        var redirectTo = category.RedirectToId is null
            ? null
            : await db.Categories.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == category.RedirectToId, ct);

        return TypedResults.Ok(new CategoryDetailResponse(
            ToResponse(category), breadcrumb, children, secondaryParents,
            redirectTo is null ? null : ToResponse(redirectTo)));
    }

    /// <summary>
    /// Gets the items associated with a category, optionally including items from descendant categories.
    /// </summary>
    /// <param name="id">The ID of the category.</param>
    /// <param name="db">The database context.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <param name="includeDescendants">Whether to include items from descendant categories.</param>
    /// <param name="page">The page number.</param>
    /// <param name="pageSize">The page size.</param>
    /// <returns></returns>
    private static async Task<Results<Ok<PagedResult<ItemResponse>>, NotFound>> GetItemsAsync(
        Guid id,
        AppDbContext db,
        CancellationToken ct,
        bool includeDescendants = false,
        int? page = null,
        int? pageSize = null)
    {
        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null)
        {
            return TypedResults.NotFound();
        }

        var (p, size) = Paging.Normalize(page, pageSize);

        // Descendant lookup is a single indexed prefix scan on the materialised path.
        var categoryIds = includeDescendants
            ? db.Categories.Where(c => c.Path.StartsWith(category.Path)).Select(c => c.Id)
            : db.Categories.Where(c => c.Id == id).Select(c => c.Id);

        var query = db.ItemCategories.AsNoTracking()
            .Where(ic => categoryIds.Contains(ic.CategoryId))
            .Select(ic => ic.Item)
            .Distinct();

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip((p - 1) * size)
            .Take(size)
            .Select(i => new ItemResponse(i.Id, i.Name, i.Description, i.CreatedAt, i.UpdatedAt))
            .ToListAsync(ct);

        return TypedResults.Ok(new PagedResult<ItemResponse>(items, p, size, total));
    }

    /// <summary>
    /// Creates a new category. If a parentId is provided, the new category will be linked to that parent.
    /// Requires the Admin Role. Returns 201 Created with the new category, or 409 Conflict if a category with the same slug already exists.
    /// </summary>
    /// <param name="request">The request containing the category details.</param>
    /// <param name="tree">The category tree service.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    private static async Task<Results<Created<CategoryResponse>, ProblemHttpResult>> CreateAsync(
        CreateCategoryRequest request,
        CategoryTreeService tree,
        CancellationToken ct)
    {
        var result = await tree.CreateAsync(
            request.Name, request.Slug, request.MetaTitle, request.MetaDescription,
            request.ParentId, request.SortOrder, request.IsActive, request.IsMenu,
            request.RedirectToId, ct);

        if (!result.Succeeded)
        {
            return Problem(result.Error);
        }

        var response = ToResponse(result.Category!);
        return TypedResults.Created($"/api/v1/categories/{response.Id}", response);
    }

    /// <summary>
    /// Updates an existing category.
    /// Requires the Admin Role. Returns 200 OK with the updated category, or 404 Not Found if the category does not exist, or 409 Conflict if a category with the same slug already exists.
    /// </summary>
    /// <param name="id">The ID of the category to update.</param>
    /// <param name="request">The request containing the updated category details.</param>
    /// <param name="tree">The category tree service.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    private static async Task<Results<Ok<CategoryResponse>, ProblemHttpResult>> UpdateAsync(
        Guid id,
        UpdateCategoryRequest request,
        CategoryTreeService tree,
        CancellationToken ct)
    {
        var result = await tree.UpdateAsync(
            id, request.Name, request.Slug, request.MetaTitle, request.MetaDescription,
            request.SortOrder, request.IsActive, request.IsMenu, request.RedirectToId, ct);

        return result.Succeeded
            ? TypedResults.Ok(ToResponse(result.Category!))
            : Problem(result.Error);
    }

    /// <summary>
    /// Moves a category to a new parent. 
    /// Requires the Admin Role. Returns 200 OK with the updated category, or 404 Not Found if the category or new parent does not exist, or 409 Conflict if the move would create a cycle in the category tree.
    /// </summary>
    /// <param name="id">The ID of the category to move.</param>
    /// <param name="request">The request containing the new parent ID.</param>
    /// <param name="tree">The category tree service.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    private static async Task<Results<Ok<CategoryResponse>, ProblemHttpResult>> MoveAsync(
        Guid id,
        MoveCategoryRequest request,
        CategoryTreeService tree,
        CancellationToken ct)
    {
        var result = await tree.MoveAsync(id, request.NewParentId, request.Position, ct);

        return result.Succeeded
            ? TypedResults.Ok(ToResponse(result.Category!))
            : Problem(result.Error);
    }

    /// <summary>
    /// Deltes a category. If the category has children, they will be promoted to the deleted category's parent.
    /// Requires the Admin Role. Returns 200 OK with the list of promoted child IDs, or 404 Not Found if the category does not exist.
    /// </summary>
    /// <param name="id">The ID of the category to delete.</param>
    /// <param name="tree">The category tree service.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    private static async Task<Results<Ok<DeleteCategoryResponse>, ProblemHttpResult>> DeleteAsync(
        Guid id,
        CategoryTreeService tree,
        CancellationToken ct)
    {
        var (error, promoted) = await tree.DeleteAsync(id, ct);

        return error == CategoryError.None
            ? TypedResults.Ok(new DeleteCategoryResponse(promoted))
            : Problem(error);
    }

    /// <summary>
    /// Adds a link between an existing category and a new parent category. This allows for multiple parent categories for a single child category.
    /// Requires the Admin Role. Returns 204 No Content on success, or 404 Not Found if the category or parent does not exist, or 409 Conflict if the link already exists or would create a cycle in the category tree.
    /// </summary>
    /// <param name="id">The ID of the category to link.</param>
    /// <param name="request">The request containing the parent ID.</param>
    /// <param name="tree">The category tree service.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    private static async Task<Results<NoContent, ProblemHttpResult>> AddLinkAsync(
        Guid id,
        AddCategoryLinkRequest request,
        CategoryTreeService tree,
        CancellationToken ct)
    {
        var error = await tree.AddLinkAsync(id, request.ParentId, ct);

        return error == CategoryError.None ? TypedResults.NoContent() : Problem(error);
    }

    /// <summary>
    /// Removes link between an existing category and a parent category. This does not delete the category, but simply removes the link to the specified parent.
    /// Requires the Admin Role. Returns 204 No Content on success, or 404 Not Found if the category or parent does not exist, or 409 Conflict if the link does not exist.
    /// </summary>
    /// <param name="id">The ID of the category to unlink.</param>
    /// <param name="parentId">The ID of the parent category to unlink.</param>
    /// <param name="tree">The category tree service.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveLinkAsync(
        Guid id,
        Guid parentId,
        CategoryTreeService tree,
        CancellationToken ct)
    {
        var error = await tree.RemoveLinkAsync(id, parentId, ct);

        return error == CategoryError.None ? TypedResults.NoContent() : Problem(error);
    }

    /// <summary>
    /// Finds a category by ID or slug. Returns null if not found. This is used for endpoints that accept either an ID or a slug to identify a category.
    /// </summary>
    /// <param name="db">The database context.</param>
    /// <param name="idOrSlug">The ID or slug of the category to find.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The found category or null if not found.</returns>
    private static Task<Category?> FindAsync(AppDbContext db, string idOrSlug, CancellationToken ct) =>
        Guid.TryParse(idOrSlug, out var id)
            ? db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)
            : db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.PathSlug == idOrSlug, ct);

    private static CategoryResponse ToResponse(Category c) => new(
        c.Id, c.Name, c.Slug, c.PathSlug, c.MetaTitle, c.MetaDescription, c.RedirectToId, c.ParentId,
        c.Depth, c.SortOrder, c.IsActive, c.IsMenu, c.CreatedById, c.UpdatedById,
        c.CreatedAt, c.UpdatedAt);

    /// <summary>
    /// Assembles the nested shape from a flat, depth-ordered list.
    /// </summary>
    /// <param name="flat">The flat, depth-ordered list of categories.</param>
    /// <param name="rootId">The ID of the root category, if applicable.</param>
    /// <returns>The nested category tree.</returns>
    private static IReadOnlyList<CategoryNode> BuildTree(List<Category> flat, Guid? rootId)
    {
        var childrenByParent = flat
            .Where(c => c.ParentId is not null)
            .GroupBy(c => c.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var present = flat.Select(c => c.Id).ToHashSet();

        var roots = rootId is not null
            ? flat.Where(c => c.Id == rootId).ToList()
            : [.. flat.Where(c => c.ParentId is null || !present.Contains(c.ParentId.Value))];

        return [.. roots.Select(Build)];

        CategoryNode Build(Category c)
        {
            return new(
            c.Id, c.Name, c.Slug, c.PathSlug, c.MetaTitle, c.MetaDescription, c.RedirectToId,
            c.Depth, c.SortOrder, c.IsActive, c.IsMenu,
            childrenByParent.TryGetValue(c.Id, out var kids)
                ? kids.Select(Build).ToList()
                : []);
        }
    }

    /// <summary>
    /// Gives the appropriate ProblemHttpResult for a given CategoryError. 
    /// </summary>
    /// <param name="error">The category error.</param>
    /// <returns>The problem HTTP result.</returns>
    private static ProblemHttpResult Problem(CategoryError error) => error switch
    {
        CategoryError.NotFound => TypedResults.Problem("Category not found.", statusCode: StatusCodes.Status404NotFound),
        CategoryError.ParentNotFound => TypedResults.Problem("Parent category not found.", statusCode: StatusCodes.Status404NotFound),
        CategoryError.DuplicateSlug => TypedResults.Problem("A category with that slug already exists.", statusCode: StatusCodes.Status409Conflict),
        CategoryError.Cycle => TypedResults.Problem("That would place the category inside its own subtree.", statusCode: StatusCodes.Status409Conflict),
        CategoryError.SelfReference => TypedResults.Problem("A category cannot be linked to itself.", statusCode: StatusCodes.Status409Conflict),
        CategoryError.DuplicateLink => TypedResults.Problem("That link already exists.", statusCode: StatusCodes.Status409Conflict),
        CategoryError.LinkDuplicatesMainParent => TypedResults.Problem("That category is already the main parent.", statusCode: StatusCodes.Status409Conflict),
        CategoryError.RedirectNotFound => TypedResults.Problem("The category to redirect to was not found.", statusCode: StatusCodes.Status404NotFound),
        CategoryError.RedirectChain => TypedResults.Problem("Redirects are one hop only: a category cannot forward to one that already forwards, and a destination cannot forward on.", statusCode: StatusCodes.Status409Conflict),
        _ => TypedResults.Problem("Unexpected category error.", statusCode: StatusCodes.Status500InternalServerError),
    };
}
