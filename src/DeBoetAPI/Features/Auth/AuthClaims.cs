namespace DeBoetAPI.Features.Auth;

/// <summary>
/// Authentication claims used in the application.
/// </summary>
public static class AuthClaims
{
    public const string Role = "role";

    public const string MustChangePassword = "must_change_password";
}

/// <summary>
/// Authentication policies used in the application. 
/// These are used to define roles and permissions for the users that are using the application.
/// </summary>
public static class AuthPolicies
{
    public const string Admin = "Admin";

    public const string Editor = "Editor";

    public const string AdminRole = "Admin";

    public const string EditorRole = "Editor";

    public static readonly string[] AssignableRoles = [AdminRole, EditorRole];
}
