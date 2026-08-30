using DeBoetAPI.Data;
using DeBoetAPI.Features.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DeBoetAPI.Tests.Integration;

/// <summary>
/// Boots the real application against the fixture's database. Only configuration is
/// overridden — the DI graph, middleware pipeline and endpoints are exactly what production
/// runs, so these tests exercise the real SQL that EF generates.
/// </summary>
public sealed class ApiFactory(
    string connectionString,
    int authPermitPerMinute = 10_000,
    bool trustForwardedHeaders = false,
    string? logDirectory = null,
    bool enableOpenApi = false) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");

        builder.UseSetting("ConnectionStrings:Postgres", connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "false");

        // Seeding runs during host startup, which happens before the test can apply
        // migrations — it would hit tables that do not exist yet. Tests call
        // SeedAdminAsync() explicitly once the schema is in place.
        builder.UseSetting("Admin:SeedOnStartup", "false");
        builder.UseSetting("Jwt:Issuer", "deboetapi-api-tests");
        builder.UseSetting("Jwt:Audience", "deboetapi-clients-tests");
        builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-at-least-32-bytes-long");

        // The production limit of 10/min would throttle the suite itself, so it is raised by
        // default. RateLimitTests passes a low limit to pin the throttling behaviour.
        builder.UseSetting("RateLimit:AuthPermitPerMinute", authPermitPerMinute.ToString());

        // Same reasoning, and more pressing: these windows are global rather than per-user, so a
        // handful of export or import tests running together would throttle each other.
        builder.UseSetting("RateLimit:ExportPermitPerMinute", "10000");
        builder.UseSetting("RateLimit:ImportPermitPerMinute", "10000");
        builder.UseSetting("Proxy:TrustForwardedHeaders", trustForwardedHeaders.ToString());
        builder.UseSetting("OpenApi:Enabled", enableOpenApi.ToString());

        // Off by default so a test run does not scatter log files; LoggingTests passes a
        // temp directory to exercise the real sink.
        builder.UseSetting("FileLogging:Enabled", (logDirectory is not null).ToString());
        if (logDirectory is not null)
        {
            builder.UseSetting("FileLogging:Directory", logDirectory);
        }

        // HSTS deliberately skips loopback hosts so local development is never pinned to
        // HTTPS. TestServer always serves "localhost", so that exemption has to go or the
        // header could never be observed.
        builder.ConfigureServices(services =>
            services.Configure<HstsOptions>(options => options.ExcludedHosts.Clear()));
    }

    /// <summary>Applies migrations, so the suite validates the migration scripts themselves.</summary>
    public async Task MigrateAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    /// <summary>
    /// Runs the real seeder with the real defaults, so the tests exercise the same code path
    /// that creates the admin account on a production boot.
    /// </summary>
    public async Task SeedAdminAsync(AdminSeedOptions? options = null)
    {
        using var scope = Services.CreateScope();
        await AdminSeeder.SeedAsync(
            scope.ServiceProvider,
            options ?? new AdminSeedOptions(),
            Services.GetRequiredService<IHostEnvironment>(),
            NullLogger.Instance);
    }
}
