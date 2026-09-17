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
    public async Task NeitherMetaFieldFallsBackToTheName()
    {
        // Repeating the name in a meta title tells a search engine nothing the page does not
        // already say, and a one-word meta description is worse than none — it writes a better
        // snippet itself. Blank is stored as blank so the panel can show the field as unset.
        var response = await Admin.PostAsJsonAsync("/api/v1/categories",
            new { name = "Ice Lights", slug = UniqueSlug("icelights") });
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;

        Assert.Null(created.MetaTitle);
        Assert.Null(created.MetaDescription);
    }

    [Fact]
    public async Task AHandWrittenSlugKeepsItsHyphens()
    {
        // Derived slugs strip separators, but a route that already exists elsewhere may be
        // spelled with a hyphen, so an explicit slug is kept as typed.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var response = await Admin.PostAsJsonAsync("/api/v1/categories",
            new { name = "Ice Lights", slug = $"ice-lights-{suffix}" });
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;

        Assert.Equal($"ice-lights-{suffix}", created.Slug);
        Assert.Equal($"ice-lights-{suffix}", created.PathSlug);
    }

    [Fact]
    public async Task AHandWrittenSlugIsTidiedRatherThanRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var response = await Admin.PostAsJsonAsync("/api/v1/categories",
            new { name = "Messy", slug = $"  Ice -- Lights! {suffix} " });
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;

        Assert.Equal($"ice-lights-{suffix}", created.Slug);
    }

    [Fact]
    public async Task ANameStillDerivesASlugWithoutSpaces()
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/categories",
            new { name = $"Christmas Lights {Guid.NewGuid():N}" });
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;

        Assert.StartsWith("christmas-lights-", created.Slug);
        Assert.DoesNotContain(' ', created.Slug);
    }

    [Fact]
    public async Task EditingTheSlugRewritesTheRouteOfTheWholeSubtree()
    {
        var (_, lights, ice) = await CreateChristmasChainAsync();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var response = await Admin.PutAsJsonAsync($"/api/v1/categories/{lights.Id}", new
        {
            name = lights.Name,
            slug = $"xmas-lights-{suffix}",
            sortOrder = 0,
            isActive = true,
            isMenu = true,
        });
        response.EnsureSuccessStatusCode();
        var updated = (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;
        Assert.EndsWith($"xmas-lights-{suffix}", updated.PathSlug);

        var child = await Admin.GetFromJsonAsync<CategoryDetailResponse>($"/api/v1/categories/{ice.Id}");
        Assert.NotNull(child);
        Assert.Contains($"xmas-lights-{suffix}-", child.Category.PathSlug);
    }

    [Fact]
    public async Task AMetaTitleOfWhitespaceIsStoredAsNull()
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/categories",
            new { name = "Blank Meta", slug = UniqueSlug("blankmeta"), metaTitle = "   " });
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;

        Assert.Null(created.MetaTitle);
    }

    [Fact]
    public async Task Update_CanClearAMetaTitleThatWasSet()
    {
        var created = await CreateCategoryAsync("Christmas", slug: UniqueSlug("christmas"));

        var withTitle = await Admin.PutAsJsonAsync($"/api/v1/categories/{created.Id}", new
        {
            name = "Christmas",
            metaTitle = "Christmas at De Boet",
            sortOrder = 0,
            isActive = true,
            isMenu = true,
        });
        withTitle.EnsureSuccessStatusCode();
        Assert.Equal("Christmas at De Boet",
            (await withTitle.Content.ReadFromJsonAsync<CategoryResponse>())!.MetaTitle);

        // Sending it back empty clears it rather than reinstating the name.
        var response = await Admin.PutAsJsonAsync($"/api/v1/categories/{created.Id}",
            new { name = "Christmas", metaTitle = "", sortOrder = 0, isActive = true, isMenu = true });
        response.EnsureSuccessStatusCode();
        var updated = (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;

        Assert.Null(updated.MetaTitle);
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
