using System.Net;
using System.Text.Json;

namespace DeBoetAPI.Tests.Integration;

/// <summary>
/// The docs are opt-in, and both halves of that matter: shipping them unintentionally exposes
/// the API's whole shape, while an enable flag that does nothing is worse than no flag.
/// Every case here runs under the Production environment the VPS uses.
/// </summary>
[Collection(PostgresCollection.Name)]
public class OpenApiTests(PostgresFixture postgres)
{
    [Fact]
    public async Task DocsAreNotServedByDefault()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/scalar/v1")).StatusCode);
    }

    [Fact]
    public async Task DocsAreServedWhenExplicitlyEnabled()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, enableOpenApi: true);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/scalar/v1")).StatusCode);
    }

    [Fact]
    public async Task EnablingDocsDoesNotEnableDeveloperExceptionPages()
    {
        // The whole point of a separate flag: docs without Development's other behaviour.
        // A failure must still come back as ProblemDetails, never as a stack trace.
        await using var factory = new ApiFactory("Host=127.0.0.1;Port=1;Database=nope;Username=x;Password=y;Timeout=2",
            enableOpenApi: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Npgsql.NpgsqlException", body);
        Assert.DoesNotContain("at Npgsql", body);
    }

    [Fact]
    public async Task SpecDeclaresTheBearerSchemeAndMarksSecuredOperations()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, enableOpenApi: true);
        using var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var root = document.RootElement;

        var scheme = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());

        var paths = root.GetProperty("paths");

        // Login must stay open, or a generated client cannot obtain a token at all.
        Assert.False(paths.GetProperty("/api/v1/auth/login").GetProperty("post")
            .TryGetProperty("security", out _));

        // Admin-only writes must be marked, or an imported collection sends them bare.
        Assert.True(paths.GetProperty("/api/v1/categories").GetProperty("post")
            .TryGetProperty("security", out _));
    }
}
