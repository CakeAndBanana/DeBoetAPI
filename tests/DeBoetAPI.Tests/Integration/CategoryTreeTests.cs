using System.Net;
using System.Net.Http.Json;
using DeBoetAPI.Features.Categories;

namespace DeBoetAPI.Tests.Integration;

/// <summary>
/// The hierarchy itself: paths, depths, breadcrumbs, moves and deletes. These run against a
/// real database because the subtree rewrite is raw SQL — an in-memory provider would prove
/// nothing about it.
/// </summary>
public class CategoryTreeTests(PostgresFixture postgres) : CategoryTestBase(postgres)
{
    [Fact]
    public async Task Chain_HasCorrectDepthsAndBreadcrumb()
    {
        var (christmas, lights, ice) = await CreateChristmasChainAsync();

        Assert.Equal(0, christmas.Depth);
        Assert.Equal(1, lights.Depth);
        Assert.Equal(2, ice.Depth);

        var detail = await GetCategoryAsync(ice.Id);

        Assert.Equal(
            expectedArray,
            detail.Breadcrumb.Select(c => c.Name).ToArray());
        Assert.Equal("Ice Lights", detail.Category.Name);
    }

    [Fact]
    public async Task Create_DerivesTheSlugFromTheName()
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/categories", new { name = $"Ice Lights {Guid.NewGuid():N}" });
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;

        // Spaces become hyphens, the separator a reader expects between words in a URL.
        Assert.StartsWith("ice-lights-", created.Slug);
        Assert.DoesNotContain(' ', created.Slug);
        Assert.DoesNotContain('_', created.Slug);
        Assert.Equal(created.Slug, created.PathSlug); // a root's route is just its own segment
        Assert.Null(created.MetaTitle); // the slug is derived from the name; the meta title is not
        Assert.True(created.IsActive);
    }

    [Fact]
    public async Task Create_RejectsDuplicateSlug()
    {
        var slug = UniqueSlug("dupe");
        await CreateCategoryAsync("First", slug: slug);

        var response = await Admin.PostAsJsonAsync("/api/v1/categories", new { name = "Second", slug });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithUnknownParent_IsNotFound()
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/categories",
            new { name = "Orphan", parentId = Guid.CreateVersion7() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static readonly string[] expected = ["Outdoor", "Christmas Lights"];
    private static readonly string[] expectedArray = ["Christmas", "Christmas Lights"];
    private static readonly string[] expectedArray0 = ["Christmas Lights"];
    private static readonly string[] expectedArray1 = ["Christmas"];
    private static readonly string[] expectedAfterRename = ["Christmas", "Festive Lighting"];

    [Fact]
    public async Task Move_RewritesTheEntireSubtree()
    {
        // The most breakable part of the design: moving a mid-tree node must drag every
        // descendant's path and depth with it, not just its own.
        var (_, lights, ice) = await CreateChristmasChainAsync();
        var outdoor = await CreateCategoryAsync("Outdoor", slug: UniqueSlug("outdoor"));

        var response = await Admin.PostAsJsonAsync($"/api/v1/categories/{lights.Id}/move",
            new { newParentId = outdoor.Id });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var movedLights = (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;
        Assert.Equal(outdoor.Id, movedLights.ParentId);
        Assert.Equal(1, movedLights.Depth);

        // The grandchild is the real assertion.
        var iceDetail = await GetCategoryAsync(ice.Id);
        Assert.Equal(2, iceDetail.Category.Depth);
        Assert.Equal(
            expected,
            iceDetail.Breadcrumb.Select(c => c.Name).ToArray());
    }

    [Fact]
    public async Task Move_ToRoot_FlattensDepths()
    {
        var (_, lights, ice) = await CreateChristmasChainAsync();

        var response = await Admin.PostAsJsonAsync($"/api/v1/categories/{lights.Id}/move",
            new { newParentId = (Guid?)null });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var iceDetail = await GetCategoryAsync(ice.Id);
        Assert.Equal(1, iceDetail.Category.Depth);
        Assert.Equal(expectedArray0, iceDetail.Breadcrumb.Select(c => c.Name).ToArray());
    }

    [Fact]
    public async Task Move_UnderOwnDescendant_IsRejectedAndLeavesTreeIntact()
    {
        var (christmas, lights, ice) = await CreateChristmasChainAsync();

        var response = await Admin.PostAsJsonAsync($"/api/v1/categories/{christmas.Id}/move",
            new { newParentId = ice.Id });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // Nothing may have shifted.
        var detail = await GetCategoryAsync(ice.Id);
        Assert.Equal(2, detail.Category.Depth);
        Assert.Equal(expectedArray, detail.Breadcrumb.Select(c => c.Name).ToArray());
        Assert.Null((await GetCategoryAsync(christmas.Id)).Category.ParentId);
        Assert.Equal(christmas.Id, (await GetCategoryAsync(lights.Id)).Category.ParentId);
    }

    [Fact]
    public async Task Move_ToItself_IsRejected()
    {
        var christmas = await CreateCategoryAsync("Self", slug: UniqueSlug("self"));

        var response = await Admin.PostAsJsonAsync($"/api/v1/categories/{christmas.Id}/move",
            new { newParentId = christmas.Id });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Delete_PromotesChildrenToGrandparent()
    {
        var (christmas, lights, ice) = await CreateChristmasChainAsync();

        var response = await Admin.DeleteAsync($"/api/v1/categories/{lights.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = (await response.Content.ReadFromJsonAsync<DeleteCategoryResponse>())!;
        Assert.Equal([ice.Id], result.PromotedChildIds);

        var iceDetail = await GetCategoryAsync(ice.Id);
        Assert.Equal(christmas.Id, iceDetail.Category.ParentId);
        Assert.Equal(1, iceDetail.Category.Depth);
        Assert.Equal(expectedArray1, iceDetail.Breadcrumb.Select(c => c.Name).ToArray());

        Assert.Equal(HttpStatusCode.NotFound, (await Admin.GetAsync($"/api/v1/categories/{lights.Id}")).StatusCode);
    }

    [Fact]
    public async Task Delete_OfRoot_PromotesChildrenToRoots()
    {
        var (christmas, lights, ice) = await CreateChristmasChainAsync();

        Assert.Equal(HttpStatusCode.OK, (await Admin.DeleteAsync($"/api/v1/categories/{christmas.Id}")).StatusCode);

        var lightsDetail = await GetCategoryAsync(lights.Id);
        Assert.Null(lightsDetail.Category.ParentId);
        Assert.Equal(0, lightsDetail.Category.Depth);

        // The grandchild has to follow its promoted parent.
        var iceDetail = await GetCategoryAsync(ice.Id);
        Assert.Equal(1, iceDetail.Category.Depth);
        Assert.Equal(expectedArray0, iceDetail.Breadcrumb.Select(c => c.Name).ToArray());
    }

    [Fact]
    public async Task Rename_KeepsTheStructureButRewritesDescendantRoutes()
    {
        // Path holds ids, so structure is untouched. PathSlug is built from names, so the
        // descendants' routes do have to follow the rename.
        var (christmas, lights, ice) = await CreateChristmasChainAsync();

        var response = await Admin.PutAsJsonAsync($"/api/v1/categories/{lights.Id}",
            new { name = "Festive Lighting", sortOrder = 5, isActive = true });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var iceDetail = await GetCategoryAsync(ice.Id);
        Assert.Equal(2, iceDetail.Category.Depth);
        Assert.Equal(lights.Id, iceDetail.Category.ParentId);
        // The breadcrumb must show the NEW name — asserting the pre-rename value here would
        // pass even if the rename had silently failed.
        Assert.Equal(expectedAfterRename, iceDetail.Breadcrumb.Select(c => c.Name).ToArray());

        Assert.Equal($"{christmas.Slug}-festive-lighting-{ice.Slug}", iceDetail.Category.PathSlug);
    }

    [Fact]
    public async Task Tree_NestsChildrenUnderTheirRoot()
    {
        var (christmas, lights, ice) = await CreateChristmasChainAsync();

        var tree = await Admin.GetFromJsonAsync<List<CategoryNode>>(
            $"/api/v1/categories/tree?rootId={christmas.Id}");

        Assert.NotNull(tree);
        var root = Assert.Single(tree);
        Assert.Equal(christmas.Id, root.Id);
        var child = Assert.Single(root.Children);
        Assert.Equal(lights.Id, child.Id);
        var grandchild = Assert.Single(child.Children);
        Assert.Equal(ice.Id, grandchild.Id);
    }

    [Fact]
    public async Task Tree_RespectsMaxDepth()
    {
        var (christmas, lights, _) = await CreateChristmasChainAsync();

        var tree = await Admin.GetFromJsonAsync<List<CategoryNode>>(
            $"/api/v1/categories/tree?rootId={christmas.Id}&maxDepth=1");

        Assert.NotNull(tree);
        var child = Assert.Single(Assert.Single(tree).Children);
        Assert.Equal(lights.Id, child.Id);
        Assert.Empty(child.Children);
    }

    [Fact]
    public async Task Category_IsAddressableBySlug()
    {
        var slug = UniqueSlug("byslug");
        var created = await CreateCategoryAsync("By Slug", slug: slug);

        var detail = await Admin.GetFromJsonAsync<CategoryDetailResponse>($"/api/v1/categories/{slug}");

        Assert.NotNull(detail);
        Assert.Equal(created.Id, detail.Category.Id);
    }

    [Fact]
    public async Task PathSlug_LinksTheWholeChainWithDashes()
    {
        var (christmas, lights, ice) = await CreateChristmasChainAsync();

        // These three slugs are supplied as single words, so every dash in the route joins
        // two levels. A derived slug spells its own words with dashes too, so a route in
        // general cannot be split back into its levels — it is matched whole.
        Assert.All([christmas.Slug, lights.Slug, ice.Slug], s => Assert.DoesNotContain('-', s));

        Assert.Equal(christmas.Slug, christmas.PathSlug);
        Assert.Equal($"{christmas.Slug}-{lights.Slug}", lights.PathSlug);
        Assert.Equal($"{christmas.Slug}-{lights.Slug}-{ice.Slug}", ice.PathSlug);

        Assert.Equal(3, ice.PathSlug.Split('-').Length);
    }

    [Fact]
    public async Task MultiWordName_JoinsItsWordsWithHyphens()
    {
        var root = await CreateCategoryAsync("Root", slug: UniqueSlug("root"));

        var child = await CreateCategoryAsync("Christmas Lights", root.Id);

        Assert.Equal("christmas-lights", child.Slug);
        Assert.Equal($"{root.Slug}-christmas-lights", child.PathSlug);
    }

    [Fact]
    public async Task PunctuationInAName_DisappearsRatherThanSplittingAWord()
    {
        var root = await CreateCategoryAsync("Root", slug: UniqueSlug("root"));

        var child = await CreateCategoryAsync("Christmas Light's", root.Id);

        // The apostrophe goes, so "Light's" closes up into "lights" rather than "light-s".
        Assert.Equal("christmas-lights", child.Slug);
    }

    [Fact]
    public async Task AccentsAreFoldedSoTheRouteStaysReadableInAUrl()
    {
        // Left alone these travel as caf%C3%A9-d%C3%A9j%C3%A0-vu, which nobody can read or type.
        var root = await CreateCategoryAsync("Root", slug: UniqueSlug("root"));

        var child = await CreateCategoryAsync("Café Déjà Vu", root.Id);

        Assert.Equal("cafe-deja-vu", child.Slug);
    }

    [Fact]
    public async Task LettersWithNoAccentToStripAreSpelledOut()
    {
        // Decomposition does nothing for these, so without spelling them out they would simply
        // disappear and "Größe" would come back as "groe".
        var root = await CreateCategoryAsync("Root", slug: UniqueSlug("root"));

        var child = await CreateCategoryAsync("Größe Blåbær", root.Id);

        Assert.Equal("grosse-blabaer", child.Slug);
    }

    [Fact]
    public async Task RunsOfSpacesAndPunctuationDoNotPileUpSeparators()
    {
        var root = await CreateCategoryAsync("Root", slug: UniqueSlug("root"));

        var child = await CreateCategoryAsync("  Christmas   &   Lights!  ", root.Id);

        Assert.Equal("christmas-lights", child.Slug);
    }

    [Fact]
    public async Task Category_IsAddressableByItsFullRoute()
    {
        var (_, _, ice) = await CreateChristmasChainAsync();

        var detail = await Admin.GetFromJsonAsync<CategoryDetailResponse>(
            $"/api/v1/categories/{ice.PathSlug}");

        Assert.NotNull(detail);
        Assert.Equal(ice.Id, detail.Category.Id);
        Assert.Equal("Ice Lights", detail.Category.Name);
    }

    [Fact]
    public async Task SameSegment_MayBeReusedUnderDifferentParents()
    {
        // "lights" under two different roots is fine — only the full route must be unique.
        var christmasA = await CreateCategoryAsync("Christmas", slug: UniqueSlug("christmas"));
        var halloween = await CreateCategoryAsync("Halloween", slug: UniqueSlug("halloween"));

        var underChristmas = await CreateCategoryAsync("Lights", christmasA.Id);
        var underHalloween = await CreateCategoryAsync("Lights", halloween.Id);

        Assert.Equal("lights", underChristmas.Slug);
        Assert.Equal("lights", underHalloween.Slug);
        Assert.NotEqual(underChristmas.PathSlug, underHalloween.PathSlug);
    }

    [Fact]
    public async Task SameSegment_IsRejectedUnderOneParent()
    {
        var root = await CreateCategoryAsync("Root", slug: UniqueSlug("root"));
        await CreateCategoryAsync("Ice Lights", root.Id);

        var response = await Admin.PostAsJsonAsync("/api/v1/categories",
            new { name = "Ice Lights", parentId = root.Id });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SuppliedSlug_KeepsHyphensAndTidiesTheRest()
    {
        // Hyphens survive because that is what both a hand-spelled and a derived slug are
        // made of; a space and an underscore alike become one.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var response = await Admin.PostAsJsonAsync("/api/v1/categories",
            new { name = "Anything", slug = $"ice lights_{suffix}" });
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;

        Assert.Equal($"ice-lights-{suffix}", created.Slug);
        Assert.DoesNotContain(' ', created.Slug);
        Assert.DoesNotContain('_', created.Slug);
    }

    [Fact]
    public async Task SavingACategoryAgain_LeavesItsDerivedSlugAlone()
    {
        // The panel submits the stored slug on every save. If the hand-written path rewrote
        // the separator, editing a name would silently move the category's route each time.
        var root = await CreateCategoryAsync("Root", slug: UniqueSlug("root"));
        var child = await CreateCategoryAsync("Christmas Lights", root.Id);
        Assert.Equal("christmas-lights", child.Slug);

        var response = await Admin.PutAsJsonAsync($"/api/v1/categories/{child.Id}", new
        {
            name = "Christmas Lights Renamed",
            slug = child.Slug,
            sortOrder = 0,
            isActive = true,
            isMenu = true,
        });
        response.EnsureSuccessStatusCode();

        var updated = (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;
        Assert.Equal("christmas-lights", updated.Slug);
    }

    [Fact]
    public async Task Move_RewritesDescendantRoutes()
    {
        var (_, lights, ice) = await CreateChristmasChainAsync();
        var outdoor = await CreateCategoryAsync("Outdoor", slug: UniqueSlug("outdoor"));

        await Admin.PostAsJsonAsync($"/api/v1/categories/{lights.Id}/move", new { newParentId = outdoor.Id });

        var iceDetail = await GetCategoryAsync(ice.Id);
        Assert.Equal($"{outdoor.Slug}-{lights.Slug}-{ice.Slug}", iceDetail.Category.PathSlug);
    }

    [Fact]
    public async Task InactiveCategories_AreHiddenUnlessRequested()
    {
        var slug = UniqueSlug("hidden");
        await CreateCategoryAsync("Hidden", isActive: false, slug: slug);

        var visible = await Admin.GetFromJsonAsync<PagedCategories>("/api/v1/categories?rootsOnly=true&pageSize=100");
        Assert.NotNull(visible);
        Assert.DoesNotContain(visible.Items, c => c.Slug == slug);

        var all = await Admin.GetFromJsonAsync<PagedCategories>(
            "/api/v1/categories?rootsOnly=true&includeInactive=true&pageSize=100");
        Assert.NotNull(all);
        Assert.Contains(all.Items, c => c.Slug == slug);
    }

    private sealed record PagedCategories(List<CategoryResponse> Items, int Page, int PageSize, int TotalCount);
}
