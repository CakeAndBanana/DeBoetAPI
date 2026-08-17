using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using DeBoetAPI.Common;

namespace DeBoetAPI.Tests;

public class ClaimsPrincipalExtensionsTests
{
    [Fact]
    public void GetUserId_ReadsSubClaim()
    {
        var id = Guid.CreateVersion7();
        var principal = PrincipalWith(new Claim(JwtRegisteredClaimNames.Sub, id.ToString()));

        Assert.Equal(id, principal.GetUserId());
    }

    [Fact]
    public void GetUserId_ReadsNameIdentifierClaim()
    {
        var id = Guid.CreateVersion7();
        var principal = PrincipalWith(new Claim(ClaimTypes.NameIdentifier, id.ToString()));

        Assert.Equal(id, principal.GetUserId());
    }

    [Fact]
    public void GetUserId_ThrowsWhenSubjectMissing()
    {
        Assert.Throws<InvalidOperationException>(() => PrincipalWith().GetUserId());
    }

    [Fact]
    public void GetUserId_ThrowsWhenSubjectIsNotAGuid()
    {
        var principal = PrincipalWith(new Claim(JwtRegisteredClaimNames.Sub, "not-a-guid"));

        Assert.Throws<InvalidOperationException>(() => principal.GetUserId());
    }

    private static ClaimsPrincipal PrincipalWith(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "Test"));
}
