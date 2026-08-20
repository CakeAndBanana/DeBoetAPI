using System.Globalization;
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

    /// <summary>
    /// Every filter arrives as a string and is parsed here rather than bound directly.
    /// Minimal APIs answer a query string they cannot bind with a 500, and these values come
    /// straight off a URL somebody can edit — a typo deserves "that is not a record type",
    /// not an internal error.
    /// </summary>
    private static async Task<Results<Ok<PagedResult<AuditEntryResponse>>, ProblemHttpResult>> ListAsync(
        AppDbContext db,
        CancellationToken ct,
        string? entityType = null,
        string? action = null,
        string? entityId = null,
        string? changedById = null,
        string? since = null,
        int? page = null,
        int? pageSize = null)
    {
        if (!TryParse<AuditedEntityType>(entityType, out var type))
        {
            return BadFilter("entityType", entityType);
        }

        if (!TryParse<AuditAction>(action, out var auditAction))
        {
            return BadFilter("action", action);
        }

        if (!TryParseGuid(entityId, out var entity))
        {
            return BadFilter("entityId", entityId);
        }

        if (!TryParseGuid(changedById, out var actor))
        {
            return BadFilter("changedById", changedById);
        }

        if (!TryParseInstant(since, out var from))
        {
            return BadFilter("since", since);
        }

        var query = db.AuditEntries.AsNoTracking();

        if (type is not null)
        {
            query = query.Where(a => a.EntityType == type);
        }

        if (auditAction is not null)
        {
            query = query.Where(a => a.Action == auditAction);
        }

        if (entity is not null)
        {
            query = query.Where(a => a.EntityId == entity);
        }

        if (actor is not null)
        {
            query = query.Where(a => a.ChangedById == actor);
        }

        if (from is not null)
        {
            query = query.Where(a => a.ChangedAt >= from);
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

    /// <summary>Absent is fine; present but unparseable is not.</summary>
    private static bool TryParse<T>(string? value, out T? parsed) where T : struct, Enum
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value)) return true;

        if (!Enum.TryParse<T>(value, ignoreCase: true, out var result)) return false;

        parsed = result;
        return true;
    }

    private static bool TryParseGuid(string? value, out Guid? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value)) return true;

        if (!Guid.TryParse(value, out var result)) return false;

        parsed = result;
        return true;
    }

    private static bool TryParseInstant(string? value, out DateTimeOffset? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value)) return true;

        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal, out var result)) return false;

        parsed = result;
        return true;
    }

    private static ProblemHttpResult BadFilter(string name, string? value) =>
        TypedResults.Problem(
            $"'{value}' is not a valid value for {name}.",
            statusCode: StatusCodes.Status400BadRequest);

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
