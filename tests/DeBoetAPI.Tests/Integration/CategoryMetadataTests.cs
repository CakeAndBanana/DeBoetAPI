using System.Net;
using System.Net.Http.Json;
using DeBoetAPI.Features.Categories;

namespace DeBoetAPI.Tests.Integration;

/// <summary>SEO fields and the navigation flag.</summary>
public class CategoryMetadataTests(PostgresFixture postgres) : CategoryTestBase(postgres)
{
    [Fact]
    public async Task MetaDescription_RoundTrips()
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/categories", new
        {
            name = "Christmas",
            slug = UniqueSlug("christmas"),
            metaTitle = "Christmas Decorations | De Boet",
            metaDescription = "Everything for the festive season, from lights to trees.",
        });
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;

        Assert.Equal("Christmas Decorations | De Boet", created.MetaTitle);
        Assert.Equal("Everything for the festive season, from lights to trees.", created.MetaDescription);
    }

    [Fact]
    public async Task MetaTitle_FallsBackToNameButMetaDescriptionDoesNot()
    {
        // A one-word meta description is worse than none — search engines write a better
        // snippet themselves — so only the title gets a fallback.
        var response = await Admin.PostAsJsonAsync("/api/v1/categories",
            new { name = "Ice Lights", slug = UniqueSlug("icelights") });
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;

        Assert.Equal("Ice Lights", created.MetaTitle);
        Assert.Null(created.MetaDescription);
    }

    [Fact]
    public async Task Update_KeepsTheSameAsymmetry()
    {
        var created = await CreateCategoryAsync("Christmas", slug: UniqueSlug("christmas"));

        var response = await Admin.PutAsJsonAsync($"/api/v1/categories/{created.Id}",
            new { name = "Christmas", sortOrder = 0, isActive = true, isMenu = true });
        response.EnsureSuccessStatusCode();
        var updated = (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;

        Assert.Equal("Christmas", updated.MetaTitle);
        Assert.Null(updated.MetaDescription);
    }

    [Fact]
    public async Task IsMenu_DefaultsToTrue()
    {
        var created = await CreateCategoryAsync("Visible", slug: UniqueSlug("visible"));

        Assert.True(created.IsMenu);
    }

    [Fact]
    public async Task MenuOnly_FiltersTheList()
    {
        var inMenu = UniqueSlug("inmenu");
        var hidden = UniqueSlug("hidden");

        await Admin.PostAsJsonAsync("/api/v1/categories", new { name = "In Menu", slug = inMenu, isMenu = true });
        await Admin.PostAsJsonAsync("/api/v1/categories", new { name = "Hidden", slug = hidden, isMenu = false });

        var all = await Admin.GetFromJsonAsync<PagedCategories>("/api/v1/categories?rootsOnly=true&pageSize=100");
        Assert.NotNull(all);
        Assert.Contains(all.Items, c => c.Slug == inMenu);
        Assert.Contains(all.Items, c => c.Slug == hidden);

        var menu = await Admin.GetFromJsonAsync<PagedCategories>(
            "/api/v1/categories?rootsOnly=true&menuOnly=true&pageSize=100");
        Assert.NotNull(menu);
        Assert.Contains(menu.Items, c => c.Slug == inMenu);
        Assert.DoesNotContain(menu.Items, c => c.Slug == hidden);
    }

    [Fact]
    public async Task MenuOnly_FiltersTheTree()
    {
        var (christmas, lights, _) = await CreateChristmasChainAsync();

        var response = await Admin.PutAsJsonAsync($"/api/v1/categories/{lights.Id}",
            new { name = lights.Name, slug = lights.Slug, sortOrder = 0, isActive = true, isMenu = false });
        response.EnsureSuccessStatusCode();

        var full = await Admin.GetFromJsonAsync<List<CategoryNode>>(
            $"/api/v1/categories/tree?rootId={christmas.Id}");
        Assert.NotNull(full);
        Assert.Single(Assert.Single(full).Children);

        var menu = await Admin.GetFromJsonAsync<List<CategoryNode>>(
            $"/api/v1/categories/tree?rootId={christmas.Id}&menuOnly=true");
        Assert.NotNull(menu);
        Assert.Empty(Assert.Single(menu).Children);
    }

    [Fact]
    public async Task IsMenu_IsIndependentOfIsActive()
    {
        // Two separate axes: a category can be live but absent from navigation.
        var slug = UniqueSlug("liveoffmenu");
        var response = await Admin.PostAsJsonAsync("/api/v1/categories",
            new { name = "Live Off Menu", slug, isActive = true, isMenu = false });
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;

        Assert.True(created.IsActive);
        Assert.False(created.IsMenu);

        // Still reachable directly, just not listed in the menu.
        Assert.Equal(HttpStatusCode.OK, (await Admin.GetAsync($"/api/v1/categories/{slug}")).StatusCode);
    }

    private sealed record PagedCategories(List<CategoryResponse> Items, int Page, int PageSize, int TotalCount);
}
