using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace DeBoetAPI.Tests.Integration;

internal static class ForgedTokens
{
    public static string WithSigningKey(string signingKey)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));

        var token = new JwtSecurityToken(
            issuer: "deboetapi-api-tests",
            audience: "deboetapi-clients-tests",
            claims: [new Claim(JwtRegisteredClaimNames.Sub, Guid.CreateVersion7().ToString())],
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
