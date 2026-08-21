using System.Net;
using System.Net.Http.Json;
using DeBoetAPI.Features.Categories;

namespace DeBoetAPI.Tests.Integration;

/// <summary>
/// Sibling order: what decides which category sits at the top of a menu.
/// </summary>
public class CategoryOrderTests(PostgresFixture postgres) : CategoryTestBase(postgres)
{
    [Fact]
    public async Task NewSiblings_KeepTheOrderTheyWereAddedIn()
    {
        var (root, names) = await ThreeSiblingsAsync();

        Assert.Equal(names, await OrderUnderAsync(root.Id));
    }

    [Fact]
    public async Task ACategory_CanBeSentToTheTop()
    {
        var (root, names) = await ThreeSiblingsAsync();
        var last = await ChildAsync(root.Id, names[2]);

        var response = await Admin.PostAsJsonAsync($"/api/v1/categories/{last.Id}/move",
            new { newParentId = root.Id, position = 0 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal([names[2], names[0], names[1]], await OrderUnderAsync(root.Id));
    }

    [Fact]
    public async Task ACategory_CanStepDownOnePlace()
    {
        // What the down arrow does: position + 1.
        var (root, names) = await ThreeSiblingsAsync();
        var first = await ChildAsync(root.Id, names[0]);

        await Admin.PostAsJsonAsync($"/api/v1/categories/{first.Id}/move",
            new { newParentId = root.Id, position = 1 });

        Assert.Equal([names[1], names[0], names[2]], await OrderUnderAsync(root.Id));
    }

    [Fact]
    public async Task APositionPastTheEnd_LandsLastRatherThanFailing()
    {
        var (root, names) = await ThreeSiblingsAsync();
        var first = await ChildAsync(root.Id, names[0]);

        var response = await Admin.PostAsJsonAsync($"/api/v1/categories/{first.Id}/move",
            new { newParentId = root.Id, position = 99 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal([names[1], names[2], names[0]], await OrderUnderAsync(root.Id));
    }

    [Fact]
    public async Task Reordering_LeavesNoGapsInSortOrder()
    {
        // Renumbering from zero every time is what stops repeated drags drifting into
        // sparse, meaningless numbers.
        var (root, names) = await ThreeSiblingsAsync();
        var last = await ChildAsync(root.Id, names[2]);

        await Admin.PostAsJsonAsync($"/api/v1/categories/{last.Id}/move",
            new { newParentId = root.Id, position = 0 });

        var detail = await Admin.GetFromJsonAsync<CategoryDetailResponse>($"/api/v1/categories/{root.Id}");
        Assert.NotNull(detail);
        Assert.Equal([0, 1, 2], detail.Children.Select(c => (int)c.SortOrder).ToArray());
    }

    [Fact]
    public async Task MovingToAnotherParent_LandsLastByDefault()
    {
        var (root, names) = await ThreeSiblingsAsync();
        var elsewhere = await CreateCategoryAsync("Elsewhere", slug: UniqueSlug("elsewhere"));
        var stray = await CreateCategoryAsync("Stray", elsewhere.Id);

        var response = await Admin.PostAsJsonAsync($"/api/v1/categories/{stray.Id}/move",
            new { newParentId = root.Id });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal([names[0], names[1], names[2], "Stray"], await OrderUnderAsync(root.Id));
    }

    [Fact]
    public async Task MovingToAnotherParent_CanAskForAPositionAtTheSameTime()
    {
        // One call, because dragging a category between two others is both at once.
        var (root, names) = await ThreeSiblingsAsync();
        var elsewhere = await CreateCategoryAsync("Elsewhere", slug: UniqueSlug("elsewhere"));
        var stray = await CreateCategoryAsync("Stray", elsewhere.Id);

        await Admin.PostAsJsonAsync($"/api/v1/categories/{stray.Id}/move",
            new { newParentId = root.Id, position = 1 });

        Assert.Equal([names[0], "Stray", names[1], names[2]], await OrderUnderAsync(root.Id));
    }

    [Fact]
    public async Task AReorder_IsRecordedAsOneAndSaysWhereItWent()
    {
        var (root, names) = await ThreeSiblingsAsync();
        var last = await ChildAsync(root.Id, names[2]);

        await Admin.PostAsJsonAsync($"/api/v1/categories/{last.Id}/move",
            new { newParentId = root.Id, position = 0 });

        var history = await Admin.GetFromJsonAsync<PagedAudit>($"/api/v1/categories/{last.Id}/history");
        Assert.NotNull(history);
        var entry = Assert.Single(history.Items, e => e.Action == "Reordered");
        Assert.Contains("position: 2 -> 0", entry.Summary);
        // A reorder is not a move: the route did not change, so nothing should say it did.
        Assert.DoesNotContain(history.Items, e => e.Action == "Moved");
    }

    [Fact]
    public async Task AskingForThePositionItAlreadyHas_RecordsNothing()
    {
        var (root, names) = await ThreeSiblingsAsync();
        var first = await ChildAsync(root.Id, names[0]);

        await Admin.PostAsJsonAsync($"/api/v1/categories/{first.Id}/move",
            new { newParentId = root.Id, position = 0 });

        var history = await Admin.GetFromJsonAsync<PagedAudit>($"/api/v1/categories/{first.Id}/history");
        Assert.NotNull(history);
        Assert.DoesNotContain(history.Items, e => e.Action == "Reordered");
    }

    private async Task<(CategoryResponse Root, string[] Names)> ThreeSiblingsAsync()
    {
        var root = await CreateCategoryAsync("Ordered", slug: UniqueSlug("ordered"));
        var names = new[] { "First", "Second", "Third" };

        foreach (var name in names)
        {
            await CreateCategoryAsync(name, root.Id);
        }

        return (root, names);
    }

    private async Task<string[]> OrderUnderAsync(Guid rootId)
    {
        var detail = await Admin.GetFromJsonAsync<CategoryDetailResponse>($"/api/v1/categories/{rootId}");
        Assert.NotNull(detail);
        return [.. detail.Children.Select(c => c.Name)];
    }

    private async Task<CategoryResponse> ChildAsync(Guid rootId, string name)
    {
        var detail = await Admin.GetFromJsonAsync<CategoryDetailResponse>($"/api/v1/categories/{rootId}");
        Assert.NotNull(detail);
        return detail.Children.Single(c => c.Name == name);
    }

    private sealed record PagedAudit(List<DeBoetAPI.Features.Audit.AuditEntryResponse> Items);
}
