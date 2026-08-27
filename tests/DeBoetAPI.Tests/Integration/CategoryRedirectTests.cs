using System.Net;
using System.Net.Http.Json;
using DeBoetAPI.Features.Audit;
using DeBoetAPI.Features.Categories;

namespace DeBoetAPI.Tests.Integration;

/// <summary>
/// Forwarding one category to another, for nodes that organise the tree but have nothing of
/// their own to show.
/// </summary>
public class CategoryRedirectTests(PostgresFixture postgres) : CategoryTestBase(postgres)
{
    [Fact]
    public async Task ACategory_HasNoRedirectByDefault()
    {
        var category = await CreateCategoryAsync("Plain", slug: UniqueSlug("plain"));

        Assert.Null(category.RedirectToId);
        Assert.Null((await DetailAsync(category.Id)).RedirectTo);
    }

    [Fact]
    public async Task ACategory_CanForwardToAnother()
    {
        var trees = await CreateCategoryAsync("Christmas Trees", slug: UniqueSlug("christmastrees"));
        var decorations = await CreateCategoryAsync("Christmas Decorations", slug: UniqueSlug("christmasdecorations"));

        var response = await SetRedirectAsync(decorations, trees.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;
        Assert.Equal(trees.Id, updated.RedirectToId);
    }

    [Fact]
    public async Task TheDestination_ComesBackResolvedSoASiteNeedsOneRequest()
    {
        var trees = await CreateCategoryAsync("Christmas Trees", slug: UniqueSlug("christmastrees"));
        var decorations = await CreateCategoryAsync("Christmas Decorations", slug: UniqueSlug("christmasdecorations"));
        await SetRedirectAsync(decorations, trees.Id);

        var detail = await DetailAsync(decorations.Id);

        Assert.NotNull(detail.RedirectTo);
        // The route is the point: it is what the site sends the visitor to.
        Assert.Equal(trees.PathSlug, detail.RedirectTo.PathSlug);
        Assert.Equal("Christmas Trees", detail.RedirectTo.Name);
    }

    [Fact]
    public async Task ARedirect_CanBeClearedAgain()
    {
        var trees = await CreateCategoryAsync("Trees", slug: UniqueSlug("trees"));
        var decorations = await CreateCategoryAsync("Decorations", slug: UniqueSlug("decorations"));
        await SetRedirectAsync(decorations, trees.Id);

        var response = await SetRedirectAsync(decorations, null);
        response.EnsureSuccessStatusCode();

        Assert.Null((await response.Content.ReadFromJsonAsync<CategoryResponse>())!.RedirectToId);
    }

    [Fact]
    public async Task ACategory_CannotForwardToItself()
    {
        var category = await CreateCategoryAsync("Loop", slug: UniqueSlug("loop"));

        var response = await SetRedirectAsync(category, category.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ACategory_CannotForwardToOneThatIsMissing()
    {
        var category = await CreateCategoryAsync("Dangling", slug: UniqueSlug("dangling"));

        var response = await SetRedirectAsync(category, Guid.CreateVersion7());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ACategory_CannotForwardToOneThatAlreadyForwards()
    {
        // Kept to a single hop, so nobody has to follow a chain, and so a cycle cannot exist.
        var end = await CreateCategoryAsync("End", slug: UniqueSlug("end"));
        var middle = await CreateCategoryAsync("Middle", slug: UniqueSlug("middle"));
        var start = await CreateCategoryAsync("Start", slug: UniqueSlug("start"));
        await SetRedirectAsync(middle, end.Id);

        var response = await SetRedirectAsync(start, middle.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ADestination_CannotItselfStartForwarding()
    {
        // The same rule from the far end: allowing this would grow a chain behind the back of
        // whoever already pointed here.
        var end = await CreateCategoryAsync("End", slug: UniqueSlug("end"));
        var destination = await CreateCategoryAsync("Destination", slug: UniqueSlug("destination"));
        var source = await CreateCategoryAsync("Source", slug: UniqueSlug("source"));
        await SetRedirectAsync(source, destination.Id);

        var response = await SetRedirectAsync(destination, end.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ARedirect_CanBeSetWhenTheCategoryIsCreated()
    {
        var trees = await CreateCategoryAsync("Trees", slug: UniqueSlug("trees"));

        var response = await Admin.PostAsJsonAsync("/api/v1/categories", new
        {
            name = "Decorations",
            slug = UniqueSlug("decorations"),
            redirectToId = trees.Id,
        });
        response.EnsureSuccessStatusCode();

        var created = (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;
        Assert.Equal(trees.Id, created.RedirectToId);
    }

    [Fact]
    public async Task DeletingTheDestination_LeavesTheSourceWithoutItsRedirect()
    {
        // The source is still a real category; it simply stops forwarding. Blocking the delete,
        // or deleting the source too, would both be worse answers.
        var trees = await CreateCategoryAsync("Trees", slug: UniqueSlug("trees"));
        var decorations = await CreateCategoryAsync("Decorations", slug: UniqueSlug("decorations"));
        await SetRedirectAsync(decorations, trees.Id);

        Assert.Equal(HttpStatusCode.OK, (await Admin.DeleteAsync($"/api/v1/categories/{trees.Id}")).StatusCode);

        var detail = await DetailAsync(decorations.Id);
        Assert.Null(detail.Category.RedirectToId);
        Assert.Null(detail.RedirectTo);
    }

    [Fact]
    public async Task TheTree_ReportsWhichCategoriesForward()
    {
        var trees = await CreateCategoryAsync("Trees", slug: UniqueSlug("trees"));
        var decorations = await CreateCategoryAsync("Decorations", slug: UniqueSlug("decorations"));
        await SetRedirectAsync(decorations, trees.Id);

        var tree = await Admin.GetFromJsonAsync<List<CategoryNode>>("/api/v1/categories/tree");

        Assert.NotNull(tree);
        Assert.Equal(trees.Id, tree.Single(n => n.Id == decorations.Id).RedirectToId);
        Assert.Null(tree.Single(n => n.Id == trees.Id).RedirectToId);
    }

    [Fact]
    public async Task SettingARedirect_IsRecordedInTheTrail()
    {
        var trees = await CreateCategoryAsync("Trees", slug: UniqueSlug("trees"));
        var decorations = await CreateCategoryAsync("Decorations", slug: UniqueSlug("decorations"));

        await SetRedirectAsync(decorations, trees.Id);

        var history = await Admin.GetFromJsonAsync<PagedAudit>($"/api/v1/categories/{decorations.Id}/history");
        Assert.NotNull(history);
        var update = Assert.Single(history.Items, e => e.Action == "Updated");
        Assert.Contains("redirectTo", update.Summary);
    }

    private Task<HttpResponseMessage> SetRedirectAsync(CategoryResponse category, Guid? targetId) =>
        Admin.PutAsJsonAsync($"/api/v1/categories/{category.Id}", new
        {
            name = category.Name,
            slug = category.Slug,
            redirectToId = targetId,
            sortOrder = 0,
            isActive = true,
            isMenu = true,
        });

    private async Task<CategoryDetailResponse> DetailAsync(Guid id)
    {
        var detail = await Admin.GetFromJsonAsync<CategoryDetailResponse>($"/api/v1/categories/{id}");
        Assert.NotNull(detail);
        return detail;
    }

    private sealed record PagedAudit(List<AuditEntryResponse> Items);
}
