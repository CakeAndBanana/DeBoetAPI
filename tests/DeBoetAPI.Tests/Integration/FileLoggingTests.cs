using System.Net.Http.Json;

namespace DeBoetAPI.Tests.Integration;

/// <summary>
/// The file sink, exercised for real. A logger that silently writes nothing looks exactly
/// like a quiet application, so these assert on files actually appearing on disk with the
/// expected content.
/// </summary>
[Collection(PostgresCollection.Name)]
public class FileLoggingTests(PostgresFixture postgres) : IAsyncLifetime
{
    private string _logDirectory = null!;

    public Task InitializeAsync()
    {
        _logDirectory = Path.Combine(Path.GetTempPath(), $"deboetapi-logtest-{Guid.NewGuid():N}");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_logDirectory))
            {
                Directory.Delete(_logDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A held file handle should never fail the suite.
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task RequestsAreWrittenToADatedLogFile()
    {
        await using (var factory = new ApiFactory(postgres.ConnectionString, logDirectory: _logDirectory))
        {
            await factory.MigrateAsync();
            using var client = factory.CreateClient();

            await client.GetAsync("/api/v1/categories");
        }

        var files = Directory.GetFiles(_logDirectory, "*.log");
        var file = Assert.Single(files);

        // The name carries the date, which is what makes each day a separate file.
        Assert.Contains(DateTime.Now.ToString("yyyyMMdd"), Path.GetFileName(file));
        Assert.StartsWith("deboetapi-", Path.GetFileName(file));

        var contents = await ReadSharedAsync(file);
        Assert.Contains("/api/v1/categories", contents);
    }

    [Fact]
    public async Task UnauthorizedRequestsAreRecordedWithTheirStatus()
    {
        await using (var factory = new ApiFactory(postgres.ConnectionString, logDirectory: _logDirectory))
        {
            await factory.MigrateAsync();
            using var client = factory.CreateClient();

            await client.GetAsync("/api/v1/items");
        }

        var contents = await ReadSharedAsync(Directory.GetFiles(_logDirectory, "*.log").Single());

        Assert.Contains("401", contents);
    }

    [Fact]
    public async Task ApplicationLogsReachTheFile()
    {
        await using (var factory = new ApiFactory(postgres.ConnectionString, logDirectory: _logDirectory))
        {
            await factory.MigrateAsync();
            await factory.SeedAdminAsync();
            using var client = factory.CreateClient();

            // Registering then replaying a refresh token makes TokenService log a warning —
            // a real application log line rather than request noise.
            var registered = await client.PostAsJsonAsync("/api/v1/auth/register", new
            {
                email = $"logs-{Guid.NewGuid():N}@example.com",
                password = "Passw0rd!",
                displayName = "Log Check",
            });
            registered.EnsureSuccessStatusCode();
            var tokens = (await registered.Content.ReadFromJsonAsync<DeBoetAPI.Features.Auth.TokenPairResponse>())!;

            await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = tokens.RefreshToken });
            await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = tokens.RefreshToken });
        }

        var contents = await ReadSharedAsync(Directory.GetFiles(_logDirectory, "*.log").Single());

        Assert.Contains("Refresh token reuse detected", contents);
        Assert.Contains("TokenService", contents); // the source context is preserved
    }

    [Fact]
    public async Task DisabledFileLogging_WritesNothing()
    {
        await using (var factory = new ApiFactory(postgres.ConnectionString))
        {
            await factory.MigrateAsync();
            using var client = factory.CreateClient();
            await client.GetAsync("/health");
        }

        Assert.False(Directory.Exists(_logDirectory));
    }

    [Fact]
    public async Task UnwritableDirectory_DoesNotStopTheApplication()
    {
        // A wrongly-owned or read-only mount must degrade to console logging, not take the
        // whole API down on boot.
        var conflicting = Path.Combine(Path.GetTempPath(), $"deboetapi-notadir-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(conflicting, "this is a file, not a directory");

        try
        {
            await using var factory = new ApiFactory(postgres.ConnectionString, logDirectory: conflicting);
            await factory.MigrateAsync();
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/health");

            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            File.Delete(conflicting);
        }
    }

    /// <summary>The sink keeps the file open, so it has to be read with sharing allowed.</summary>
    private static async Task<string> ReadSharedAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
