using System.Net;
using System.Net.Http.Json;
using DeBoetAPI.Features.Auth;

namespace DeBoetAPI.Tests.Integration;

/// <summary>
/// The bootstrap administrator. If this account cannot sign in, every admin route on a fresh
/// database is unreachable and there is no way to fix it through the API.
/// </summary>
[Collection(PostgresCollection.Name)]
public class AdminSeedTests(PostgresFixture postgres)
{
    [Fact]
    public async Task SeededAdmin_SignsInWithTheDocumentedCredentials()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        await factory.MigrateAsync();
        await factory.SeedAdminAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "admin", password = "deboet" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(await response.Content.ReadFromJsonAsync<TokenPairResponse>());
    }

    [Fact]
    public async Task SeededAdmin_AlsoSignsInWithItsEmail()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        await factory.MigrateAsync();
        await factory.SeedAdminAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = AdminSeedOptions.DefaultEmail, password = AdminSeedOptions.DefaultPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SeededAdmin_RejectsTheWrongPassword()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        await factory.MigrateAsync();
        await factory.SeedAdminAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "admin", password = "not-the-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Seeding_IsIdempotent()
    {
        // Every container restart re-runs this; a second pass must not fail or duplicate.
        await using var factory = new ApiFactory(postgres.ConnectionString);
        await factory.MigrateAsync();
        await factory.SeedAdminAsync();
        await factory.SeedAdminAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "admin", password = "deboet" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ShortSeedPassword_IsAcceptedDespiteThePasswordPolicy()
    {
        // "deboet" is six characters and the policy demands eight. The seeder hashes directly
        // so the configured value works, while ordinary registration stays constrained —
        // asserted by the second half of this test.
        await using var factory = new ApiFactory(postgres.ConnectionString);
        await factory.MigrateAsync();
        await factory.SeedAdminAsync();
        using var client = factory.CreateClient();

        var adminLogin = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "admin", password = "deboet" });
        Assert.Equal(HttpStatusCode.OK, adminLogin.StatusCode);

        // Registration is admin-only now, so the call below has to be authenticated.
        var adminTokens = (await adminLogin.Content.ReadFromJsonAsync<TokenPairResponse>())!;
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminTokens.AccessToken);

        var register = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = $"short-{Guid.NewGuid():N}@example.com",
            password = "deboet",
            displayName = "Short Password",
        });

        Assert.Equal(HttpStatusCode.BadRequest, register.StatusCode);
    }
}
