using Microsoft.AspNetCore.Identity;

namespace DeBoetAPI.Data.Entities;

public class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// True while the account is on a password an administrator set. Cleared only when the
    /// user changes it themselves. Enforced by the authorization policies, not merely by the
    /// admin panel — otherwise the temporary password would grant full API access.
    /// </summary>
    public bool MustChangePassword { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}
