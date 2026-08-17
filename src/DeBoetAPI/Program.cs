using System.Text;
using DeBoetAPI.Common;
using DeBoetAPI.Data;
using DeBoetAPI.Data.Entities;
using DeBoetAPI.Features.Auth;
using DeBoetAPI.Features.Categories;
using DeBoetAPI.Features.Items;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// Console plus a log file that starts a new one each calendar day.
builder.AddAppLogging();

builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(
    connectionString,
    npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history")));

builder.Services.AddIdentityCore<AppUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>();

builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<CategoryTreeService>();

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt section is not configured.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(30),

            RoleClaimType = AuthClaims.Role,
            NameClaimType = JwtRegisteredClaimNames.Sub,
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthPolicies.Admin, policy => policy.RequireRole(AuthPolicies.AdminRole));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddValidation();
builder.Services.AddAppOpenApi();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (allowedOrigins.Length > 0)
    {
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    }
}));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter(RateLimitPolicies.Auth, limiter =>
    {
        limiter.PermitLimit = builder.Configuration.GetValue("RateLimit:AuthPermitPerMinute", 10);
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
});

builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres", tags: ["ready"]);

var trustForwardedHeaders = builder.Configuration.GetValue("Proxy:TrustForwardedHeaders", false);
if (trustForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;

        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

var app = builder.Build();

if (builder.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

var adminSeed = builder.Configuration.GetSection(AdminSeedOptions.SectionName).Get<AdminSeedOptions>()
                ?? new AdminSeedOptions();

if (adminSeed.SeedOnStartup)
{
    using var scope = app.Services.CreateScope();
    await AdminSeeder.SeedAsync(
        scope.ServiceProvider,
        adminSeed,
        app.Environment,
        app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("AdminSeeder"));
}

if (trustForwardedHeaders)
{
    app.UseForwardedHeaders();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseSerilogRequestLogging(options =>
{
    options.GetLevel = (http, _, ex) =>
        ex is not null || http.Response.StatusCode >= 500 ? LogEventLevel.Error
        : http.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose
        : LogEventLevel.Information;
});

app.UseExceptionHandler();
app.UseRateLimiter();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Docs are opt-in rather than tied to the environment, so they can be turned on in Production
// without also enabling developer exception pages and the rest of Development's behaviour.
// Caddy does not proxy these paths, so on the VPS they are reachable only over the loopback
// binding — i.e. through an SSH tunnel. See the Caddyfile before exposing them publicly.
if (builder.Configuration.GetValue("OpenApi:Enabled", app.Environment.IsDevelopment()))
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapHealthChecks("/health", new()
{
    Predicate = _ => false,
});

app.MapHealthChecks("/health/ready", new()
{
    Predicate = check => check.Tags.Contains("ready"),
});

app.MapAuthEndpoints();
app.MapItemEndpoints();
app.MapCategoryEndpoints();

app.Run();