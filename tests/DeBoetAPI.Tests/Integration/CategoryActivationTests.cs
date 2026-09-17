using System.Net;
using System.Net.Http.Json;
using DeBoetAPI.Features.Audit;
using DeBoetAPI.Features.Categories;

namespace DeBoetAPI.Tests.Integration;

/// <summary>
/// The rule that a sub category is never active while the category above it is not.
///
/// It is enforced downwards — switching a section off takes its whole subtree with it — and
/// upwards, by refusing to switch one back on underneath something that is still off. Against a
/// real database, because the cascade walks the materialised path the same way the subtree
/// rewrites do.
/// </summary>
public class CategoryActivationTests(PostgresFixture postgres) : CategoryTestBase(postgres)
{
    [Fact]
    public async Task SwitchingASectionOff_SwitchesOffEverythingBeneathIt()
    {
        var (christmas, lights, ice) = await CreateChristmasChainAsync();

        await DeactivateAsync(christmas);

        Assert.False(await IsActiveAsync(lights.Id));
        Assert.False(await IsActiveAsync(ice.Id));
    }

    [Fact]
    public async Task SwitchingASectionOff_LeavesItsSiblingsAlone()
    {
        // The cascade follows the path, so a category that merely sorts next to the one being
        // switched off must not be caught by it.
        var (christmas, lights, _) = await CreateChristmasChainAsync();
        var halloween = await CreateCategoryAsync("Halloween", slug: UniqueSlug("halloween"));

        await DeactivateAsync(christmas);

        Assert.False(await IsActiveAsync(lights.Id));
        Assert.True(await IsActiveAsync(halloween.Id));
    }

    [Fact]
    public async Task SwitchingASectionBackOn_LeavesWhatItSwitchedOffAlone()
    {
        // Deliberate: the state each sub category was in beforehand is not recorded anywhere,
        // so bringing them all back would publish ones that were switched off on purpose.
        var (christmas, lights, ice) = await CreateChristmasChainAsync();
        await DeactivateAsync(christmas);

        var response = await Admin.PutAsJsonAsync($"/api/v1/categories/{christmas.Id}",
            Payload(christmas, isActive: true));
        response.EnsureSuccessStatusCode();

        Assert.True(await IsActiveAsync(christmas.Id));
        Assert.False(await IsActiveAsync(lights.Id));
        Assert.False(await IsActiveAsync(ice.Id));
    }

    [Fact]
    public async Task SwitchingOneOnUnderneathAnInactiveSection_IsRefused()
    {
        var (christmas, lights, _) = await CreateChristmasChainAsync();
        await DeactivateAsync(christmas);

        var response = await Admin.PutAsJsonAsync($"/api/v1/categories/{lights.Id}",
            Payload(lights, isActive: true));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.False(await IsActiveAsync(lights.Id));
    }

    [Fact]
    public async Task SwitchingOneOnIsRefused_EvenWhenTheInactiveSectionIsTwoLevelsUp()
    {
        var (christmas, _, ice) = await CreateChristmasChainAsync();
        await DeactivateAsync(christmas);

        var response = await Admin.PutAsJsonAsync($"/api/v1/categories/{ice.Id}",
            Payload(ice, isActive: true));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task EditingAnInactiveCategory_IsNotRefusedWhileItStaysOff()
    {
        // Only switching one on is refused. Everything else about a category below an inactive
        // section stays editable — its meta description, its place in the order.
        var (christmas, lights, _) = await CreateChristmasChainAsync();
        await DeactivateAsync(christmas);

        var response = await Admin.PutAsJsonAsync($"/api/v1/categories/{lights.Id}",
            Payload(lights, isActive: false, name: "Festive Lighting"));

        response.EnsureSuccessStatusCode();
        Assert.Equal("Festive Lighting", (await GetCategoryAsync(lights.Id)).Category.Name);
    }

    [Fact]
    public async Task CreatingAnActiveCategoryUnderAnInactiveOne_IsRefused()
    {
        var christmas = await CreateCategoryAsync("Christmas", slug: UniqueSlug("christmas"), isActive: false);

        var response = await Admin.PostAsJsonAsync("/api/v1/categories", new
        {
            name = "Christmas Lights",
            slug = UniqueSlug("christmaslights"),
            parentId = christmas.Id,
            isActive = true,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreatingAnInactiveCategoryUnderAnInactiveOne_IsAllowed()
    {
        var christmas = await CreateCategoryAsync("Christmas", slug: UniqueSlug("christmas"), isActive: false);

        var lights = await CreateCategoryAsync(
            "Christmas Lights", christmas.Id, isActive: false, slug: UniqueSlug("christmaslights"));

        Assert.False(lights.IsActive);
    }

    [Fact]
    public async Task MovingABranchUnderAnInactiveSection_SwitchesTheWholeBranchOff()
    {
        var (_, lights, ice) = await CreateChristmasChainAsync();
        var archive = await CreateCategoryAsync("Archive", slug: UniqueSlug("archive"), isActive: false);

        var response = await Admin.PostAsJsonAsync($"/api/v1/categories/{lights.Id}/move",
            new { newParentId = archive.Id });
        response.EnsureSuccessStatusCode();

        Assert.False(await IsActiveAsync(lights.Id));
        Assert.False(await IsActiveAsync(ice.Id));
    }

    [Fact]
    public async Task MovingABranchBackUnderAnActiveSection_LeavesItOff()
    {
        // Moving out is not a decision to publish: the categories stay off until somebody says so.
        var (christmas, lights, _) = await CreateChristmasChainAsync();
        var archive = await CreateCategoryAsync("Archive", slug: UniqueSlug("archive"), isActive: false);

        var away = await Admin.PostAsJsonAsync($"/api/v1/categories/{lights.Id}/move",
            new { newParentId = archive.Id });
        away.EnsureSuccessStatusCode();

        var back = await Admin.PostAsJsonAsync($"/api/v1/categories/{lights.Id}/move",
            new { newParentId = christmas.Id });
        back.EnsureSuccessStatusCode();

        Assert.False(await IsActiveAsync(lights.Id));

        // ...and now that nothing above it is off, it can be switched on again.
        var on = await Admin.PutAsJsonAsync($"/api/v1/categories/{lights.Id}",
            Payload(lights, isActive: true));
        on.EnsureSuccessStatusCode();
        Assert.True(await IsActiveAsync(lights.Id));
    }

    [Fact]
    public async Task EachCategoryTheCascadeTouches_SaysSoInItsOwnHistory()
    {
        // The reason a category went dark has to be findable from the category, not only from
        // the one somebody actually edited.
        var (christmas, _, ice) = await CreateChristmasChainAsync();

        await DeactivateAsync(christmas);

        var history = await Admin.GetFromJsonAsync<PagedAudit>($"/api/v1/categories/{ice.Id}/history");
        var entry = history!.Items.First(e => e.Action == "Updated");

        Assert.Contains("isActive: True -> False", entry.Summary);
        Assert.Contains("Christmas", entry.Summary);
    }

    [Fact]
    public async Task TheTreeEndpoint_NoLongerHasToStrandAnActiveChild()
    {
        // Filtering out an inactive category used to leave its active children behind as roots.
        // With the cascade there is nothing left to strand.
        var (christmas, lights, _) = await CreateChristmasChainAsync();
        await DeactivateAsync(christmas);

        var tree = await Admin.GetFromJsonAsync<List<CategoryNode>>("/api/v1/categories/tree");

        Assert.DoesNotContain(tree!, n => n.Id == christmas.Id || n.Id == lights.Id);
    }

    private async Task DeactivateAsync(CategoryResponse category)
    {
        var response = await Admin.PutAsJsonAsync($"/api/v1/categories/{category.Id}",
            Payload(category, isActive: false));
        response.EnsureSuccessStatusCode();
    }

    /// <summary>The whole record as the panel submits it, with one cell changed.</summary>
    private static object Payload(CategoryResponse category, bool isActive, string? name = null) => new
    {
        name = name ?? category.Name,
        slug = category.Slug,
        metaTitle = category.MetaTitle,
        metaDescription = category.MetaDescription,
        sortOrder = category.SortOrder,
        isActive,
        isMenu = category.IsMenu,
        redirectToId = category.RedirectToId,
    };

    private async Task<bool> IsActiveAsync(Guid id) => (await GetCategoryAsync(id)).Category.IsActive;

    private sealed record PagedAudit(List<AuditEntryResponse> Items, int Page, int PageSize, int TotalCount);
}
