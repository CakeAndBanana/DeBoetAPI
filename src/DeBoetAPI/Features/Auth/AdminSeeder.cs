using DeBoetAPI.Data.Entities;
using Microsoft.AspNetCore.Identity;

namespace DeBoetAPI.Features.Auth;

public sealed class AdminSeedOptions
{
    public const string SectionName = "Admin";

    public bool SeedOnStartup { get; set; } = true;

    public string Username { get; set; } = DefaultUsername;

    /// <summary>Identity requires a unique email, so the seed account gets a placeholder one.</summary>
    public string Email { get; set; } = DefaultEmail;

    public string Password { get; set; } = DefaultPassword;

    public const string DefaultUsername = "admin";

    public const string DefaultEmail = "admin@deboetapi.local";

    public const string DefaultPassword = "deboet";
}

/// <summary>
/// Ensures the Admin role and a bootstrap administrator exist.
/// </summary>
public static class AdminSeeder
{
    public static async Task SeedAsync(
        IServiceProvider services,
        AdminSeedOptions options,
        IHostEnvironment environment,
        ILogger logger)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        if (!await roleManager.RoleExistsAsync(AuthPolicies.AdminRole))
        {
            await roleManager.CreateAsync(new IdentityRole<Guid>(AuthPolicies.AdminRole)
            {
                Id = Guid.CreateVersion7(),
            });
            logger.LogInformation("Created {Role} role", AuthPolicies.AdminRole);
        }

        if (!environment.IsDevelopment() && options.Password == AdminSeedOptions.DefaultPassword)
        {
            logger.LogWarning(
                "The seeded {User} account is using the built-in default password. Set Admin__Password " +
                "(or disable Admin__SeedOnStartup) before this is reachable from the internet.",
                options.Username);
        }

        var userManager = services.GetRequiredService<UserManager<AppUser>>();

        var user = await userManager.FindByNameAsync(options.Username);
        if (user is not null)
        {
            await EnsureInAdminRoleAsync(userManager, user, logger);
            return;
        }

        user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            UserName = options.Username,
            Email = options.Email,
            EmailConfirmed = true,
            DisplayName = "Administrator",
        };

        user.PasswordHash = userManager.PasswordHasher.HashPassword(user, options.Password);

        var created = await userManager.CreateAsync(user);
        if (!created.Succeeded)
        {
            logger.LogError("Could not seed the {User} account: {Errors}",
                options.Username, string.Join("; ", created.Errors.Select(e => e.Description)));
            return;
        }

        await EnsureInAdminRoleAsync(userManager, user, logger);
        logger.LogInformation("Seeded administrator {User}", options.Username);
    }

    private static async Task EnsureInAdminRoleAsync(UserManager<AppUser> userManager, AppUser user, ILogger logger)
    {
        if (!await userManager.IsInRoleAsync(user, AuthPolicies.AdminRole))
        {
            await userManager.AddToRoleAsync(user, AuthPolicies.AdminRole);
            logger.LogInformation("Granted {Role} to {User}", AuthPolicies.AdminRole, user.UserName);
        }
    }
}
