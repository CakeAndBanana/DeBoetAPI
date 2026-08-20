using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using DeBoetAPI.Common;
using DeBoetAPI.Data;
using DeBoetAPI.Data.Entities;
using DeBoetAPI.Features.Auth;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DeBoetAPI.Features.Users;

public sealed record CreateUserRequest
{
    [Required, MaxLength(256)]
    public string UserName { get; init; } = string.Empty;

    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required, MaxLength(100)]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>The account starts on this password and must change it at first login.</summary>
    [Required, MinLength(8), MaxLength(128)]
    public string Password { get; init; } = string.Empty;

    /// <summary>Empty grants no rights at all — the account can sign in and nothing more.</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];
}

public sealed record SetRolesRequest
{
    [Required]
    public IReadOnlyList<string> Roles { get; init; } = [];
}

public sealed record ResetPasswordRequest
{
    [Required, MinLength(8), MaxLength(128)]
    public string NewPassword { get; init; } = string.Empty;
}

public sealed record ManagedUserResponse(
    Guid Id,
    string UserName,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles,
    bool MustChangePassword,
    bool IsDisabled,
    DateTimeOffset CreatedAt);

public static class UserEndpoints
{
    private static readonly DateTimeOffset DisabledUntil = new(9999, 12, 31, 23, 59, 59, TimeSpan.Zero);

    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/users")
            .WithTags("Users")
            .RequireAuthorization(AuthPolicies.Admin);

        group.MapGet("/", ListAsync).WithName("ListUsers");
        group.MapPost("/", CreateAsync).WithName("CreateUser");
        group.MapPut("/{id:guid}/roles", SetRolesAsync).WithName("SetUserRoles");
        group.MapPost("/{id:guid}/reset-password", ResetPasswordAsync).WithName("ResetUserPassword");
        group.MapPost("/{id:guid}/disable", DisableAsync).WithName("DisableUser");
        group.MapPost("/{id:guid}/enable", EnableAsync).WithName("EnableUser");

        return app;
    }

    private static async Task<Ok<PagedResult<ManagedUserResponse>>> ListAsync(
        AppDbContext db,
        UserManager<AppUser> userManager,
        CancellationToken ct,
        string? search = null,
        int? page = null,
        int? pageSize = null)
    {
        var (p, size) = Paging.Normalize(page, pageSize);

        var query = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(u =>
                EF.Functions.ILike(u.UserName!, $"%{search}%") ||
                EF.Functions.ILike(u.DisplayName, $"%{search}%"));
        }

        var total = await query.CountAsync(ct);

        var users = await query
            .OrderBy(u => u.UserName)
            .Skip((p - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        var results = new List<ManagedUserResponse>(users.Count);
        foreach (var user in users)
        {
            results.Add(await ToResponseAsync(user, userManager));
        }

        return TypedResults.Ok(new PagedResult<ManagedUserResponse>(results, p, size, total));
    }

    private static async Task<Results<Created<ManagedUserResponse>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        CreateUserRequest request,
        UserManager<AppUser> userManager,
        AuditLog audit,
        AppDbContext db,
        CancellationToken ct)
    {
        if (InvalidRole(request.Roles) is { } bad)
        {
            return TypedResults.Problem(bad, statusCode: StatusCodes.Status400BadRequest);
        }

        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            UserName = request.UserName,
            Email = request.Email,
            DisplayName = request.DisplayName,
            MustChangePassword = true,
        };

        var created = await userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            return TypedResults.ValidationProblem(ToErrors(created));
        }

        if (request.Roles.Count > 0)
        {
            await userManager.AddToRolesAsync(user, request.Roles);
        }

        audit.RecordUser(user, AuditAction.Created,
            request.Roles.Count == 0 ? "no roles" : $"roles: {string.Join(", ", request.Roles)}");
        await db.SaveChangesAsync(ct);

        var response = await ToResponseAsync(user, userManager);
        return TypedResults.Created($"/api/v1/users/{user.Id}", response);
    }

    private static async Task<Results<Ok<ManagedUserResponse>, NotFound, ProblemHttpResult>> SetRolesAsync(
        Guid id,
        SetRolesRequest request,
        ClaimsPrincipal principal,
        UserManager<AppUser> userManager,
        TokenService tokens,
        AuditLog audit,
        AppDbContext db,
        CancellationToken ct)
    {
        if (InvalidRole(request.Roles) is { } bad)
        {
            return TypedResults.Problem(bad, statusCode: StatusCodes.Status400BadRequest);
        }

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return TypedResults.NotFound();
        }

        if (id == principal.GetUserId() && !request.Roles.Contains(AuthPolicies.AdminRole))
        {
            return TypedResults.Problem(
                "You cannot remove your own Admin role.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var current = await userManager.GetRolesAsync(user);
        await userManager.RemoveFromRolesAsync(user, current.Except(request.Roles));
        await userManager.AddToRolesAsync(user, request.Roles.Except(current));

        audit.RecordUser(user, AuditAction.RolesChanged,
            $"{Describe(current)} -> {Describe(request.Roles)}");
        await db.SaveChangesAsync(ct);

        await tokens.RevokeAllForUserAsync(user.Id, ct);

        return TypedResults.Ok(await ToResponseAsync(user, userManager));
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> ResetPasswordAsync(
        Guid id,
        ResetPasswordRequest request,
        UserManager<AppUser> userManager,
        TokenService tokens,
        AuditLog audit,
        AppDbContext db,
        CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return TypedResults.NotFound();
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, request.NewPassword);

        if (!result.Succeeded)
        {
            return TypedResults.ValidationProblem(ToErrors(result));
        }

        user.MustChangePassword = true;
        await userManager.UpdateAsync(user);

        audit.RecordUser(user, AuditAction.PasswordReset);
        await db.SaveChangesAsync(ct);

        await tokens.RevokeAllForUserAsync(user.Id, ct);

        return TypedResults.NoContent();
    }

    private static Task<Results<NoContent, NotFound, ProblemHttpResult>> DisableAsync(
        Guid id, ClaimsPrincipal principal, UserManager<AppUser> userManager,
        TokenService tokens, AuditLog audit, AppDbContext db, CancellationToken ct) =>
        SetEnabledAsync(id, false, principal, userManager, tokens, audit, db, ct);

    private static Task<Results<NoContent, NotFound, ProblemHttpResult>> EnableAsync(
        Guid id, ClaimsPrincipal principal, UserManager<AppUser> userManager,
        TokenService tokens, AuditLog audit, AppDbContext db, CancellationToken ct) =>
        SetEnabledAsync(id, true, principal, userManager, tokens, audit, db, ct);

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> SetEnabledAsync(
        Guid id,
        bool enabled,
        ClaimsPrincipal principal,
        UserManager<AppUser> userManager,
        TokenService tokens,
        AuditLog audit,
        AppDbContext db,
        CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return TypedResults.NotFound();
        }

        if (!enabled && id == principal.GetUserId())
        {
            return TypedResults.Problem(
                "You cannot disable your own account.",
                statusCode: StatusCodes.Status409Conflict);
        }

        await userManager.SetLockoutEnabledAsync(user, true);
        await userManager.SetLockoutEndDateAsync(user, enabled ? null : DisabledUntil);

        audit.RecordUser(user, enabled ? AuditAction.Enabled : AuditAction.Disabled);
        await db.SaveChangesAsync(ct);

        if (!enabled)
        {
            await tokens.RevokeAllForUserAsync(user.Id, ct);
        }

        return TypedResults.NoContent();
    }

    private static async Task<ManagedUserResponse> ToResponseAsync(AppUser user, UserManager<AppUser> userManager)
    {
        var roles = await userManager.GetRolesAsync(user);

        return new ManagedUserResponse(
            user.Id,
            user.UserName ?? string.Empty,
            user.Email ?? string.Empty,
            user.DisplayName,
            roles.ToList(),
            user.MustChangePassword,
            user.LockoutEnd is not null && user.LockoutEnd > DateTimeOffset.UtcNow,
            user.CreatedAt);
    }

    private static string? InvalidRole(IReadOnlyList<string> roles)
    {
        var unknown = roles.Except(AuthPolicies.AssignableRoles, StringComparer.Ordinal).ToList();

        return unknown.Count == 0
            ? null
            : $"Unknown role(s): {string.Join(", ", unknown)}. Valid roles are {string.Join(", ", AuthPolicies.AssignableRoles)}.";
    }

    private static string Describe(IEnumerable<string> roles)
    {
        var list = roles.ToList();
        return list.Count == 0 ? "(none)" : string.Join(", ", list);
    }

    private static Dictionary<string, string[]> ToErrors(IdentityResult result) =>
        result.Errors
            .GroupBy(e => e.Code)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
}
