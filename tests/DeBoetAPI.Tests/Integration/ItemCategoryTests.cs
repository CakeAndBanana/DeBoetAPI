using System.Net;
using System.Net.Http.Json;
using DeBoetAPI.Features.Items;

namespace DeBoetAPI.Tests.Integration;

/// <summary>Assigning items to the shared taxonomy, and reading items back out of it.</summary>
public class ItemCategoryTests(PostgresFixture postgres) : CategoryTestBase(postgres)
{
    [Fact]
    public async Task Item_CanBeAssignedToSeveralCategoriesWithOnePrimary()
    {
        var (_, lights, ice) = await CreateChristmasChainAsync();
        var item = await CreateItemAsync("Icicle string");

        var response = await User.PutAsJsonAsync($"/api/v1/items/{item.Id}/categories", new
        {
            categoryIds = new[] { lights.Id, ice.Id },
            primaryCategoryId = ice.Id,
        });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var assigned = await User.GetFromJsonAsync<List<ItemCategoryResponse>>(
            $"/api/v1/items/{item.Id}/categories");

        Assert.NotNull(assigned);
        Assert.Equal(2, assigned.Count);
        var primary = Assert.Single(assigned, c => c.IsPrimary);
        Assert.Equal(ice.Id, primary.CategoryId);
    }

    [Fact]
    public async Task Assignment_ReplacesThePreviousSetRatherThanAppending()
    {
        var (christmas, lights, ice) = await CreateChristmasChainAsync();
        var item = await CreateItemAsync("Reassigned");

        await User.PutAsJsonAsync($"/api/v1/items/{item.Id}/categories",
            new { categoryIds = new[] { christmas.Id, lights.Id }, primaryCategoryId = christmas.Id });

        // A second call with a different primary must not trip the one-primary-per-item index.
        var response = await User.PutAsJsonAsync($"/api/v1/items/{item.Id}/categories",
            new { categoryIds = new[] { ice.Id }, primaryCategoryId = ice.Id });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var assigned = await User.GetFromJsonAsync<List<ItemCategoryResponse>>(
            $"/api/v1/items/{item.Id}/categories");

        Assert.NotNull(assigned);
        var only = Assert.Single(assigned);
        Assert.Equal(ice.Id, only.CategoryId);
        Assert.True(only.IsPrimary);
    }

    [Fact]
    public async Task Assignment_RejectsAPrimaryOutsideTheAssignedSet()
    {
        var (christmas, _, ice) = await CreateChristmasChainAsync();
        var item = await CreateItemAsync("Bad primary");

        var response = await User.PutAsJsonAsync($"/api/v1/items/{item.Id}/categories",
            new { categoryIds = new[] { christmas.Id }, primaryCategoryId = ice.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Assignment_RejectsUnknownCategories()
    {
        var item = await CreateItemAsync("Unknown category");

        var response = await User.PutAsJsonAsync($"/api/v1/items/{item.Id}/categories",
            new { categoryIds = new[] { Guid.CreateVersion7() } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Assignment_IsOwnerScoped()
    {
        var christmas = await CreateCategoryAsync("Owned", slug: UniqueSlug("owned"));
        var item = await CreateItemAsync("Someone else's");

        // Admin is a different user, so the item is invisible to them.
        var response = await Admin.PutAsJsonAsync($"/api/v1/items/{item.Id}/categories",
            new { categoryIds = new[] { christmas.Id } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CategoryItems_IncludeDescendantsOnlyWhenAsked()
    {
        var (christmas, _, ice) = await CreateChristmasChainAsync();
        var item = await CreateItemAsync("Deep item");

        await User.PutAsJsonAsync($"/api/v1/items/{item.Id}/categories",
            new { categoryIds = new[] { ice.Id }, primaryCategoryId = ice.Id });

        var direct = await Admin.GetFromJsonAsync<PagedItems>($"/api/v1/categories/{christmas.Id}/items");
        Assert.NotNull(direct);
        Assert.DoesNotContain(direct.Items, i => i.Id == item.Id);

        var withDescendants = await Admin.GetFromJsonAsync<PagedItems>(
            $"/api/v1/categories/{christmas.Id}/items?includeDescendants=true");
        Assert.NotNull(withDescendants);
        Assert.Contains(withDescendants.Items, i => i.Id == item.Id);
    }

    [Fact]
    public async Task CategoryItems_DoNotDuplicateWhenAnItemIsInSeveralDescendants()
    {
        var (christmas, lights, ice) = await CreateChristmasChainAsync();
        var item = await CreateItemAsync("In two places");

        await User.PutAsJsonAsync($"/api/v1/items/{item.Id}/categories",
            new { categoryIds = new[] { lights.Id, ice.Id }, primaryCategoryId = ice.Id });

        var results = await Admin.GetFromJsonAsync<PagedItems>(
            $"/api/v1/categories/{christmas.Id}/items?includeDescendants=true");

        Assert.NotNull(results);
        Assert.Single(results.Items, i => i.Id == item.Id);
    }

    [Fact]
    public async Task DeletingACategory_UnassignsItWithoutTouchingTheItem()
    {
        var category = await CreateCategoryAsync("Doomed", slug: UniqueSlug("doomed"));
        var item = await CreateItemAsync("Survivor");

        await User.PutAsJsonAsync($"/api/v1/items/{item.Id}/categories",
            new { categoryIds = new[] { category.Id }, primaryCategoryId = category.Id });

        Assert.Equal(HttpStatusCode.OK, (await Admin.DeleteAsync($"/api/v1/categories/{category.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await User.GetAsync($"/api/v1/items/{item.Id}")).StatusCode);
        var assigned = await User.GetFromJsonAsync<List<ItemCategoryResponse>>(
            $"/api/v1/items/{item.Id}/categories");
        Assert.NotNull(assigned);
        Assert.Empty(assigned);
    }

    private async Task<ItemResponse> CreateItemAsync(string name)
    {
        var response = await User.PostAsJsonAsync("/api/v1/items", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ItemResponse>())!;
    }

    private sealed record PagedItems(List<ItemResponse> Items, int Page, int PageSize, int TotalCount);
}
