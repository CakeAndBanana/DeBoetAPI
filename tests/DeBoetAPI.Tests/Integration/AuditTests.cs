using System.Net;
using System.Net.Http.Json;
using DeBoetAPI.Features.Audit;
using DeBoetAPI.Features.Auth;
using DeBoetAPI.Features.Categories;
using DeBoetAPI.Features.Items;

namespace DeBoetAPI.Tests.Integration;

/// <summary>
/// The change history. Covers both audited entity types, and the property that matters most:
/// the trail outlives the record it describes.
/// </summary>
public class AuditTests(PostgresFixture postgres) : CategoryTestBase(postgres)
{
    [Fact]
    public async Task CreatingACategory_RecordsWhoAndWhen()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);
        var created = await CreateCategoryAsync("Christmas", slug: UniqueSlug("christmas"));

        var history = await HistoryAsync($"/api/v1/categories/{created.Id}/history");

        var entry = Assert.Single(history.Items);
        Assert.Equal("Created", entry.Action);
        Assert.Equal("Category", entry.EntityType);
        Assert.Equal("Christmas", entry.EntityName);
        Assert.Equal("Administrator", entry.ChangedByName);
        Assert.NotNull(entry.ChangedById);
        Assert.True(entry.ChangedAt >= before);
    }

    [Fact]
    public async Task EditingACategory_RecordsWhatChanged()
    {
        var created = await CreateCategoryAsync("Christmas", slug: UniqueSlug("christmas"));

        var response = await Admin.PutAsJsonAsync($"/api/v1/categories/{created.Id}",
            new { name = "Festive", slug = created.Slug, sortOrder = 3, isActive = false, isMenu = true });
        response.EnsureSuccessStatusCode();

        var history = await HistoryAsync($"/api/v1/categories/{created.Id}/history");
        var update = history.Items.First(e => e.Action == "Updated");

        Assert.Contains("name: Christmas -> Festive", update.Summary);
        Assert.Contains("sortOrder: 0 -> 3", update.Summary);
        Assert.Contains("isActive: True -> False", update.Summary);
    }

    [Fact]
    public async Task AnUnchangedSave_RecordsNothing()
    {
        // Re-submitting identical values must not litter the trail with empty Updated rows.
        var created = await CreateCategoryAsync("Christmas", slug: UniqueSlug("christmas"));

        var response = await Admin.PutAsJsonAsync($"/api/v1/categories/{created.Id}", new
        {
            name = created.Name,
            slug = created.Slug,
            metaTitle = created.MetaTitle,
            sortOrder = created.SortOrder,
            isActive = created.IsActive,
            isMenu = created.IsMenu,
        });
        response.EnsureSuccessStatusCode();

        var history = await HistoryAsync($"/api/v1/categories/{created.Id}/history");
        Assert.DoesNotContain(history.Items, e => e.Action == "Updated");
    }

    [Fact]
    public async Task MovingACategory_RecordsTheOldAndNewRoute()
    {
        var (_, lights, _) = await CreateChristmasChainAsync();
        var outdoor = await CreateCategoryAsync("Outdoor", slug: UniqueSlug("outdoor"));

        await Admin.PostAsJsonAsync($"/api/v1/categories/{lights.Id}/move", new { newParentId = outdoor.Id });

        var history = await HistoryAsync($"/api/v1/categories/{lights.Id}/history");
        var move = history.Items.First(e => e.Action == "Moved");

        Assert.Contains(lights.PathSlug, move.Summary);
        Assert.Contains(outdoor.Slug, move.Summary);
    }

    [Fact]
    public async Task DeletingACategory_LeavesItsHistoryIntact()
    {
        // The whole point of an audit trail: a deletion is the event you most need later.
        var (_, lights, _) = await CreateChristmasChainAsync();

        Assert.Equal(HttpStatusCode.OK, (await Admin.DeleteAsync($"/api/v1/categories/{lights.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Admin.GetAsync($"/api/v1/categories/{lights.Id}")).StatusCode);

        var history = await HistoryAsync($"/api/v1/categories/{lights.Id}/history");

        Assert.Contains(history.Items, e => e.Action == "Created");
        var deletion = Assert.Single(history.Items, e => e.Action == "Deleted");
        Assert.Equal("Christmas Lights", deletion.EntityName);
        Assert.Equal("Administrator", deletion.ChangedByName);
    }

    [Fact]
    public async Task PromotedChildren_AreRecordedToo()
    {
        var (_, lights, ice) = await CreateChristmasChainAsync();

        await Admin.DeleteAsync($"/api/v1/categories/{lights.Id}");

        var history = await HistoryAsync($"/api/v1/categories/{ice.Id}/history");
        var promoted = Assert.Single(history.Items, e => e.Action == "ChildPromoted");
        Assert.Contains("Christmas Lights", promoted.Summary);
    }

    [Fact]
    public async Task LinkChanges_AreRecorded()
    {
        var (_, _, ice) = await CreateChristmasChainAsync();
        var outdoor = await CreateCategoryAsync("Outdoor Lighting", slug: UniqueSlug("outdoor"));

        await Admin.PostAsJsonAsync($"/api/v1/categories/{ice.Id}/links", new { parentId = outdoor.Id });
        await Admin.DeleteAsync($"/api/v1/categories/{ice.Id}/links/{outdoor.Id}");

        var history = await HistoryAsync($"/api/v1/categories/{ice.Id}/history");

        Assert.Contains(history.Items, e => e.Action == "LinkAdded");
        Assert.Contains(history.Items, e => e.Action == "LinkRemoved");
    }

    [Fact]
    public async Task ItemsAreAuditedAsWell()
    {
        var create = await User.PostAsJsonAsync("/api/v1/items", new { name = "Icicle string" });
        create.EnsureSuccessStatusCode();
        var item = (await create.Content.ReadFromJsonAsync<ItemResponse>())!;

        await User.PutAsJsonAsync($"/api/v1/items/{item.Id}",
            new { name = "Icicle string v2", description = "now with more sparkle" });

        var history = await HistoryAsync($"/api/v1/items/{item.Id}/history", User);

        Assert.Equal(2, history.Items.Count);
        Assert.Contains(history.Items, e => e.Action == "Created" && e.EntityType == "Item");
        var update = Assert.Single(history.Items, e => e.Action == "Updated");
        Assert.Contains("name: Icicle string -> Icicle string v2", update.Summary);
        Assert.Equal("Ordinary User", update.ChangedByName);
    }

    [Fact]
    public async Task ItemHistory_IsOwnerScoped()
    {
        var create = await User.PostAsJsonAsync("/api/v1/items", new { name = "Private" });
        create.EnsureSuccessStatusCode();
        var item = (await create.Content.ReadFromJsonAsync<ItemResponse>())!;

        // Admin is a different account, and the item is invisible to them.
        Assert.Equal(HttpStatusCode.NotFound, (await Admin.GetAsync($"/api/v1/items/{item.Id}/history")).StatusCode);
    }

    [Fact]
    public async Task GlobalFeed_SpansBothEntityTypesAndFilters()
    {
        var category = await CreateCategoryAsync("Feed", slug: UniqueSlug("feed"));
        var create = await User.PostAsJsonAsync("/api/v1/items", new { name = "Feed item" });
        create.EnsureSuccessStatusCode();
        var item = (await create.Content.ReadFromJsonAsync<ItemResponse>())!;

        var all = await HistoryAsync("/api/v1/audit?pageSize=100");
        Assert.Contains(all.Items, e => e.EntityId == category.Id && e.EntityType == "Category");
        Assert.Contains(all.Items, e => e.EntityId == item.Id && e.EntityType == "Item");

        var itemsOnly = await HistoryAsync("/api/v1/audit?entityType=Item&pageSize=100");
        Assert.All(itemsOnly.Items, e => Assert.Equal("Item", e.EntityType));

        var single = await HistoryAsync($"/api/v1/audit?entityId={category.Id}");
        Assert.All(single.Items, e => Assert.Equal(category.Id, e.EntityId));
    }

    [Fact]
    public async Task GlobalFeed_IsAdminOnly()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await User.GetAsync("/api/v1/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonymous.GetAsync("/api/v1/audit")).StatusCode);
    }

    [Fact]
    public async Task CategoryResponse_CarriesTheLastEditor()
    {
        var created = await CreateCategoryAsync("Stamped", slug: UniqueSlug("stamped"));

        Assert.NotNull(created.CreatedById);
        Assert.Equal(created.CreatedById, created.UpdatedById);

        var detail = await GetCategoryAsync(created.Id);
        Assert.Equal(created.CreatedById, detail.Category.UpdatedById);
    }

    private async Task<PagedAudit> HistoryAsync(string url, HttpClient? client = null)
    {
        var result = await (client ?? Admin).GetFromJsonAsync<PagedAudit>(url);
        Assert.NotNull(result);
        return result;
    }

    private sealed record PagedAudit(List<AuditEntryResponse> Items, int Page, int PageSize, int TotalCount);

    [Fact]
    public async Task Audit_FiltersByAction()
    {
        var category = await CreateCategoryAsync("Filtered", slug: UniqueSlug("filtered"));
        await Admin.PutAsJsonAsync($"/api/v1/categories/{category.Id}",
            new { name = "Filtered Twice", sortOrder = 0, isActive = true, isMenu = true });

        var created = await Admin.GetFromJsonAsync<PagedAudit>(
            $"/api/v1/audit?entityId={category.Id}&action=Created");
        var updated = await Admin.GetFromJsonAsync<PagedAudit>(
            $"/api/v1/audit?entityId={category.Id}&action=Updated");

        Assert.NotNull(created);
        Assert.NotNull(updated);
        Assert.All(created.Items, e => Assert.Equal("Created", e.Action));
        Assert.All(updated.Items, e => Assert.Equal("Updated", e.Action));
        Assert.Single(created.Items);
        Assert.Single(updated.Items);
    }

    [Fact]
    public async Task Audit_FiltersByPerson()
    {
        var (editor, editorId, _, _) = await CreateUserAndSignInAsync(AuthPolicies.EditorRole);
        var response = await editor.PostAsJsonAsync("/api/v1/categories",
            new { name = "By The Editor", slug = UniqueSlug("bytheeditor") });
        response.EnsureSuccessStatusCode();

        var theirs = await Admin.GetFromJsonAsync<PagedAudit>($"/api/v1/audit?changedById={editorId}");

        Assert.NotNull(theirs);
        Assert.NotEmpty(theirs.Items);
        Assert.All(theirs.Items, e => Assert.Equal(editorId, e.ChangedById));
    }

    [Fact]
    public async Task Audit_CombinesActionAndPerson()
    {
        var (editor, editorId, _, _) = await CreateUserAndSignInAsync(AuthPolicies.EditorRole);
        var created = await editor.PostAsJsonAsync("/api/v1/categories",
            new { name = "Both Filters", slug = UniqueSlug("bothfilters") });
        created.EnsureSuccessStatusCode();
        var category = (await created.Content.ReadFromJsonAsync<CategoryResponse>())!;

        await editor.PutAsJsonAsync($"/api/v1/categories/{category.Id}",
            new { name = "Both Filters Again", sortOrder = 0, isActive = true, isMenu = true });

        var both = await Admin.GetFromJsonAsync<PagedAudit>(
            $"/api/v1/audit?changedById={editorId}&action=Updated");

        Assert.NotNull(both);
        Assert.All(both.Items, e =>
        {
            Assert.Equal(editorId, e.ChangedById);
            Assert.Equal("Updated", e.Action);
        });
        Assert.Contains(both.Items, e => e.EntityId == category.Id);
    }

    [Theory]
    [InlineData("entityType=Nonsense")]
    [InlineData("action=Nonsense")]
    [InlineData("entityId=notauuid")]
    [InlineData("changedById=notauuid")]
    [InlineData("since=whenever")]
    public async Task Audit_RejectsAnUnreadableFilterWithoutFailing(string query)
    {
        // These arrive straight off an editable URL, so a typo has to read as a bad request
        // rather than an internal error.
        var response = await Admin.GetAsync($"/api/v1/audit?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

}
