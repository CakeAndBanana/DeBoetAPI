using System.ComponentModel.DataAnnotations;

namespace DeBoetAPI.Features.Users;

/// <summary>
/// Creates a new user account with the specified username, email, display name, password, and roles. 
/// The account will be created with a flag indicating that the user must change their password at first login. 
/// If no roles are specified, the account will have no additional rights beyond signing in.
/// </summary>
public sealed record CreateUserRequest
{
    [Required, MaxLength(256)]
    public string UserName { get; init; } = string.Empty;

    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required, MaxLength(100)]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>The account starts on this password and must change it at first login.</summary>

    [Required, MinLength(8), MaxLength(128)]
    public string Password { get; init; } = string.Empty;
    /// <summary>Empty grants no rights at all - the account can sign in and nothing more.</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];
}

/// <summary>
/// Dto for the setting of roles on a user.
/// </summary>
public sealed record SetRolesRequest
{
    [Required]
    public IReadOnlyList<string> Roles { get; init; } = [];
}

/// <summary>
/// Dto for resetting a user's password.
/// </summary>
public sealed record ResetPasswordRequest
{
    [Required, MinLength(8), MaxLength(128)]
    public string NewPassword { get; init; } = string.Empty;
}

/// <summary>
/// Managed user response DTO used for returning detailed user information to the client, including roles and account status.
/// </summary>
/// <param name="Id">The unique identifier for the user.</param>
/// <param name="UserName">The username of the user.</param>
/// <param name="Email">The email address of the user.</param>
/// <param name="DisplayName">The display name of the user.</param>
/// <param name="Roles">The roles assigned to the user.</param>
/// <param name="MustChangePassword">Indicates whether the user must change their password at first login.</param>
/// <param name="IsDisabled">Indicates whether the user account is disabled.</param>
/// <param name="CreatedAt">The date and time when the user account was created.</param>
public sealed record ManagedUserResponse(
    Guid Id,
    string UserName,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles,
    bool MustChangePassword,
    bool IsDisabled,
    DateTimeOffset CreatedAt);
