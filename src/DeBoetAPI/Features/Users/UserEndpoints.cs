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

/// <summary>
/// The UserEndpoints class defines the API endpoints for managing users in the application.
/// </summary>
public static class UserEndpoints
{
    private static readonly DateTimeOffset DisabledUntil = new(9999, 12, 31, 23, 59, 59, TimeSpan.Zero);

    /// <summary>
    /// Mapping the user endpoints to the specified IEndpointRouteBuilder.
    /// </summary>
    /// <param name="app">The IEndpointRouteBuilder to map the endpoints to.</param>
    /// <returns>The updated IEndpointRouteBuilder.</returns>
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

    /// <summary>
    /// Lists users with optional search and pagination parameters.
    /// </summary>
    /// <param name="db">The application database context.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <param name="search">The search term.</param>
    /// <param name="page">The page number.</param>
    /// <param name="pageSize">The page size.</param>
    /// <returns>The paged list of users.</returns>
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

    /// <summary>
    /// Creates a new user with the specified details and roles, and returns the created user information.
    /// </summary>
    /// <param name="request">The request containing the user details and roles.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="audit">The audit log.</param>
    /// <param name="db">The application database context.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The created user information.</returns>
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

    /// <summary>
    /// Roles are set for a user with the specified ID, and the updated user information is returned.
    /// </summary>
    /// <param name="id">The ID of the user for whom to set roles.</param>
    /// <param name="request">The request containing the roles to set.</param>
    /// <param name="principal">The principal representing the current user.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="tokens">The token service.</param>
    /// <param name="audit">The audit log.</param>
    /// <param name="db">The application database context.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The updated user information.</returns>
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

    /// <summary>
    /// The resets the password for a user with the specified ID and returns a no-content response if successful.
    /// </summary>
    /// <param name="id">The ID of the user for whom to reset the password.</param>
    /// <param name="request">The request containing the new password.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="tokens">The token service.</param>
    /// <param name="audit">The audit log.</param>
    /// <param name="db">The application database context.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The result of the operation.</returns>
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

    /// <summary>
    /// Disables the user account with the specified ID and returns a no-content response if successful.
    /// </summary>
    /// <param name="id">The ID of the user to disable.</param>
    /// <param name="principal">The principal representing the authenticated user.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="tokens">The token service.</param>
    /// <param name="audit">The audit log.</param>
    /// <param name="db">The application database context.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    private static Task<Results<NoContent, NotFound, ProblemHttpResult>> DisableAsync(
        Guid id, ClaimsPrincipal principal, UserManager<AppUser> userManager,
        TokenService tokens, AuditLog audit, AppDbContext db, CancellationToken ct) =>
        SetEnabledAsync(id, false, principal, userManager, tokens, audit, db, ct);

    /// <summary>
    /// Enables the user account with the specified ID and returns a no-content response if successful.
    /// </summary>
    /// <param name="id">The ID of the user to enable.</param>
    /// <param name="principal">The principal representing the authenticated user.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="tokens">The token service.</param>
    /// <param name="audit">The audit log.</param>
    /// <param name="db">The application database context.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    private static Task<Results<NoContent, NotFound, ProblemHttpResult>> EnableAsync(
        Guid id, ClaimsPrincipal principal, UserManager<AppUser> userManager,
        TokenService tokens, AuditLog audit, AppDbContext db, CancellationToken ct) =>
        SetEnabledAsync(id, true, principal, userManager, tokens, audit, db, ct);

    /// <summary>
    /// Sets the enabled state of a user account with the specified ID and returns a no-content response if successful.
    /// </summary>
    /// <param name="id">The ID of the user to set the enabled state for.</param>
    /// <param name="enabled">The enabled state to set.</param>
    /// <param name="principal">The principal representing the authenticated user.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="tokens">The token service.</param>
    /// <param name="audit">The audit log.</param>
    /// <param name="db">The application database context.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The result of the operation.</returns>
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

    /// <summary>
    /// Converts an AppUser entity to a ManagedUserResponse DTO, including the user's roles.
    /// </summary>
    /// <param name="user">The AppUser entity to convert.</param>
    /// <param name="userManager">The user manager.</param>
    /// <returns>The ManagedUserResponse DTO.</returns>
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

    /// <summary>
    /// Checks if the provided roles are valid and returns an error message if any unknown roles are found.
    /// </summary>
    /// <param name="roles">The list of roles to validate.</param>
    /// <returns>The error message if any unknown roles are found; otherwise, null.</returns>
    private static string? InvalidRole(IReadOnlyList<string> roles)
    {
        var unknown = roles.Except(AuthPolicies.AssignableRoles, StringComparer.Ordinal).ToList();

        return unknown.Count == 0
            ? null
            : $"Unknown role(s): {string.Join(", ", unknown)}. Valid roles are {string.Join(", ", AuthPolicies.AssignableRoles)}.";
    }

    /// <summary>
    /// Describes the provided roles as a comma-separated string, or "(none)" if the list is empty.
    /// </summary>
    /// <param name="roles">The list of roles to describe.</param>
    /// <returns>The comma-separated string of roles or "(none)" if the list is empty.</returns>
    private static string Describe(IEnumerable<string> roles)
    {
        var list = roles.ToList();
        return list.Count == 0 ? "(none)" : string.Join(", ", list);
    }

    /// <summary>
    /// Converts the errors from an IdentityResult into a dictionary where the keys are error codes and the values are arrays of error descriptions.
    /// </summary>
    /// <param name="result">The IdentityResult containing the errors.</param>
    /// <returns>The dictionary of errors.</returns>
    private static Dictionary<string, string[]> ToErrors(IdentityResult result) =>
        result.Errors
            .GroupBy(e => e.Code)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
}
