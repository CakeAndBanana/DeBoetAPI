using System.Security.Claims;
using DeBoetAPI.Common;
using DeBoetAPI.Data;
using DeBoetAPI.Data.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace DeBoetAPI.Features.Auth;

/// <summary>
/// Authentication endpoints for the application. 
/// These endpoints handle user registration, login, token refresh, logout, password change, and retrieving the current authenticated user.
/// </summary>
public static class AuthEndpoints
{
    /// <summary>
    /// Endpoints for authentication and user management. 
    /// These endpoints are grouped under the "/api/v1/auth" route and include functionality for registering, logging in, refreshing tokens, logging out, changing passwords, and retrieving the current authenticated user.
    /// </summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The updated endpoint route builder.</returns>
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

        // Requires to be authorized user to access the /me and /change-password endpoint, which returns the authenticated user's information.
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

    /// <summary>
    /// Registers a new user account and issues a token pair (access and refresh tokens) upon successful registration. 
    /// This endpoint requires admin authorization to create new users.
    /// </summary>
    /// <param name="request">The registration request.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="tokens">The token service.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The result of the registration attempt.</returns>
    private static async Task<Results<Ok<TokenPairResponse>, ValidationProblem>> RegisterAsync(
        RegisterRequest request,
        UserManager<AppUser> userManager,
        TokenService tokens,
        CancellationToken ct)
    {
        // Creates new user with the given information from the DTO and generates a new GUID for the user ID.
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

    /// <summary>
    /// Logins a user by validating their credentials and issuing a token pair (access and refresh tokens) upon successful authentication.
    /// </summary>
    /// <param name="request">The login request.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="tokens">The token service.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The result of the login attempt.</returns>
    private static async Task<Results<Ok<TokenPairResponse>, ProblemHttpResult>> LoginAsync(
        LoginRequest request,
        UserManager<AppUser> userManager,
        TokenService tokens,
        CancellationToken ct)
    {
        // Finds the user by email or username and checks if the password is correct.
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

    /// <summary>
    /// Refreshes the access token using a valid refresh token. 
    /// If the refresh token is valid and not expired, a new token pair is issued. Otherwise, an error response is returned.
    /// </summary>
    /// <param name="request">The refresh request.</param>
    /// <param name="tokens">The token service.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The result of the refresh attempt.</returns>

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

    /// <summary>
    /// Logouts a user by revoking the provided refresh token.
    /// </summary>
    /// <param name="request">The logout request.</param>
    /// <param name="tokens">The token service.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The result of the logout attempt.</returns>
    private static async Task<NoContent> LogoutAsync(
        RefreshRequest request,
        TokenService tokens,
        CancellationToken ct)
    {
        await tokens.RevokeAsync(request.RefreshToken, ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Gets the currently authenticated user's information.
    /// </summary>
    /// <param name="principal">The claims principal.</param>
    /// <param name="userManager">The user manager.</param>
    /// <returns>The result of the operation.</returns>
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
            [.. roles],
            user.MustChangePassword,
            user.CreatedAt));
    }

    /// <summary>
    /// Changes the password of the currently authenticated user.
    /// </summary>
    /// <param name="request">The change password request.</param>
    /// <param name="principal">The claims principal.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="tokens">The token service.</param>
    /// <param name="audit">The audit log.</param>
    /// <param name="db">The database context.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The result of the operation.</returns>
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

        // Audit would never show the old and new password.
        audit.RecordUser(user, AuditAction.PasswordChanged);
        await db.SaveChangesAsync(ct);

        await tokens.RevokeAllForUserAsync(user.Id, ct);

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Returns an error dictionary from the IdentityResult.
    /// </summary>
    /// <param name="result">The IdentityResult.</param>
    /// <returns>The error dictionary.</returns>
    private static Dictionary<string, string[]> ToErrorDictionary(IdentityResult result) =>
        result.Errors
            .GroupBy(e => e.Code)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
}
