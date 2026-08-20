using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DeBoetAPI.Features.Auth;

namespace DeBoetAPI.Tests.Integration;

public class AuthEndpointsTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    [Fact]
    public async Task Register_ReturnsUsableTokenPair()
    {
        var tokens = await RegisterAsync();

        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
        Assert.True(tokens.AccessTokenExpiresAt > DateTimeOffset.UtcNow);
        Assert.True(tokens.RefreshTokenExpiresAt > tokens.AccessTokenExpiresAt);
    }

    [Fact]
    public async Task Register_RejectsDuplicateEmail()
    {
        var email = $"dupe-{Guid.NewGuid():N}@example.com";
        await RegisterAsync(email);

        var response = await Admin.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "Passw0rd!",
            displayName = "Someone Else",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_RejectsInvalidPayload()
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = "not-an-email",
            password = "short",
            displayName = "",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblem>();
        Assert.NotNull(problem);
        Assert.Contains("Email", problem.Errors.Keys);
        Assert.Contains("Password", problem.Errors.Keys);
    }

    [Fact]
    public async Task Login_SucceedsWithCorrectPassword()
    {
        var email = $"login-{Guid.NewGuid():N}@example.com";
        await RegisterAsync(email);

        var response = await Client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Passw0rd!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(await response.Content.ReadFromJsonAsync<TokenPairResponse>());
    }

    [Fact]
    public async Task Login_WithWrongPassword_IsIndistinguishableFromUnknownUser()
    {
        var email = $"known-{Guid.NewGuid():N}@example.com";
        await RegisterAsync(email);

        var wrongPassword = await Client.PostAsJsonAsync("/api/v1/auth/login",
            new { email, password = "WrongPassword!" });
        var unknownUser = await Client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = $"ghost-{Guid.NewGuid():N}@example.com", password = "WrongPassword!" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);

        // traceId is per-request by design, so compare everything else.
        Assert.Equal(
            await WithoutTraceIdAsync(wrongPassword),
            await WithoutTraceIdAsync(unknownUser));
    }

    [Fact]
    public async Task Me_ReturnsAuthenticatedUser()
    {
        var email = $"me-{Guid.NewGuid():N}@example.com";
        var tokens = await RegisterAsync(email);

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var response = await Client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(user);
        Assert.Equal(email, user.Email);
        Assert.Equal("Test User", user.DisplayName);
        Assert.NotEqual(Guid.Empty, user.Id);
    }

    [Fact]
    public async Task Me_WithoutToken_IsUnauthorized()
    {
        var response = await Client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithTokenSignedByADifferentKey_IsUnauthorized()
    {
        var forged = ForgedTokens.WithSigningKey("a-completely-different-signing-key-32-bytes!!");

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forged);
        var response = await Client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_IssuesNewPairAndRetiresTheOldToken()
    {
        var original = await RegisterAsync();

        var response = await Client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = original.RefreshToken });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rotated = await response.Content.ReadFromJsonAsync<TokenPairResponse>();
        Assert.NotNull(rotated);
        Assert.NotEqual(original.RefreshToken, rotated.RefreshToken);

        // The new access token must actually work.
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rotated.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Refresh_ReplayingAConsumedToken_RevokesTheEntireChain()
    {
        var original = await RegisterAsync();

        var firstRotation = await Client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = original.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, firstRotation.StatusCode);
        var rotated = (await firstRotation.Content.ReadFromJsonAsync<TokenPairResponse>())!;

        // Replay the already-consumed token.
        var replay = await Client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = original.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // The legitimately-rotated token must now be dead too — the whole chain is burned.
        var afterDetection = await Client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = rotated.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, afterDetection.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithUnknownToken_IsUnauthorized()
    {
        var response = await Client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = "this-token-was-never-issued" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesTheRefreshToken()
    {
        var tokens = await RegisterAsync();

        var logout = await Client.PostAsJsonAsync("/api/v1/auth/logout",
            new { refreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var refresh = await Client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Logout_WithUnknownToken_StillSucceeds()
    {
        // Logout is deliberately not an oracle for token validity.
        var response = await Client.PostAsJsonAsync("/api/v1/auth/logout",
            new { refreshToken = "never-issued" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<string> WithoutTraceIdAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return string.Join('|', document.RootElement
            .EnumerateObject()
            .Where(p => p.Name != "traceId")
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"{p.Name}={p.Value}"));
    }

    private sealed record ValidationProblem(Dictionary<string, string[]> Errors);
}
