using Microsoft.AspNetCore.Identity;

namespace DeBoetAPI.Data.Entities;

/// <summary>
/// AppUser represents a user in the application, extending the IdentityUser class provided by ASP.NET Core Identity.
/// </summary>
public class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;

    public bool MustChangePassword { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}
