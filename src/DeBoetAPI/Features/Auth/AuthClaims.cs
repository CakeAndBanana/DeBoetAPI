namespace DeBoetAPI.Features.Auth;

public static class AuthClaims
{
    /// <summary>
    /// Claim type carrying role names. Written by TokenService and read back via
    /// TokenValidationParameters.RoleClaimType — the two must stay in step or role checks
    /// silently pass nobody.
    /// </summary>
    public const string Role = "role";
}

public static class AuthPolicies
{
    public const string Admin = "Admin";

    /// <summary>Role name for taxonomy administrators. Seeded at startup.</summary>
    public const string AdminRole = "Admin";
}
