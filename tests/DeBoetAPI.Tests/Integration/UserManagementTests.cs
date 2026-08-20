using System.Net;
using System.Net.Http.Json;
using DeBoetAPI.Features.Audit;
using DeBoetAPI.Features.Auth;
using DeBoetAPI.Features.Categories;

namespace DeBoetAPI.Tests.Integration;

/// <summary>
/// Admin-provisioned accounts, the Editor role, and the forced password change. The security
/// properties here are enforced by the API, so they are tested against it rather than through
/// the admin panel.
/// </summary>
public class UserManagementTests(PostgresFixture postgres) : CategoryTestBase(postgres)
{
    // ── Editor rights ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Editor_CanCreateAndEditCategories()
    {
        var (editor, _, _, _) = await CreateUserAndSignInAsync(AuthPolicies.EditorRole);

        var created = await editor.PostAsJsonAsync("/api/v1/categories",
            new { name = "Editor Made", slug = UniqueSlug("editormade") });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var category = (await created.Content.ReadFromJsonAsync<CategoryResponse>())!;

        var edited = await editor.PutAsJsonAsync($"/api/v1/categories/{category.Id}",
            new { name = "Editor Renamed", sortOrder = 0, isActive = true, isMenu = true });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
    }

    [Fact]
    public async Task Editor_MayReshapeTheTree()
    {
        // The whole taxonomy belongs to editors: move, delete and secondary links included.
        // Only the audit trail and user management are held back, covered by the next test.
        var (editor, _, _, _) = await CreateUserAndSignInAsync(AuthPolicies.EditorRole);
        var (christmas, lights, ice) = await CreateChristmasChainAsync();

        Assert.Equal(HttpStatusCode.OK,
            (await editor.PostAsJsonAsync($"/api/v1/categories/{ice.Id}/move", new { newParentId = (Guid?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await editor.PostAsJsonAsync($"/api/v1/categories/{ice.Id}/links", new { parentId = christmas.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await editor.DeleteAsync($"/api/v1/categories/{ice.Id}/links/{christmas.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await editor.DeleteAsync($"/api/v1/categories/{lights.Id}")).StatusCode);
    }

    [Fact]
    public async Task Editor_CannotManageUsersOrReadTheAuditTrail()
    {
        var (editor, _, _, _) = await CreateUserAndSignInAsync(AuthPolicies.EditorRole);

        Assert.Equal(HttpStatusCode.Forbidden, (await editor.GetAsync("/api/v1/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.GetAsync("/api/v1/audit")).StatusCode);

        // But one category's own history is theirs to read, since the tree is theirs to change.
        var category = await CreateCategoryAsync("Editor Readable", slug: UniqueSlug("editorreadable"));
        Assert.Equal(HttpStatusCode.OK,
            (await editor.GetAsync($"/api/v1/categories/{category.Id}/history")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await editor.PostAsJsonAsync("/api/v1/users", new { userName = "x", email = "x@y.z", displayName = "X", password = "Passw0rd!" })).StatusCode);
    }

    [Fact]
    public async Task UserWithoutRoles_CannotTouchCategories()
    {
        var (plain, _, _, _) = await CreateUserAndSignInAsync();

        Assert.Equal(HttpStatusCode.Forbidden,
            (await plain.PostAsJsonAsync("/api/v1/categories", new { name = "Nope" })).StatusCode);
    }

    // ── Forced password change ───────────────────────────────────────────────────────────

    [Fact]
    public async Task NewAccount_MustChangeItsPasswordBeforeDoingAnything()
    {
        // The whole point of enforcing this in the API: a temporary password the admin also
        // knows must not be usable for real work, however the client chooses to behave.
        var (editor, _, _, _) = await CreateUserAndSignInAsync(AuthPolicies.EditorRole, changePasswordFirst: false);

        var blocked = await editor.PostAsJsonAsync("/api/v1/categories",
            new { name = "Should Not Exist", slug = UniqueSlug("blocked") });
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);

        // But it can see who it is, and change the password.
        var me = await editor.GetFromJsonAsync<UserResponse>("/api/v1/auth/me");
        Assert.NotNull(me);
        Assert.True(me.MustChangePassword);
    }

    [Fact]
    public async Task ChangingThePassword_ClearsTheFlagAndUnlocksWork()
    {
        var (editor, _, _, _) = await CreateUserAndSignInAsync(AuthPolicies.EditorRole, changePasswordFirst: true);

        var me = await editor.GetFromJsonAsync<UserResponse>("/api/v1/auth/me");
        Assert.NotNull(me);
        Assert.False(me.MustChangePassword);

        var created = await editor.PostAsJsonAsync("/api/v1/categories",
            new { name = "Now Allowed", slug = UniqueSlug("nowallowed") });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    [Fact]
    public async Task ChangingThePassword_RevokesOtherSessions()
    {
        var (editor, _, userName, password) = await CreateUserAndSignInAsync(AuthPolicies.EditorRole);

        // A second session, as if the account were signed in elsewhere.
        var second = NewClient();
        var login = await second.PostAsJsonAsync("/api/v1/auth/login", new { email = userName, password });
        login.EnsureSuccessStatusCode();
        var otherSession = (await login.Content.ReadFromJsonAsync<TokenPairResponse>())!;

        var changed = await editor.PostAsJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = password, newPassword = "Another!Pass1" });
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        // The other session's refresh token is dead, so it cannot renew itself.
        var refresh = await second.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = otherSession.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task ChangingThePassword_RejectsAWrongCurrentPassword()
    {
        var (editor, _, _, _) = await CreateUserAndSignInAsync(AuthPolicies.EditorRole);

        var response = await editor.PostAsJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = "NotThePassword1", newPassword = "Another!Pass1" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Admin reset ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdminReset_ForcesAnotherChangeAndKillsTheSession()
    {
        var (editor, id, userName, _) = await CreateUserAndSignInAsync(AuthPolicies.EditorRole);

        // Working normally before the reset.
        Assert.Equal(HttpStatusCode.Created,
            (await editor.PostAsJsonAsync("/api/v1/categories", new { name = "Before", slug = UniqueSlug("before") })).StatusCode);

        var reset = await Admin.PostAsJsonAsync($"/api/v1/users/{id}/reset-password",
            new { newPassword = "Reset!Pass1" });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        // Signing in with the admin-set password lands back in the forced-change state.
        var after = await SignInAsync(userName, "Reset!Pass1");
        var me = await after.GetFromJsonAsync<UserResponse>("/api/v1/auth/me");
        Assert.NotNull(me);
        Assert.True(me.MustChangePassword);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await after.PostAsJsonAsync("/api/v1/categories", new { name = "After" })).StatusCode);
    }

    [Fact]
    public async Task AdminReset_InvalidatesTheOldPassword()
    {
        var (_, id, userName, oldPassword) = await CreateUserAndSignInAsync(AuthPolicies.EditorRole);

        await Admin.PostAsJsonAsync($"/api/v1/users/{id}/reset-password", new { newPassword = "Reset!Pass1" });

        var login = await NewClient().PostAsJsonAsync("/api/v1/auth/login",
            new { email = userName, password = oldPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    // ── Roles, disabling and guard rails ────────────────────────────────────────────────

    [Fact]
    public async Task Admin_CanGrantAndRevokeTheEditorRole()
    {
        var (_, id, userName, password) = await CreateUserAndSignInAsync();

        var granted = await Admin.PutAsJsonAsync($"/api/v1/users/{id}/roles",
            new { roles = new[] { AuthPolicies.EditorRole } });
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);

        var withRole = await SignInAsync(userName, password);
        Assert.Equal(HttpStatusCode.Created,
            (await withRole.PostAsJsonAsync("/api/v1/categories", new { name = "Granted", slug = UniqueSlug("granted") })).StatusCode);

        await Admin.PutAsJsonAsync($"/api/v1/users/{id}/roles", new { roles = Array.Empty<string>() });

        var withoutRole = await SignInAsync(userName, password);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await withoutRole.PostAsJsonAsync("/api/v1/categories", new { name = "Revoked" })).StatusCode);
    }

    [Fact]
    public async Task UnknownRoles_AreRejectedRatherThanIgnored()
    {
        var (_, id, _, _) = await CreateUserAndSignInAsync();

        var response = await Admin.PutAsJsonAsync($"/api/v1/users/{id}/roles",
            new { roles = new[] { "Superuser" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CannotLockThemselvesOut()
    {
        var me = await Admin.GetFromJsonAsync<UserResponse>("/api/v1/auth/me");
        Assert.NotNull(me);

        // A single-admin install would otherwise be one click from being unadministrable.
        Assert.Equal(HttpStatusCode.Conflict,
            (await Admin.PutAsJsonAsync($"/api/v1/users/{me.Id}/roles", new { roles = Array.Empty<string>() })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Admin.PostAsJsonAsync($"/api/v1/users/{me.Id}/disable", new { })).StatusCode);
    }

    [Fact]
    public async Task DisablingAnAccount_StopsItSigningIn()
    {
        var (_, id, userName, password) = await CreateUserAndSignInAsync(AuthPolicies.EditorRole);

        Assert.Equal(HttpStatusCode.NoContent,
            (await Admin.PostAsJsonAsync($"/api/v1/users/{id}/disable", new { })).StatusCode);

        var blocked = await NewClient().PostAsJsonAsync("/api/v1/auth/login", new { email = userName, password });
        Assert.Equal(HttpStatusCode.Locked, blocked.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await Admin.PostAsJsonAsync($"/api/v1/users/{id}/enable", new { })).StatusCode);

        var allowed = await NewClient().PostAsJsonAsync("/api/v1/auth/login", new { email = userName, password });
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task UserList_ShowsRolesAndState()
    {
        var (_, id, userName, _) = await CreateUserAndSignInAsync(AuthPolicies.EditorRole);

        var list = await Admin.GetFromJsonAsync<PagedUsers>($"/api/v1/users?search={userName}&pageSize=100");

        Assert.NotNull(list);
        var user = Assert.Single(list.Items, u => u.Id == id);
        Assert.Contains(AuthPolicies.EditorRole, user.Roles);
        Assert.False(user.IsDisabled);
    }

    // ── Registration is closed ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Registration_IsClosedToTheWorldButOpenToAdmins()
    {
        var body = new
        {
            email = $"outsider-{Guid.NewGuid():N}@example.com",
            password = "Passw0rd!",
            displayName = "Outsider",
        };

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Anonymous.PostAsJsonAsync("/api/v1/auth/register", body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await Admin.PostAsJsonAsync("/api/v1/auth/register", body)).StatusCode);
    }

    // ── Audit ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UserChanges_AreAuditedWithoutLeakingPasswords()
    {
        var (_, id, userName, _) = await CreateUserAndSignInAsync(AuthPolicies.EditorRole);
        await Admin.PostAsJsonAsync($"/api/v1/users/{id}/reset-password", new { newPassword = "Secret!Pass9" });
        await Admin.PutAsJsonAsync($"/api/v1/users/{id}/roles", new { roles = Array.Empty<string>() });

        var audit = await Admin.GetFromJsonAsync<PagedAudit>($"/api/v1/audit?entityType=User&pageSize=100");
        Assert.NotNull(audit);

        var mine = audit.Items.Where(e => e.EntityId == id).ToList();
        Assert.Contains(mine, e => e.Action == "Created");
        Assert.Contains(mine, e => e.Action == "PasswordReset");
        Assert.Contains(mine, e => e.Action == "RolesChanged");
        Assert.Contains(mine, e => e.EntityName == userName);

        // The trail must never carry credentials, in any field.
        foreach (var entry in audit.Items)
        {
            Assert.DoesNotContain("Secret!Pass9", entry.Summary ?? string.Empty);
            Assert.DoesNotContain("Initial!Pass1", entry.Summary ?? string.Empty);
            Assert.DoesNotContain("Chosen!Pass1", entry.Summary ?? string.Empty);
        }
    }

    private sealed record PagedUsers(List<ManagedUser> Items, int Page, int PageSize, int TotalCount);

    private sealed record PagedAudit(List<AuditEntryResponse> Items, int Page, int PageSize, int TotalCount);
}
