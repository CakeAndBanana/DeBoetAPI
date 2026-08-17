namespace DeBoetAPI.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class ForwardedHeadersTests(PostgresFixture postgres)
{
    private const string HstsHeader = "Strict-Transport-Security";

    [Fact]
    public async Task ForwardedProto_IsHonouredWhenTheProxyIsTrusted()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, trustForwardedHeaders: true);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(ProxiedRequest());

        Assert.True(response.Headers.Contains(HstsHeader),
            "Request should have been treated as HTTPS once the proxy header was trusted.");
    }

    [Fact]
    public async Task PlainRequest_IsNotTreatedAsSecure()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, trustForwardedHeaders: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.False(response.Headers.Contains(HstsHeader));
    }

    [Fact]
    public async Task ForwardedProto_IsIgnoredWhenTheProxyIsNotTrusted()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, trustForwardedHeaders: false);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(ProxiedRequest());

        Assert.False(response.Headers.Contains(HstsHeader),
            "A spoofed X-Forwarded-Proto must not be honoured when no proxy is trusted.");
    }

    /// <summary>Mirrors what Caddy actually sends — both headers, not just the scheme.</summary>
    private static HttpRequestMessage ProxiedRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");
        return request;
    }
}
