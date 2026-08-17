using System.Net;
using System.Net.Http.Json;
using DeBoetAPI.Common;
using DeBoetAPI.Features.Items;

namespace DeBoetAPI.Tests.Integration;

public class ItemEndpointsTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    [Fact]
    public async Task Crud_RoundTripsThroughTheDatabase()
    {
        var client = await AuthenticatedClientAsync();

        var created = await CreateItemAsync(client, "First item", "created by test");
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("First item", created.Name);

        var fetched = await client.GetFromJsonAsync<ItemResponse>($"/api/v1/items/{created.Id}");
        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal("created by test", fetched.Description);

        var updateResponse = await client.PutAsJsonAsync($"/api/v1/items/{created.Id}",
            new { name = "Renamed", description = (string?)null });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = (await updateResponse.Content.ReadFromJsonAsync<ItemResponse>())!;
        Assert.Equal("Renamed", updated.Name);
        Assert.Null(updated.Description);
        Assert.True(updated.UpdatedAt >= created.UpdatedAt);

        var deleteResponse = await client.DeleteAsync($"/api/v1/items/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var afterDelete = await client.GetAsync($"/api/v1/items/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task Create_LocationHeaderPointsAtTheNewItem()
    {
        var client = await AuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/items", new { name = "Located" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var location = response.Headers.Location;
        Assert.NotNull(location);

        var followed = await client.GetAsync(location);
        Assert.Equal(HttpStatusCode.OK, followed.StatusCode);
    }

    [Fact]
    public async Task Create_RejectsInvalidPayload()
    {
        var client = await AuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/items", new { name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AllRoutes_RequireAuthentication()
    {
        var id = Guid.CreateVersion7();

        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/api/v1/items")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync($"/api/v1/items/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Client.PostAsJsonAsync("/api/v1/items", new { name = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Client.PutAsJsonAsync($"/api/v1/items/{id}", new { name = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.DeleteAsync($"/api/v1/items/{id}")).StatusCode);
    }

    [Fact]
    public async Task Items_AreInvisibleToOtherUsers()
    {
        var owner = await AuthenticatedClientAsync();
        var stranger = await AuthenticatedClientAsync();

        var item = await CreateItemAsync(owner, "Private", "owner only");

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/v1/items/{item.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await stranger.PutAsJsonAsync($"/api/v1/items/{item.Id}", new { name = "hijacked" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/v1/items/{item.Id}")).StatusCode);

        var strangerList = await stranger.GetFromJsonAsync<PagedResult<ItemResponse>>("/api/v1/items");
        Assert.NotNull(strangerList);
        Assert.DoesNotContain(strangerList.Items, i => i.Id == item.Id);

        // The owner still has it untouched.
        var ownerList = await owner.GetFromJsonAsync<PagedResult<ItemResponse>>("/api/v1/items");
        Assert.NotNull(ownerList);
        Assert.Contains(ownerList.Items, i => i.Id == item.Id && i.Name == "Private");
    }

    [Fact]
    public async Task List_PagesResultsAndReportsTotals()
    {
        var client = await AuthenticatedClientAsync();
        for (var i = 0; i < 5; i++)
        {
            await CreateItemAsync(client, $"Item {i}");
        }

        var firstPage = await client.GetFromJsonAsync<PagedResult<ItemResponse>>("/api/v1/items?page=1&pageSize=2");
        Assert.NotNull(firstPage);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Equal(5, firstPage.TotalCount);
        Assert.Equal(3, firstPage.TotalPages);

        var lastPage = await client.GetFromJsonAsync<PagedResult<ItemResponse>>("/api/v1/items?page=3&pageSize=2");
        Assert.NotNull(lastPage);
        Assert.Single(lastPage.Items);

        // Pages must not overlap.
        var firstIds = firstPage.Items.Select(i => i.Id).ToHashSet();
        Assert.DoesNotContain(lastPage.Items[0].Id, firstIds);
    }

    [Fact]
    public async Task List_ClampsOversizedPageSize()
    {
        var client = await AuthenticatedClientAsync();
        await CreateItemAsync(client, "Only one");

        var page = await client.GetFromJsonAsync<PagedResult<ItemResponse>>("/api/v1/items?pageSize=5000");

        Assert.NotNull(page);
        Assert.Equal(Paging.MaxPageSize, page.PageSize);
    }

    [Fact]
    public async Task List_ReturnsNewestFirst()
    {
        var client = await AuthenticatedClientAsync();
        await CreateItemAsync(client, "Older");
        await CreateItemAsync(client, "Newer");

        var page = await client.GetFromJsonAsync<PagedResult<ItemResponse>>("/api/v1/items");

        Assert.NotNull(page);
        Assert.Equal("Newer", page.Items[0].Name);
        Assert.Equal("Older", page.Items[1].Name);
    }

    private static async Task<ItemResponse> CreateItemAsync(HttpClient client, string name, string? description = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/items", new { name, description });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ItemResponse>())!;
    }
}
