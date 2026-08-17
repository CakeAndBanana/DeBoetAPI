using System.Net;
using System.Net.Http.Json;

namespace DeBoetAPI.Tests.Integration;

public class CategoryLinkTests(PostgresFixture postgres) : CategoryTestBase(postgres)
{
    private static readonly string[] expected = ["Christmas", "Christmas Lights"];

    [Fact]
    public async Task SecondaryLink_AddsAParentWithoutChangingTheMainPath()
    {
        var (_, _, ice) = await CreateChristmasChainAsync();
        var outdoor = await CreateCategoryAsync("Outdoor Lighting", slug: UniqueSlug("outdoor"));

        var response = await Admin.PostAsJsonAsync($"/api/v1/categories/{ice.Id}/links",
            new { parentId = outdoor.Id });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var detail = await GetCategoryAsync(ice.Id);

        Assert.Contains(detail.SecondaryParents, p => p.Id == outdoor.Id);

        Assert.Equal(expected, detail.Breadcrumb.Select(c => c.Name).ToArray());
        Assert.Equal(2, detail.Category.Depth);
    }

    [Fact]
    public async Task SecondaryLink_CanBeRemoved()
    {
        var (_, _, ice) = await CreateChristmasChainAsync();
        var outdoor = await CreateCategoryAsync("Outdoor", slug: UniqueSlug("outdoor"));

        await Admin.PostAsJsonAsync($"/api/v1/categories/{ice.Id}/links", new { parentId = outdoor.Id });

        var response = await Admin.DeleteAsync($"/api/v1/categories/{ice.Id}/links/{outdoor.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.Empty((await GetCategoryAsync(ice.Id)).SecondaryParents);
    }

    [Fact]
    public async Task SecondaryLink_RejectsDuplicates()
    {
        var (_, _, ice) = await CreateChristmasChainAsync();
        var outdoor = await CreateCategoryAsync("Outdoor", slug: UniqueSlug("outdoor"));

        await Admin.PostAsJsonAsync($"/api/v1/categories/{ice.Id}/links", new { parentId = outdoor.Id });
        var second = await Admin.PostAsJsonAsync($"/api/v1/categories/{ice.Id}/links", new { parentId = outdoor.Id });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task SecondaryLink_RejectsTheExistingMainParent()
    {
        var (_, lights, ice) = await CreateChristmasChainAsync();

        var response = await Admin.PostAsJsonAsync($"/api/v1/categories/{ice.Id}/links",
            new { parentId = lights.Id });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SecondaryLink_RejectsItsOwnDescendant()
    {
        var (christmas, _, ice) = await CreateChristmasChainAsync();

        var response = await Admin.PostAsJsonAsync($"/api/v1/categories/{christmas.Id}/links",
            new { parentId = ice.Id });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SecondaryLink_RejectsSelf()
    {
        var category = await CreateCategoryAsync("Loop", slug: UniqueSlug("loop"));

        var response = await Admin.PostAsJsonAsync($"/api/v1/categories/{category.Id}/links",
            new { parentId = category.Id });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DeletingACategory_DropsItsLinks()
    {
        var (_, _, ice) = await CreateChristmasChainAsync();
        var outdoor = await CreateCategoryAsync("Outdoor", slug: UniqueSlug("outdoor"));
        await Admin.PostAsJsonAsync($"/api/v1/categories/{ice.Id}/links", new { parentId = outdoor.Id });

        Assert.Equal(HttpStatusCode.OK, (await Admin.DeleteAsync($"/api/v1/categories/{outdoor.Id}")).StatusCode);

        var detail = await GetCategoryAsync(ice.Id);
        Assert.Empty(detail.SecondaryParents);
    }
}
