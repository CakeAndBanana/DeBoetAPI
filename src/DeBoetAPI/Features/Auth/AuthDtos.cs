using System.ComponentModel.DataAnnotations;

namespace DeBoetAPI.Features.Auth;

/// <summary>
/// Register request DTO used for creating new users in the system.
/// The Email could also be interpreted as a username.
/// </summary>
public sealed record RegisterRequest
{
    [Required, EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required, MinLength(8), MaxLength(128)]
    public string Password { get; init; } = string.Empty;

    [Required, MaxLength(100)]
    public string DisplayName { get; init; } = string.Empty;
}

/// <summary>
/// Login request DTO used for authentication of users.
/// The email could also be a username.
/// </summary>
public sealed record LoginRequest
{
    [Required, MaxLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;
}

/// <summary>
/// Refresh request DTO used for refreshing access tokens using a valid refresh token.
/// </summary>
public sealed record RefreshRequest
{
    [Required]
    public string RefreshToken { get; init; } = string.Empty;
}

/// <summary>
/// Token pair response DTO used for returning access and refresh tokens to the client after successful authentication or token refresh.
/// </summary>
/// <param name="AccessToken">The access token.</param>
/// <param name="AccessTokenExpiresAt">The expiration date and time of the access token.</param>
/// <param name="RefreshToken">The refresh token.</param>
/// <param name="RefreshTokenExpiresAt">The expiration date and time of the refresh token.</param>
public sealed record TokenPairResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

/// <summary>
/// User response DTO used for returning user information to the client after successful authentication or user retrieval.
/// </summary>
/// <param name="Id">The unique identifier of the user.</param>
/// <param name="Email">The email address of the user.</param>
/// <param name="UserName">The username of the user.</param>
/// <param name="DisplayName">The display name of the user.</param>
/// <param name="Roles">The roles assigned to the user.</param>
/// <param name="MustChangePassword">Indicates whether the user must change their password.</param>
/// <param name="CreatedAt">The date and time when the user was created.</param>
public sealed record UserResponse(
    Guid Id,
    string Email,
    string UserName,
    string DisplayName,
    IReadOnlyList<string> Roles,
    bool MustChangePassword,
    DateTimeOffset CreatedAt);

/// <summary>
/// Change password request DTO used for changing the user's password.
/// </summary>
public sealed record ChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; init; } = string.Empty;

    [Required, MinLength(8), MaxLength(128)]
    public string NewPassword { get; init; } = string.Empty;
}
