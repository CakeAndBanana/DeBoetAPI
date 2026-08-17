using System.Security.Claims;
using DeBoetAPI.Data.Entities;
using DeBoetAPI.Common;
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

        return user is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(new UserResponse(user.Id, user.Email ?? string.Empty, user.DisplayName, user.CreatedAt));
    }

    private static Dictionary<string, string[]> ToErrorDictionary(IdentityResult result) =>
        result.Errors
            .GroupBy(e => e.Code)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
}
