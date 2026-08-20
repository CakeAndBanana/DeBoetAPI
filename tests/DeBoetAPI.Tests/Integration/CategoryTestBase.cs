using System.Net.Http.Headers;
using System.Net.Http.Json;
using DeBoetAPI.Features.Auth;
using DeBoetAPI.Features.Categories;

namespace DeBoetAPI.Tests.Integration;

[Collection(PostgresCollection.Name)]
public abstract class CategoryTestBase(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    /// <summary>Signed in as the seeded <c>admin</c> account.</summary>
    protected HttpClient Admin { get; private set; } = null!;

    /// <summary>Signed in as an ordinary registered user.</summary>
    protected HttpClient User { get; private set; } = null!;

    protected HttpClient Anonymous { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory(postgres.ConnectionString);
        await _factory.MigrateAsync();
        await _factory.SeedAdminAsync();

        Anonymous = _factory.CreateClient();
        Admin = await SignInAsAdminAsync();
        User = await RegisterAndSignInAsync();
    }

    public Task DisposeAsync()
    {
        Admin.Dispose();
        User.Dispose();
        Anonymous.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    private async Task<HttpClient> SignInAsAdminAsync()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = AdminSeedOptions.DefaultUsername,
            password = AdminSeedOptions.DefaultPassword,
        });

        response.EnsureSuccessStatusCode();
        var tokens = (await response.Content.ReadFromJsonAsync<TokenPairResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    private async Task<HttpClient> RegisterAndSignInAsync()
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = $"user-{Guid.NewGuid():N}@example.com",
            password = "Passw0rd!",
            displayName = "Ordinary User",
        });

        response.EnsureSuccessStatusCode();
        var tokens = (await response.Content.ReadFromJsonAsync<TokenPairResponse>())!;

        return ClientWithToken(tokens.AccessToken);
    }

    /// <summary>A client authenticated as whoever the access token belongs to.</summary>
    protected HttpClient ClientWithToken(string accessToken)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    /// <summary>An unauthenticated client, for driving a fresh sign-in.</summary>
    protected HttpClient NewClient() => _factory.CreateClient();

    /// <summary>
    /// Provisions an account through the real admin API and signs in as it, so tests exercise
    /// the same path an administrator would.
    /// </summary>
    protected async Task<(HttpClient Client, Guid Id, string UserName, string Password)> CreateUserAndSignInAsync(
        string? role = null,
        bool changePasswordFirst = true)
    {
        var userName = $"user{Guid.NewGuid():N}"[..16];
        const string initialPassword = "Initial!Pass1";

        var created = await Admin.PostAsJsonAsync("/api/v1/users", new
        {
            userName,
            email = $"{userName}@example.com",
            displayName = userName,
            password = initialPassword,
            roles = role is null ? Array.Empty<string>() : [role],
        });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<ManagedUser>())!.Id;

        var password = initialPassword;
        var client = await SignInAsync(userName, password);

        if (changePasswordFirst)
        {
            const string chosen = "Chosen!Pass1";
            var changed = await client.PostAsJsonAsync("/api/v1/auth/change-password", new
            {
                currentPassword = password,
                newPassword = chosen,
            });
            changed.EnsureSuccessStatusCode();

            password = chosen;
            client.Dispose();
            client = await SignInAsync(userName, password);
        }

        return (client, id, userName, password);
    }

    protected async Task<HttpClient> SignInAsync(string userName, string password)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = userName, password });
        response.EnsureSuccessStatusCode();

        var tokens = (await response.Content.ReadFromJsonAsync<TokenPairResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    protected sealed record ManagedUser(Guid Id, string UserName, string[] Roles, bool MustChangePassword, bool IsDisabled);

    protected async Task<CategoryResponse> CreateCategoryAsync(
        string name,
        Guid? parentId = null,
        bool isActive = true,
        string? slug = null)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/categories", new
        {
            name,
            slug,
            parentId,
            isActive,
        });

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;
    }

    protected async Task<CategoryDetailResponse> GetCategoryAsync(Guid id)
    {
        var response = await Admin.GetAsync($"/api/v1/categories/{id}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CategoryDetailResponse>())!;
    }

  
    protected async Task<(CategoryResponse Christmas, CategoryResponse Lights, CategoryResponse Ice)>
        CreateChristmasChainAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var christmas = await CreateCategoryAsync("Christmas", slug: $"christmas{suffix}");
        var lights = await CreateCategoryAsync("Christmas Lights", christmas.Id, slug: $"christmaslights{suffix}");
        var ice = await CreateCategoryAsync("Ice Lights", lights.Id, slug: $"icelights{suffix}");
        return (christmas, lights, ice);
    }

    protected static string UniqueSlug(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 12, 200)];
}
