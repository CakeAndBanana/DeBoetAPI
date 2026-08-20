using System.Net.Http.Headers;
using System.Net.Http.Json;
using DeBoetAPI.Features.Auth;

namespace DeBoetAPI.Tests.Integration;

[Collection(PostgresCollection.Name)]
public abstract class IntegrationTestBase(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    protected HttpClient Client { get; private set; } = null!;

    /// <summary>
    /// The seeded administrator. Needed because accounts are provisioned rather than
    /// self-served — <c>/auth/register</c> is admin-only.
    /// </summary>
    protected HttpClient Admin { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory(postgres.ConnectionString);
        await _factory.MigrateAsync();
        await _factory.SeedAdminAsync();

        Client = _factory.CreateClient();
        Admin = _factory.CreateClient();

        var login = await Admin.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = AdminSeedOptions.DefaultUsername,
            password = AdminSeedOptions.DefaultPassword,
        });
        login.EnsureSuccessStatusCode();
        var tokens = (await login.Content.ReadFromJsonAsync<TokenPairResponse>())!;
        Admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
    }

    public Task DisposeAsync()
    {
        Client.Dispose();
        Admin.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    /// <summary>Registers a fresh user. Emails are unique per call so tests never collide.</summary>
    protected async Task<TokenPairResponse> RegisterAsync(string? email = null, string password = "Passw0rd!")
    {
        // Provisioned by the admin; the response still carries the new account's tokens.
        var response = await Admin.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = email ?? $"user-{Guid.NewGuid():N}@example.com",
            password,
            displayName = "Test User",
        });

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenPairResponse>())!;
    }

    /// <summary>A client carrying a bearer token for a freshly registered user.</summary>
    protected async Task<HttpClient> AuthenticatedClientAsync()
    {
        var tokens = await RegisterAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }
}
