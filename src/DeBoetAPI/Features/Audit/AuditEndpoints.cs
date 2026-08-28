using System.Globalization;
using System.Security.Claims;
using DeBoetAPI.Common;
using DeBoetAPI.Data;
using DeBoetAPI.Data.Entities;
using DeBoetAPI.Features.Auth;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace DeBoetAPI.Features.Audit;

/// <summary>
/// Audit entry response model. Represents a single audit entry with details about the change made to an entity.
/// </summary>
/// <param name="Id">The ID of the audit entry.</param>
/// <param name="EntityType">The type of the entity.</param>
/// <param name="EntityId">The ID of the entity.</param>
/// <param name="EntityName">The name of the entity.</param>
/// <param name="EntityPath">The path to the entity.</param>
/// <param name="Action">The action performed.</param>
/// <param name="ChangedById">The ID of the user who made the change.</param>
/// <param name="ChangedByName">The name of the user who made the change.</param>
/// <param name="ChangedAt">The timestamp of the change.</param>
/// <param name="Summary">A summary of the change.</param>
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
    /// <summary>
    /// Mapping for all audit-related endpoints. This includes listing all audit entries, as well as retrieving the history of specific categories and items.
    /// </summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The updated endpoint route builder.</returns>
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/audit", ListAsync)
            .WithTags("Audit")
            .WithName("ListAuditEntries")
            .WithSummary("Every recorded change, newest first.")
            .RequireAuthorization(AuthPolicies.Admin);

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
    /// Lists audit entries with optional filtering by entity type, action, id, changed by id and a timestamp. Supports pagination.
    /// </summary>
    /// <param name="db">The database context.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <param name="entityType">The type of the entity.</param>
    /// <param name="action">The action performed.</param>
    /// <param name="entityId">The ID of the entity.</param>
    /// <param name="changedById">The ID of the user who made the change.</param>
    /// <param name="since">The timestamp to filter from.</param>
    /// <param name="page">The page number.</param>
    /// <param name="pageSize">The number of items per page.</param>
    /// <returns>The list of audit entries.</returns>
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

        if (!TryParseDateTimeOffset(since, out var from))
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

    /// <summary>
    /// Category history endpoint. Returns the audit history for a specific category.
    /// </summary>
    /// <param name="id">The ID of the category.</param>
    /// <param name="db">The database context.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <param name="page">The page number.</param>
    /// <param name="pageSize">The number of items per page.</param>
    /// <returns>The audit history for the specified category.</returns>
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
    
    /// <summary>
    /// Item history endpoint. Returns the audit history for a specific item.
    /// </summary>
    /// <param name="id">The ID of the item.</param>
    /// <param name="principal">The claims principal.</param>
    /// <param name="db">The database context.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <param name="page">The page number.</param>
    /// <param name="pageSize">The number of items per page.</param>
    /// <returns>The audit history for the specified item.</returns>
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

        // Select the audit entries for the specified item, ordered by ChangedAt descending and then by Id descending.
        var query = db.AuditEntries.AsNoTracking()
            .Where(a => a.EntityType == AuditedEntityType.Item && a.EntityId == id)
            .OrderByDescending(a => a.ChangedAt)
            .ThenByDescending(a => a.Id);

        return TypedResults.Ok(await PageAsync(query, page, pageSize, ct));
    }


    /// <summary>
    /// Trying to parse a string value into an enum of type.
    /// </summary>
    /// <typeparam name="T">The enum type to parse into.</typeparam>
    /// <param name="value">The string value to parse.</param>
    /// <param name="parsed">The parsed enum value.</param>
    /// <returns>True if the value was successfully parsed; otherwise, false.</returns>
    private static bool TryParse<T>(string? value, out T? parsed) where T : struct, Enum
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value)) return true;

        if (!Enum.TryParse<T>(value, ignoreCase: true, out var result)) return false;

        parsed = result;
        return true;
    }

    /// <summary>
    /// Tries to parse a string value into a nullable Guid.
    /// </summary>
    /// <param name="value">The string value to parse.</param>
    /// <param name="parsed">The parsed Guid value.</param>
    /// <returns>True if the value was successfully parsed; otherwise, false.</returns>
    private static bool TryParseGuid(string? value, out Guid? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value)) return true;

        if (!Guid.TryParse(value, out var result)) return false;

        parsed = result;
        return true;
    }

    /// <summary>
    /// Tries to parse a string value into a nullable DateTimeOffset.
    /// </summary>
    /// <param name="value">The string value to parse.</param>
    /// <param name="parsed">The parsed DateTimeOffset value.</param>
    /// <returns>True if the value was successfully parsed; otherwise, false.</returns>
    private static bool TryParseDateTimeOffset(string? value, out DateTimeOffset? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value)) return true;

        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal, out var result)) return false;

        parsed = result;
        return true;
    }

    /// <summary>
    /// Filter error response for invalid query parameters.
    /// </summary>
    /// <param name="name">The name of the query parameter.</param>
    /// <param name="value">The invalid value of the query parameter.</param>
    /// <returns>The problem HTTP result.</returns>
    private static ProblemHttpResult BadFilter(string name, string? value) =>
        TypedResults.Problem(
            $"'{value}' is not a valid value for {name}.",
            statusCode: StatusCodes.Status400BadRequest);

    /// <summary>
    /// Paginates the given query and returns a PagedResult of AuditEntryResponse.
    /// </summary>
    /// <param name="query">The query to paginate.</param>
    /// <param name="page">The page number.</param>
    /// <param name="pageSize">The number of items per page.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The paged result.</returns>
    private static async Task<PagedResult<AuditEntryResponse>> PageAsync(
        IQueryable<AuditEntry> query,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var (p, size) = Paging.Normalize(page, pageSize);
        var total = await query.CountAsync(ct);

        // Select the entries for the current page, ordered by ChangedAt descending and then by Id descending.
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
