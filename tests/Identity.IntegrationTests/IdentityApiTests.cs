using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Identity.IntegrationTests;

public class IdentityApiTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>
{
    private const string Base = IdentityApiTestData.Base;

    private sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);
    private sealed record InvitedResponse(Guid Id, string Email, string DeliveryStatus);
    private sealed record PagedList<T>(List<T> Items, int Page, int PageSize, long TotalCount);
    private sealed record RoleItem(Guid Id, string Name);
    private sealed record UserDetailItem(Guid Id, Guid RoleId, string UserType);

    [Fact]
    public async Task Login_with_valid_admin_credentials_returns_access_token()
    {
        var client = factory.CreateClient();

        var token = await IdentityApiTestData.LoginAsAdminAsync(client, factory);

        token.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"{Base}/auth/login",
            new { email = IdentityApiFactory.AdminEmail, password = "wrong" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Roles_without_token_returns_401()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync($"{Base}/roles");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_can_create_and_list_roles()
    {
        var client = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);

        var roleName = $"Role-{Guid.NewGuid():N}";
        var create = await client.PostAsJsonAsync($"{Base}/roles",
            new { name = roleName, description = "test", compatibleUserType = "SystemAdministrator", permissions = new[] { "identity.roles.view" } });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);

        var list = await client.GetFromJsonAsync<PagedList<RoleItem>>($"{Base}/roles?pageSize=100");
        list!.Items.ShouldContain(r => r.Name == roleName);
    }

    [Fact]
    public async Task Creating_role_without_compatible_user_type_returns_400()
    {
        var client = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);

        var roleName = $"MissingType-{Guid.NewGuid():N}";
        var create = await client.PostAsJsonAsync($"{Base}/roles",
            new { name = roleName, description = "test", permissions = new[] { "identity.roles.view" } });

        create.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Inviting_direct_admin_can_select_admin_compatible_role()
    {
        var client = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);

        var roleName = $"InviteRole-{Guid.NewGuid():N}";
        var createRole = await client.PostAsJsonAsync($"{Base}/roles",
            new { name = roleName, description = (string?)null, compatibleUserType = "SystemAdministrator", permissions = Array.Empty<string>() });
        createRole.StatusCode.ShouldBe(HttpStatusCode.Created);
        var roleId = await createRole.Content.ReadFromJsonAsync<Guid>();

        var email = $"selected-role-{Guid.NewGuid():N}@nags.sa";
        var invite = await client.PostAsJsonAsync($"{Base}/users/invite",
            new { email, displayName = "Selected Role User", roleId });
        invite.StatusCode.ShouldBe(HttpStatusCode.Created);
        var invited = await invite.Content.ReadFromJsonAsync<InvitedResponse>();
        invited.ShouldNotBeNull();

        var detail = await client.GetFromJsonAsync<UserDetailItem>($"{Base}/users/{invited!.Id}");
        detail!.RoleId.ShouldBe(roleId);
        detail.UserType.ShouldBe("SystemAdministrator");
    }

    [Fact]
    public async Task Inviting_direct_admin_rejects_non_admin_compatible_role()
    {
        var client = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);

        var roleName = $"StationRole-{Guid.NewGuid():N}";
        var createRole = await client.PostAsJsonAsync($"{Base}/roles",
            new { name = roleName, description = (string?)null, compatibleUserType = "StationStaff", permissions = Array.Empty<string>() });
        createRole.StatusCode.ShouldBe(HttpStatusCode.Created);
        var roleId = await createRole.Content.ReadFromJsonAsync<Guid>();

        var invite = await client.PostAsJsonAsync($"{Base}/users/invite",
            new { email = $"bad-role-{Guid.NewGuid():N}@nags.sa", displayName = "Bad Role User", roleId });

        invite.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task User_without_required_permission_gets_403()
    {
        var client = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);

        // A role with no permissions (admin-compatible, so it can be assigned to a created admin).
        var roleName = $"NoPerms-{Guid.NewGuid():N}";
        var createRole = await client.PostAsJsonAsync($"{Base}/roles",
            new { name = roleName, description = (string?)null, compatibleUserType = "SystemAdministrator", permissions = Array.Empty<string>() });
        var roleId = await createRole.Content.ReadFromJsonAsync<Guid>();

        // Invite directly into the empty administrator role so we can prove permission enforcement
        // without giving the account full access first.
        var email = $"limited-{Guid.NewGuid():N}@nags.sa";
        var invite = await client.PostAsJsonAsync($"{Base}/users/invite",
            new { email, displayName = "Limited User", roleId });
        var invited = await invite.Content.ReadFromJsonAsync<InvitedResponse>();
        invited.ShouldNotBeNull();

        var invitationToken = await factory.GetInvitationTokenAsync(email);
        invitationToken.ShouldNotBeNull();

        var activate = await client.PostAsJsonAsync($"{Base}/auth/activate",
            new { email, invitationToken, newPassword = "Limited#12345" });
        activate.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Log in as the now-limited user and attempt a permission-gated endpoint.
        var limitedClient = factory.CreateClient();
        var login = await limitedClient.PostAsJsonAsync($"{Base}/auth/login",
            new { email, password = "Limited#12345" });
        var limitedToken = (await login.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;
        limitedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", limitedToken);

        var forbidden = await limitedClient.GetAsync($"{Base}/roles");
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Released_login_email_can_activate_and_login_as_new_invited_user()
    {
        var client = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);
        var email = $"released-login-{Guid.NewGuid():N}@nags.sa";

        var firstInvite = await client.PostAsJsonAsync($"{Base}/users/invite",
            new { email, displayName = "Released Login Original" });
        firstInvite.StatusCode.ShouldBe(HttpStatusCode.Created);
        var first = await firstInvite.Content.ReadFromJsonAsync<InvitedResponse>();
        first.ShouldNotBeNull();

        var firstToken = await factory.GetInvitationTokenAsync(email);
        firstToken.ShouldNotBeNull();

        var firstActivate = await client.PostAsJsonAsync($"{Base}/auth/activate",
            new { email, invitationToken = firstToken, newPassword = "Original#12345" });
        firstActivate.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await ReleaseLoginEmailAsync(first!.Id);

        var secondInvite = await client.PostAsJsonAsync($"{Base}/users/invite",
            new { email, displayName = "Released Login Replacement" });
        secondInvite.StatusCode.ShouldBe(HttpStatusCode.Created);
        var second = await secondInvite.Content.ReadFromJsonAsync<InvitedResponse>();
        second.ShouldNotBeNull();
        second!.Id.ShouldNotBe(first.Id);

        var secondToken = await factory.GetInvitationTokenAsync(email);
        secondToken.ShouldNotBeNull();

        var secondActivate = await client.PostAsJsonAsync($"{Base}/auth/activate",
            new { email, invitationToken = secondToken, newPassword = "Replacement#12345" });
        secondActivate.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var loginClient = factory.CreateClient();
        var login = await loginClient.PostAsJsonAsync($"{Base}/auth/login",
            new { email, password = "Replacement#12345" });

        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = await login.Content.ReadFromJsonAsync<TokenResponse>();
        token!.AccessToken.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Seeded_demo_data_supports_multi_page_users_and_roles_lists()
    {
        var client = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);

        var rolesBefore = await client.GetFromJsonAsync<PagedList<RoleItem>>($"{Base}/roles?page=1&pageSize=1");
        var usersBefore = await client.GetFromJsonAsync<PagedList<UserItem>>($"{Base}/users?page=1&pageSize=1");

        await IdentityApiTestData.SeedDemoRolesAsync(client);
        await IdentityApiTestData.SeedDemoUsersAsync(factory, client);

        const int pageSize = 20;
        var expectedRoleTotal = rolesBefore!.TotalCount + IdentityApiTestData.DemoRoleCount;
        var expectedUserTotal = usersBefore!.TotalCount + IdentityApiTestData.DemoUserCount;

        var rolesPage1 = await client.GetFromJsonAsync<PagedList<RoleItem>>($"{Base}/roles?page=1&pageSize={pageSize}");
        rolesPage1!.TotalCount.ShouldBe(expectedRoleTotal);
        rolesPage1.Items.Count.ShouldBe(pageSize);
        rolesPage1.Page.ShouldBe(1);
        rolesPage1.PageSize.ShouldBe(pageSize);

        var rolesPage2 = await client.GetFromJsonAsync<PagedList<RoleItem>>($"{Base}/roles?page=2&pageSize={pageSize}");
        rolesPage2!.TotalCount.ShouldBe(expectedRoleTotal);
        rolesPage2.Items.Count.ShouldBe(pageSize);

        var rolesLastPage = (int)Math.Ceiling(expectedRoleTotal / (double)pageSize);
        var rolesFinalPageSize = (int)(expectedRoleTotal - (pageSize * (rolesLastPage - 1)));
        var rolesPageLast = await client.GetFromJsonAsync<PagedList<RoleItem>>(
            $"{Base}/roles?page={rolesLastPage}&pageSize={pageSize}");
        rolesPageLast!.TotalCount.ShouldBe(expectedRoleTotal);
        rolesPageLast.Items.Count.ShouldBe(rolesFinalPageSize);

        var usersPage1 = await client.GetFromJsonAsync<PagedList<UserItem>>($"{Base}/users?page=1&pageSize={pageSize}");
        usersPage1!.TotalCount.ShouldBe(expectedUserTotal);
        usersPage1.Items.Count.ShouldBe(pageSize);

        var usersPage2 = await client.GetFromJsonAsync<PagedList<UserItem>>($"{Base}/users?page=2&pageSize={pageSize}");
        usersPage2!.TotalCount.ShouldBe(expectedUserTotal);
        usersPage2.Items.Count.ShouldBe(pageSize);

        var usersLastPage = (int)Math.Ceiling(expectedUserTotal / (double)pageSize);
        var usersFinalPageSize = (int)(expectedUserTotal - (pageSize * (usersLastPage - 1)));
        var usersPageLast = await client.GetFromJsonAsync<PagedList<UserItem>>(
            $"{Base}/users?page={usersLastPage}&pageSize={pageSize}");
        usersPageLast!.TotalCount.ShouldBe(expectedUserTotal);
        usersPageLast.Items.Count.ShouldBe(usersFinalPageSize);

        var search = await client.GetFromJsonAsync<PagedList<UserItem>>(
            $"{Base}/users?page=1&pageSize={pageSize}&search=Demo%20User%20042");
        search!.TotalCount.ShouldBe(1);
        search.Items.Single().DisplayName.ShouldBe(IdentityApiTestData.DemoUserDisplayName(42));
    }

    private sealed record UserItem(Guid Id, string Email, string DisplayName);

    private async Task ReleaseLoginEmailAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await db.Users.FirstAsync(u => u.Id == userId);
        user.ReleaseLoginEmail(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }
}
