using System.Security.Claims;
using DeBoetAPI.Common;
using DeBoetAPI.Data;
using DeBoetAPI.Data.Entities;
using DeBoetAPI.Features.Categories;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace DeBoetAPI.Features.Items;

/// <summary>
/// Owned-CRUD template. Every query is filtered by the caller's id, so a user can never see
/// </summary>
public static class ItemEndpoints
{
    public static IEndpointRouteBuilder MapItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/items")
            .WithTags("Items")
            .RequireAuthorization();

        group.MapGet("/", ListAsync).WithName("ListItems");
        group.MapGet("/{id:guid}", GetAsync).WithName("GetItem");
        group.MapPost("/", CreateAsync).WithName("CreateItem");
        group.MapPut("/{id:guid}", UpdateAsync).WithName("UpdateItem");
        group.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteItem");
        group.MapPut("/{id:guid}/categories", SetCategoriesAsync).WithName("SetItemCategories");
        group.MapGet("/{id:guid}/categories", GetCategoriesAsync).WithName("GetItemCategories");

        return app;
    }

    /// <summary>
    /// Replaces the item's category set wholesale. Categories are a global taxonomy, but the
    /// assignment belongs to the item, so this is owner-scoped rather than admin-only.
    /// </summary>
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> SetCategoriesAsync(
        Guid id,
        SetItemCategoriesRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        AuditLog audit,
        CancellationToken ct)
    {
        var ownerId = principal.GetUserId();
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id && i.OwnerId == ownerId, ct);

        if (item is null)
        {
            return TypedResults.NotFound();
        }

        var requestedIds = request.CategoryIds.Distinct().ToList();

        var existing = await db.Categories.Where(c => requestedIds.Contains(c.Id)).Select(c => c.Id).ToListAsync(ct);
        if (existing.Count != requestedIds.Count)
        {
            var missing = requestedIds.Except(existing);
            return TypedResults.Problem(
                $"Unknown categories: {string.Join(", ", missing)}",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.PrimaryCategoryId is { } primary && !requestedIds.Contains(primary))
        {
            return TypedResults.Problem(
                "The primary category must be one of the assigned categories.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await db.ItemCategories.Where(ic => ic.ItemId == id).ExecuteDeleteAsync(ct);

        db.ItemCategories.AddRange(requestedIds.Select(categoryId => new ItemCategory
        {
            ItemId = id,
            CategoryId = categoryId,
            IsPrimary = request.PrimaryCategoryId == categoryId,
        }));

        item.UpdatedAt = DateTimeOffset.UtcNow;
        audit.RecordItem(item, AuditAction.CategoriesAssigned,
            requestedIds.Count == 0 ? "all categories cleared" : $"{requestedIds.Count} category/categories assigned");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<IReadOnlyList<ItemCategoryResponse>>, NotFound>> GetCategoriesAsync(
        Guid id,
        ClaimsPrincipal principal,
        AppDbContext db,
        CancellationToken ct)
    {
        var ownerId = principal.GetUserId();

        if (!await db.Items.AnyAsync(i => i.Id == id && i.OwnerId == ownerId, ct))
        {
            return TypedResults.NotFound();
        }

        var categories = await db.ItemCategories.AsNoTracking()
            .Where(ic => ic.ItemId == id)
            .OrderByDescending(ic => ic.IsPrimary)
            .Select(ic => new ItemCategoryResponse(
                ic.CategoryId, ic.Category.Name, ic.Category.Slug, ic.IsPrimary))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<ItemCategoryResponse>>(categories);
    }

    private static async Task<Ok<PagedResult<ItemResponse>>> ListAsync(
        ClaimsPrincipal principal,
        AppDbContext db,
        CancellationToken ct,
        int? page = null,
        int? pageSize = null)
    {
        var ownerId = principal.GetUserId();
        var (p, size) = Paging.Normalize(page, pageSize);

        var query = db.Items.AsNoTracking().Where(i => i.OwnerId == ownerId);
        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip((p - 1) * size)
            .Take(size)
            .Select(i => new ItemResponse(i.Id, i.Name, i.Description, i.CreatedAt, i.UpdatedAt))
            .ToListAsync(ct);

        return TypedResults.Ok(new PagedResult<ItemResponse>(items, p, size, total));
    }

    private static async Task<Results<Ok<ItemResponse>, NotFound>> GetAsync(
        Guid id,
        ClaimsPrincipal principal,
        AppDbContext db,
        CancellationToken ct)
    {
        var ownerId = principal.GetUserId();

        var item = await db.Items.AsNoTracking()
            .Where(i => i.Id == id && i.OwnerId == ownerId)
            .Select(i => new ItemResponse(i.Id, i.Name, i.Description, i.CreatedAt, i.UpdatedAt))
            .FirstOrDefaultAsync(ct);

        return item is null ? TypedResults.NotFound() : TypedResults.Ok(item);
    }

    private static async Task<Created<ItemResponse>> CreateAsync(
        CreateItemRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        AuditLog audit,
        CancellationToken ct)
    {
        var item = new Item
        {
            Name = request.Name,
            Description = request.Description,
            OwnerId = principal.GetUserId(),
        };

        db.Items.Add(item);
        audit.RecordItem(item, AuditAction.Created);
        await db.SaveChangesAsync(ct);

        var response = new ItemResponse(item.Id, item.Name, item.Description, item.CreatedAt, item.UpdatedAt);
        return TypedResults.Created($"/api/v1/items/{item.Id}", response);
    }

    private static async Task<Results<Ok<ItemResponse>, NotFound>> UpdateAsync(
        Guid id,
        UpdateItemRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        AuditLog audit,
        CancellationToken ct)
    {
        var ownerId = principal.GetUserId();
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id && i.OwnerId == ownerId, ct);

        if (item is null)
        {
            return TypedResults.NotFound();
        }

        var summary = AuditLog.Diff(
            ("name", item.Name, request.Name),
            ("description", item.Description, request.Description));

        item.Name = request.Name;
        item.Description = request.Description;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        if (summary is not null)
        {
            audit.RecordItem(item, AuditAction.Updated, summary);
        }

        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new ItemResponse(item.Id, item.Name, item.Description, item.CreatedAt, item.UpdatedAt));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(
        Guid id,
        ClaimsPrincipal principal,
        AppDbContext db,
        AuditLog audit,
        CancellationToken ct)
    {
        var ownerId = principal.GetUserId();

        // Loaded rather than bulk-deleted: the audit entry needs the name, and a deletion is
        // the event most worth being able to look up afterwards.
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id && i.OwnerId == ownerId, ct);
        if (item is null)
        {
            return TypedResults.NotFound();
        }

        audit.RecordItem(item, AuditAction.Deleted);
        db.Items.Remove(item);
        await db.SaveChangesAsync(ct);

        return TypedResults.NoContent();
    }
}
