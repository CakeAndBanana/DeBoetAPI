namespace DeBoetAPI.Features.Auth;

public static class AuthClaims
{
    public const string Role = "role";

    public const string MustChangePassword = "must_change_password";
}

public static class AuthPolicies
{
    public const string Admin = "Admin";

    public const string Editor = "Editor";

    public const string AdminRole = "Admin";

    public const string EditorRole = "Editor";

    public static readonly string[] AssignableRoles = [AdminRole, EditorRole];
}
