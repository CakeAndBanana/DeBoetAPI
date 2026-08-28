using DeBoetAPI.Data.Entities;
using Microsoft.AspNetCore.Identity;

namespace DeBoetAPI.Features.Auth;

/// <summary>
/// Default options of the administrator account to seed on startup.
/// </summary>
public sealed class AdminSeedOptions
{
    public const string SectionName = "Admin";

    public bool SeedOnStartup { get; set; } = true;

    public string Username { get; set; } = DefaultUsername;

    public string Email { get; set; } = DefaultEmail;

    public string Password { get; set; } = DefaultPassword;

    public const string DefaultUsername = "admin";

    public const string DefaultEmail = "admin@deboetapi.local";

    public const string DefaultPassword = "deboet";
}

/// <summary>
/// AdminSeeder is responsible for seeding an initial administrator account and roles into the system.
/// This is only really used on the initial setup of the application.
/// </summary>
public static class AdminSeeder
{
    /// <summary>
    /// Seeds the administrator account and roles into the system if they do not already exist.
    /// </summary>
    /// <param name="services">The service provider.</param>
    /// <param name="options">The administrator seed options.</param>
    /// <param name="environment">The host environment.</param>
    /// <param name="logger">The logger.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task SeedAsync(
        IServiceProvider services,
        AdminSeedOptions options,
        IHostEnvironment environment,
        ILogger logger)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        // Ensure that all assignable roles exist in the system
        foreach (var role in AuthPolicies.AssignableRoles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(role) { Id = Guid.CreateVersion7() });
                logger.LogInformation("Created {Role} role", role);
            }
        }

        if (!environment.IsDevelopment() && options.Password == AdminSeedOptions.DefaultPassword)
        {
            logger.LogWarning(
                "The seeded {User} account is using the built-in default password. Set Admin__Password " +
                "(or disable Admin__SeedOnStartup) before this is reachable from the internet.",
                options.Username);
        }

        var userManager = services.GetRequiredService<UserManager<AppUser>>();

        // Check if the user already exists
        var user = await userManager.FindByNameAsync(options.Username);
        if (user is not null)
        {
            await EnsureInAdminRoleAsync(userManager, user, logger);
            return;
        }

        // Creates a new AppUser instance with the default options of the administrator account to seed on startup.
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

        // Ensure that the newly created user is in the Admin role
        await EnsureInAdminRoleAsync(userManager, user, logger);
        logger.LogInformation("Seeded administrator {User}", options.Username);
    }

    /// <summary>
    /// Ensures the specified user is in the Admin role. If not, adds the user to the Admin role.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="user">The user.</param>
    /// <param name="logger">The logger.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task EnsureInAdminRoleAsync(UserManager<AppUser> userManager, AppUser user, ILogger logger)
    {
        if (!await userManager.IsInRoleAsync(user, AuthPolicies.AdminRole))
        {
            await userManager.AddToRoleAsync(user, AuthPolicies.AdminRole);
            logger.LogInformation("Granted {Role} to {User}", AuthPolicies.AdminRole, user.UserName);
        }
    }
}
