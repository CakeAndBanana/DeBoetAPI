using System.Net;
using System.Net.Http.Json;
using Npgsql;

namespace DeBoetAPI.Tests.Integration;

/// <summary>Health probes, rate limiting, and the shape the migrations actually produce.</summary>
[Collection(PostgresCollection.Name)]
public class InfrastructureTests(PostgresFixture postgres)
{
    [Fact]
    public async Task HealthEndpoints_ReportHealthyAgainstARealDatabase()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        await factory.MigrateAsync();
        using var client = factory.CreateClient();

        var live = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("Healthy", await live.Content.ReadAsStringAsync());

        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Healthy", await ready.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ReadinessProbe_ReportsUnhealthyWhenTheDatabaseIsUnreachable()
    {
        var unreachable = new NpgsqlConnectionStringBuilder(postgres.ConnectionString)
        {
            Port = 1,
            Timeout = 2,
        }.ConnectionString;

        await using var factory = new ApiFactory(unreachable);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task AuthEndpoints_AreRateLimited()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, authPermitPerMinute: 3);
        await factory.MigrateAsync();
        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            var response = await client.PostAsJsonAsync("/api/v1/auth/login",
                new { email = "nobody@example.com", password = "WrongPassword!" });
            statuses.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
        Assert.Equal(HttpStatusCode.Unauthorized, statuses[0]);
    }

    [Fact]
    public async Task ItemEndpoints_AreNotRateLimited()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, authPermitPerMinute: 3);
        await factory.MigrateAsync();
        using var client = factory.CreateClient();

        for (var i = 0; i < 6; i++)
        {
            var response = await client.GetAsync("/api/v1/items");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task Migrations_ProduceSnakeCaseSchema()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        await factory.MigrateAsync();

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'", connection);
        await using var reader = await command.ExecuteReaderAsync();

        var tables = new List<string>();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        Assert.Contains("items", tables);
        Assert.Contains("refresh_tokens", tables);
        Assert.Contains("asp_net_users", tables);
        Assert.DoesNotContain(tables, t => t.Any(char.IsUpper));
    }

    [Fact]
    public async Task RefreshTokens_AreNeverStoredInPlaintext()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        await factory.MigrateAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = $"hash-{Guid.NewGuid():N}@example.com",
            password = "Passw0rd!",
            displayName = "Hash Check",
        });
        response.EnsureSuccessStatusCode();
        var issued = (await response.Content.ReadFromJsonAsync<DeBoetAPI.Features.Auth.TokenPairResponse>())!;

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM refresh_tokens WHERE token_hash = @raw", connection);
        command.Parameters.AddWithValue("raw", issued.RefreshToken);

        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }
}
