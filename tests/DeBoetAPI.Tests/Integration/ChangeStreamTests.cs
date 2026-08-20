using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DeBoetAPI.Features.Auth;

namespace DeBoetAPI.Tests.Integration;

/// <summary>
/// The live change feed, and the rule about who is entitled to hear what: the taxonomy is
/// shared work and goes to everyone, an account's own events go only to that account.
/// </summary>
public class ChangeStreamTests(PostgresFixture postgres) : CategoryTestBase(postgres)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task TheFeed_IsClosedToAnonymousCallers()
    {
        var response = await Anonymous.GetAsync("/api/v1/events");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ATaxonomyChange_ReachesSomeoneWhoDidNotMakeIt()
    {
        var (editor, _, _, _) = await CreateUserAndSignInAsync(AuthPolicies.EditorRole);

        var seen = await WatchAsync(Admin, async () =>
        {
            var created = await editor.PostAsJsonAsync("/api/v1/categories",
                new { name = "Seen By Everyone", slug = UniqueSlug("seenbyeveryone") });
            created.EnsureSuccessStatusCode();
        });

        Assert.Contains(seen, e => e.EntityType == "Category" && e.Action == "Created");
    }

    [Fact]
    public async Task AnAccountChange_ReachesTheAccountItHappenedTo()
    {
        var (editor, editorId, _, _) = await CreateUserAndSignInAsync(AuthPolicies.EditorRole);

        var seen = await WatchAsync(editor, async () =>
        {
            var reset = await Admin.PostAsJsonAsync($"/api/v1/users/{editorId}/reset-password",
                new { newPassword = "Reset!Pass789" });
            reset.EnsureSuccessStatusCode();
        });

        Assert.Contains(seen, e => e.EntityType == "User" && e.Action == "PasswordReset");
    }

    [Fact]
    public async Task AnAccountChange_ReachesNobodyElse()
    {
        var (_, editorId, _, _) = await CreateUserAndSignInAsync(AuthPolicies.EditorRole);
        var (other, _, _, _) = await CreateUserAndSignInAsync(AuthPolicies.EditorRole);

        var seen = await WatchAsync(other, async () =>
        {
            var reset = await Admin.PostAsJsonAsync($"/api/v1/users/{editorId}/reset-password",
                new { newPassword = "Reset!Pass789" });
            reset.EnsureSuccessStatusCode();

            var created = await Admin.PostAsJsonAsync("/api/v1/categories",
                new { name = "Marker", slug = UniqueSlug("marker") });
            created.EnsureSuccessStatusCode();
        });

        Assert.Contains(seen, e => e.EntityType == "Category");
        Assert.DoesNotContain(seen, e => e.EntityType == "User");
    }

    private static async Task<List<Change>> WatchAsync(HttpClient client, Func<Task> act)
    {
        using var cancellation = new CancellationTokenSource(Patience);
        var ct = cancellation.Token;

        using var response = await client.GetAsync(
            "/api/v1/events", HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        Assert.Equal(": connected", await reader.ReadLineAsync(ct));

        await act();

        var changes = new List<Change>();

        while (true)
        {
            var line = await ReadLineOrNullAsync(reader, changes.Count == 0 ? Patience : TimeSpan.FromSeconds(2), ct);
            if (line is null) return changes;

            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                changes.Add(JsonSerializer.Deserialize<Change>(line[6..],
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
            }
        }
    }

    /// <summary>A line, or null once nothing more is coming within <paramref name="within"/>.</summary>
    private static async Task<string?> ReadLineOrNullAsync(
        StreamReader reader, TimeSpan within, CancellationToken ct)
    {
        var line = reader.ReadLineAsync(ct).AsTask();

        return await Task.WhenAny(line, Task.Delay(within, CancellationToken.None)) == line
            ? await line
            : null;
    }

    private sealed record Change(string EntityType, Guid EntityId, string Action);
}
