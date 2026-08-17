using System.Net;
using System.Net.Http.Json;

namespace DeBoetAPI.Tests.Integration;

public class CategoryAuthorizationTests(PostgresFixture postgres) : CategoryTestBase(postgres)
{
    [Fact]
    public async Task SeededAdmin_CanWrite()
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/categories",
            new { name = $"Admin Made {Guid.NewGuid():N}" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task OrdinaryUser_IsForbiddenFromEveryWriteRoute()
    {
        var existing = await CreateCategoryAsync("Target", slug: UniqueSlug("target"));
        var otherId = Guid.CreateVersion7();

        var attempts = new (string Label, Func<Task<HttpResponseMessage>> Send)[]
        {
            ("create", () => User.PostAsJsonAsync("/api/v1/categories", new { name = "Nope" })),
            ("update", () => User.PutAsJsonAsync($"/api/v1/categories/{existing.Id}", new { name = "Nope" })),
            ("move", () => User.PostAsJsonAsync($"/api/v1/categories/{existing.Id}/move", new { newParentId = (Guid?)null })),
            ("delete", () => User.DeleteAsync($"/api/v1/categories/{existing.Id}")),
            ("add link", () => User.PostAsJsonAsync($"/api/v1/categories/{existing.Id}/links", new { parentId = otherId })),
            ("remove link", () => User.DeleteAsync($"/api/v1/categories/{existing.Id}/links/{otherId}")),
        };

        foreach (var (label, send) in attempts)
        {
            var response = await send();
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
                $"Expected 403 for {label} as a non-admin but got {(int)response.StatusCode}.");
        }

        // And the category is untouched.
        Assert.Equal(HttpStatusCode.OK, (await Admin.GetAsync($"/api/v1/categories/{existing.Id}")).StatusCode);
    }

    [Fact]
    public async Task OrdinaryUser_CanStillRead()
    {
        var created = await CreateCategoryAsync("Readable", slug: UniqueSlug("readable"));

        Assert.Equal(HttpStatusCode.OK, (await User.GetAsync("/api/v1/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await User.GetAsync("/api/v1/categories/tree")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await User.GetAsync($"/api/v1/categories/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task AnonymousCaller_CanBrowseTheCatalog()
    {
        // Reads are deliberately open so a public website can render the catalog without a token.
        var created = await CreateCategoryAsync("Public", slug: UniqueSlug("public"));

        Assert.Equal(HttpStatusCode.OK, (await Anonymous.GetAsync("/api/v1/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Anonymous.GetAsync("/api/v1/categories/tree")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Anonymous.GetAsync($"/api/v1/categories/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task AnonymousCaller_CannotWrite()
    {
        // Open reads must not have opened up the write side with them.
        var existing = await CreateCategoryAsync("Guarded", slug: UniqueSlug("guarded"));

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Anonymous.PostAsJsonAsync("/api/v1/categories", new { name = "Nope" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Anonymous.PutAsJsonAsync($"/api/v1/categories/{existing.Id}", new { name = "Nope" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Anonymous.DeleteAsync($"/api/v1/categories/{existing.Id}")).StatusCode);
    }
}
