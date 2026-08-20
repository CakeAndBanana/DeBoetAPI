namespace DeBoetAPI.Features.Auth;

public static class AuthClaims
{
    /// <summary>
    /// Claim type carrying role names. Written by TokenService and read back via
    /// TokenValidationParameters.RoleClaimType — the two must stay in step or role checks
    /// silently pass nobody.
    /// </summary>
    public const string Role = "role";

    /// <summary>
    /// Present and "true" while the account is on an admin-set password.
    /// <para>
    /// Carried in the token rather than checked against the database on each request, so the
    /// authorization policies can enforce it without a round trip. It is only ever cleared by
    /// the user changing their own password, which also revokes their refresh tokens — so a
    /// stale "true" cannot outlive one access-token lifetime.
    /// </para>
    /// </summary>
    public const string MustChangePassword = "must_change_password";
}

public static class AuthPolicies
{
    public const string Admin = "Admin";

    /// <summary>
    /// Create and edit categories. Satisfied by Editor <em>or</em> Admin — an admin should
    /// never be refused something an editor is allowed to do.
    /// </summary>
    public const string Editor = "Editor";

    /// <summary>Role name for full administrators. Seeded at startup.</summary>
    public const string AdminRole = "Admin";

    /// <summary>Role name for category editors. Seeded at startup.</summary>
    public const string EditorRole = "Editor";

    /// <summary>The roles an admin may assign, for validating incoming requests.</summary>
    public static readonly string[] AssignableRoles = [AdminRole, EditorRole];
}
