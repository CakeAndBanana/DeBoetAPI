using Npgsql;
using Testcontainers.PostgreSql;

namespace DeBoetAPI.Tests.Integration;

/// <summary>
/// Supplies a real PostgreSQL instance for the integration suite.
/// <para>
/// By default a throwaway container is started via Testcontainers. If
/// <c>TEST_POSTGRES_CONNECTION</c> is set, that server is used instead and the suite creates
/// (and drops) its own scratch database on it — useful where Docker is unavailable.
/// </para>
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string ExternalServerVariable = "TEST_POSTGRES_CONNECTION";

    private PostgreSqlContainer? _container;
    private string? _externalAdminConnectionString;
    private string? _scratchDatabaseName;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable(ExternalServerVariable);

        if (string.IsNullOrWhiteSpace(external))
        {
            _container = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("deboetapi_test")
                .Build();

            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
            return;
        }

        // Each run gets its own database so repeated runs cannot contaminate one another.
        _externalAdminConnectionString = external;
        _scratchDatabaseName = $"deboetapi_test_{Guid.NewGuid():N}";

        await ExecuteOnAdminAsync($"CREATE DATABASE \"{_scratchDatabaseName}\"");

        ConnectionString = new NpgsqlConnectionStringBuilder(external)
        {
            Database = _scratchDatabaseName,
        }.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
            return;
        }

        if (_scratchDatabaseName is not null)
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteOnAdminAsync($"DROP DATABASE IF EXISTS \"{_scratchDatabaseName}\" WITH (FORCE)");
        }
    }

    private async Task ExecuteOnAdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_externalAdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
