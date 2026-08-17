using System.Net.Http.Headers;
using System.Net.Http.Json;
using DeBoetAPI.Features.Auth;

namespace DeBoetAPI.Tests.Integration;

[Collection(PostgresCollection.Name)]
public abstract class IntegrationTestBase(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    protected HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory(postgres.ConnectionString);
        await _factory.MigrateAsync();
        Client = _factory.CreateClient();
    }

    public Task DisposeAsync()
    {
        Client.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    /// <summary>Registers a fresh user. Emails are unique per call so tests never collide.</summary>
    protected async Task<TokenPairResponse> RegisterAsync(string? email = null, string password = "Passw0rd!")
    {
        var response = await Client.PostAsJsonAsync("/api/v1/auth/register", new
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
