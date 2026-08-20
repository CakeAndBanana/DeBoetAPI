using System.Security.Claims;
using DeBoetAPI.Common;
using DeBoetAPI.Data;
using DeBoetAPI.Data.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace DeBoetAPI.Features.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth")
            .WithTags("Auth")
            .RequireRateLimiting(RateLimitPolicies.Auth);

        group.MapPost("/register", RegisterAsync)
            .RequireAuthorization(AuthPolicies.Admin)
            .WithName("Register")
            .WithSummary("Create an account and receive a token pair.");

        group.MapPost("/login", LoginAsync)
            .WithName("Login")
            .WithSummary("Exchange credentials for a token pair.");

        group.MapPost("/refresh", RefreshAsync)
            .WithName("Refresh")
            .WithSummary("Rotate a refresh token for a new pair.");

        group.MapPost("/logout", LogoutAsync)
            .WithName("Logout")
            .WithSummary("Revoke a refresh token.");

        group.MapPost("/change-password", ChangePasswordAsync)
            .WithName("ChangePassword")
            .WithSummary("Change your own password, clearing any forced-change flag.")
            .RequireAuthorization();

        group.MapGet("/me", GetMeAsync)
            .WithName("GetCurrentUser")
            .WithSummary("Return the authenticated user.")
            .RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<TokenPairResponse>, ValidationProblem>> RegisterAsync(
        RegisterRequest request,
        UserManager<AppUser> userManager,
        TokenService tokens,
        CancellationToken ct)
    {
        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName,
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return TypedResults.ValidationProblem(ToErrorDictionary(result));
        }

        return TypedResults.Ok(await tokens.IssuePairAsync(user, ct));
    }

    private static async Task<Results<Ok<TokenPairResponse>, ProblemHttpResult>> LoginAsync(
        LoginRequest request,
        UserManager<AppUser> userManager,
        TokenService tokens,
        CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(request.Email)
                   ?? await userManager.FindByNameAsync(request.Email);

        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
        {
            return TypedResults.Problem("Invalid email or password.", statusCode: StatusCodes.Status401Unauthorized);
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return TypedResults.Problem("Account is locked out.", statusCode: StatusCodes.Status423Locked);
        }

        return TypedResults.Ok(await tokens.IssuePairAsync(user, ct));
    }

    private static async Task<Results<Ok<TokenPairResponse>, ProblemHttpResult>> RefreshAsync(
        RefreshRequest request,
        TokenService tokens,
        CancellationToken ct)
    {
        var pair = await tokens.RotateAsync(request.RefreshToken, ct);

        return pair is null
            ? TypedResults.Problem("Invalid or expired refresh token.", statusCode: StatusCodes.Status401Unauthorized)
            : TypedResults.Ok(pair);
    }

    private static async Task<NoContent> LogoutAsync(
        RefreshRequest request,
        TokenService tokens,
        CancellationToken ct)
    {
        await tokens.RevokeAsync(request.RefreshToken, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<UserResponse>, UnauthorizedHttpResult>> GetMeAsync(
        ClaimsPrincipal principal,
        UserManager<AppUser> userManager)
    {
        var user = await userManager.FindByIdAsync(principal.GetUserId().ToString());

        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var roles = await userManager.GetRolesAsync(user);

        return TypedResults.Ok(new UserResponse(
            user.Id,
            user.Email ?? string.Empty,
            user.UserName ?? string.Empty,
            user.DisplayName,
            roles.ToList(),
            user.MustChangePassword,
            user.CreatedAt));
    }

    /// <summary>
    /// Changes the caller's own password. Every other session is revoked, so credentials that
    /// were shared or intercepted stop working the moment the real owner takes control.
    /// </summary>
    private static async Task<Results<NoContent, ValidationProblem, UnauthorizedHttpResult>> ChangePasswordAsync(
        ChangePasswordRequest request,
        ClaimsPrincipal principal,
        UserManager<AppUser> userManager,
        TokenService tokens,
        AuditLog audit,
        AppDbContext db,
        CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(principal.GetUserId().ToString());
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return TypedResults.ValidationProblem(ToErrorDictionary(result));
        }

        user.MustChangePassword = false;
        await userManager.UpdateAsync(user);

        // Note: no password material in the audit trail, ever.
        audit.RecordUser(user, AuditAction.PasswordChanged);
        await db.SaveChangesAsync(ct);

        await tokens.RevokeAllForUserAsync(user.Id, ct);

        return TypedResults.NoContent();
    }

    private static Dictionary<string, string[]> ToErrorDictionary(IdentityResult result) =>
        result.Errors
            .GroupBy(e => e.Code)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
}
