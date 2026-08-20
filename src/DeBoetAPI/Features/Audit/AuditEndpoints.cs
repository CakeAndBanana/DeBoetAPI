using System.Security.Claims;
using DeBoetAPI.Common;
using DeBoetAPI.Data;
using DeBoetAPI.Data.Entities;
using DeBoetAPI.Features.Auth;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace DeBoetAPI.Features.Audit;

public sealed record AuditEntryResponse(
    Guid Id,
    string EntityType,
    Guid EntityId,
    string EntityName,
    string? EntityPath,
    string Action,
    Guid? ChangedById,
    string ChangedByName,
    DateTimeOffset ChangedAt,
    string? Summary);

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/audit", ListAsync)
            .WithTags("Audit")
            .WithName("ListAuditEntries")
            .WithSummary("Every recorded change, newest first.")
            .RequireAuthorization(AuthPolicies.Admin);

        // One category's history follows the category write surface: whoever may reshape it
        // may see who reshaped it. The unfiltered feed above stays with admins, since it spans
        // user management too.
        app.MapGet("/api/v1/categories/{id:guid}/history", CategoryHistoryAsync)
            .WithTags("Categories")
            .WithName("GetCategoryHistory")
            .RequireAuthorization(AuthPolicies.Editor);

        app.MapGet("/api/v1/items/{id:guid}/history", ItemHistoryAsync)
            .WithTags("Items")
            .WithName("GetItemHistory")
            .RequireAuthorization();

        return app;
    }

    private static async Task<Ok<PagedResult<AuditEntryResponse>>> ListAsync(
        AppDbContext db,
        CancellationToken ct,
        AuditedEntityType? entityType = null,
        Guid? entityId = null,
        Guid? changedById = null,
        DateTimeOffset? since = null,
        int? page = null,
        int? pageSize = null)
    {
        var query = db.AuditEntries.AsNoTracking();

        if (entityType is not null)
        {
            query = query.Where(a => a.EntityType == entityType);
        }

        if (entityId is not null)
        {
            query = query.Where(a => a.EntityId == entityId);
        }

        if (changedById is not null)
        {
            query = query.Where(a => a.ChangedById == changedById);
        }

        if (since is not null)
        {
            query = query.Where(a => a.ChangedAt >= since);
        }

        return TypedResults.Ok(await PageAsync(query, page, pageSize, ct));
    }

    private static async Task<Ok<PagedResult<AuditEntryResponse>>> CategoryHistoryAsync(
        Guid id,
        AppDbContext db,
        CancellationToken ct,
        int? page = null,
        int? pageSize = null)
    {
        var query = db.AuditEntries.AsNoTracking()
            .Where(a => a.EntityType == AuditedEntityType.Category && a.EntityId == id);

        return TypedResults.Ok(await PageAsync(query, page, pageSize, ct));
    }

    private static async Task<Results<Ok<PagedResult<AuditEntryResponse>>, NotFound>> ItemHistoryAsync(
        Guid id,
        ClaimsPrincipal principal,
        AppDbContext db,
        CancellationToken ct,
        int? page = null,
        int? pageSize = null)
    {
        var ownerId = principal.GetUserId();
        if (!await db.Items.AnyAsync(i => i.Id == id && i.OwnerId == ownerId, ct))
        {
            return TypedResults.NotFound();
        }

        var query = db.AuditEntries.AsNoTracking()
            .Where(a => a.EntityType == AuditedEntityType.Item && a.EntityId == id);

        return TypedResults.Ok(await PageAsync(query, page, pageSize, ct));
    }

    private static async Task<PagedResult<AuditEntryResponse>> PageAsync(
        IQueryable<AuditEntry> query,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var (p, size) = Paging.Normalize(page, pageSize);
        var total = await query.CountAsync(ct);

        var entries = await query
            .OrderByDescending(a => a.ChangedAt)
            .ThenByDescending(a => a.Id)
            .Skip((p - 1) * size)
            .Take(size)
            .Select(a => new AuditEntryResponse(
                a.Id,
                a.EntityType.ToString(),
                a.EntityId,
                a.EntityName,
                a.EntityPath,
                a.Action.ToString(),
                a.ChangedById,
                a.ChangedByName,
                a.ChangedAt,
                a.Summary))
            .ToListAsync(ct);

        return new PagedResult<AuditEntryResponse>(entries, p, size, total);
    }
}
