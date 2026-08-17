using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DeBoetAPI.Data;
using DeBoetAPI.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DeBoetAPI.Features.Auth;

/// <summary>
/// Token service for issuing and rotating JWT access tokens and refresh tokens. 
/// </summary>
/// <param name="db">The application database context.</param>
/// <param name="userManager">The user manager.</param>
/// <param name="options">The JWT options.</param>
/// <param name="logger">The logger.</param>
public sealed class TokenService(
    AppDbContext db,
    UserManager<AppUser> userManager,
    IOptions<JwtOptions> options,
    ILogger<TokenService> logger)
{
    private readonly JwtOptions _options = options.Value;

    public async Task<TokenPairResponse> IssuePairAsync(AppUser user, CancellationToken ct)
    {
        var (accessToken, accessExpiresAt) = await CreateAccessTokenAsync(user);
        var (refreshToken, entity) = CreateRefreshToken(user.Id);

        db.RefreshTokens.Add(entity);
        await db.SaveChangesAsync(ct);

        return new TokenPairResponse(accessToken, accessExpiresAt, refreshToken, entity.ExpiresAt);
    }

    /// <summary>
    /// Rotates a refresh token: if valid, returns a new access token and a new refresh token, and revokes the old refresh token. If the refresh token is invalid, expired, or already revoked, returns null.
    /// </summary>
    /// <param name="refreshToken">The refresh token to rotate.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The new token pair, or null if the refresh token is invalid.</returns>
    public async Task<TokenPairResponse?> RotateAsync(string refreshToken, CancellationToken ct)
    {
        var hash = HashToken(refreshToken);

        var existing = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (existing is null)
        {
            return null;
        }

        if (existing.RevokedAt is not null)
        {
            logger.LogWarning("Refresh token reuse detected for user {UserId}; revoking chain", existing.UserId);
            await RevokeAllForUserAsync(existing.UserId, ct);
            return null;
        }

        if (DateTimeOffset.UtcNow >= existing.ExpiresAt)
        {
            return null;
        }

        var (newToken, newEntity) = CreateRefreshToken(existing.UserId);
        var (accessToken, accessExpiresAt) = await CreateAccessTokenAsync(existing.User);

        existing.RevokedAt = DateTimeOffset.UtcNow;
        existing.ReplacedByTokenId = newEntity.Id;
        db.RefreshTokens.Add(newEntity);
        await db.SaveChangesAsync(ct);

        return new TokenPairResponse(accessToken, accessExpiresAt, newToken, newEntity.ExpiresAt);
    }

    /// <summary>
    /// Revokes a refresh token. Returns true if the token was found and revoked, false if it was not found or already revoked.
    /// </summary>
    /// <param name="refreshToken">The refresh token to revoke.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>True if the token was found and revoked, false if it was not found or already revoked.</returns>
    public async Task<bool> RevokeAsync(string refreshToken, CancellationToken ct)
    {
        var hash = HashToken(refreshToken);
        var existing = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (existing is null || existing.RevokedAt is not null)
        {
            return false;
        }

        existing.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Revokes all refresh tokens for a user. This is used when a refresh token is reused, to prevent further abuse of the token chain.
    /// </summary>
    /// <param name="userId">The ID of the user for whom to revoke tokens.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    /// <summary>
    /// Creates a new access token for the given user, with claims including the user's ID, email, display name, and roles. Returns the token string and its expiration time.
    /// </summary>
    /// <param name="user">The user for whom to create an access token.</param>
    /// <returns>A tuple containing the access token string and its expiration time.</returns>
    private async Task<(string Token, DateTimeOffset ExpiresAt)> CreateAccessTokenAsync(AppUser user)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            new("display_name", user.DisplayName),
        };

        claims.AddRange((await userManager.GetRolesAsync(user))
            .Select(r => new Claim(AuthClaims.Role, r)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    /// <summary>
    /// Creates a new refresh token for the given user ID. Returns the raw token string and the corresponding RefreshToken entity to be stored in the database.
    /// </summary>
    /// <param name="userId">The ID of the user for whom to create a refresh token.</param>
    /// <returns>A tuple containing the raw token string and the RefreshToken entity.</returns>
    private (string Token, RefreshToken Entity) CreateRefreshToken(Guid userId)
    {
        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

        var entity = new RefreshToken
        {
            UserId = userId,
            TokenHash = HashToken(raw),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_options.RefreshTokenDays),
        };

        return (raw, entity);
    }

    /// <summary>
    /// Hashes a refresh token using SHA-256 and returns the lowercase hexadecimal string representation of the hash. This is used to securely store refresh tokens in the database without keeping the raw token.
    /// </summary>
    /// <param name="token">The refresh token to hash.</param>
    /// <returns>The hashed token as a lowercase hexadecimal string.</returns>
    private static string HashToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
