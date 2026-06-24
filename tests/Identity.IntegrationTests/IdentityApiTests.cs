using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using Shouldly;

namespace Identity.IntegrationTests;

public class IdentityApiTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>
{
    private const string Base = IdentityApiTestData.Base;

    private sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);
    private sealed record InvitedResponse(Guid Id, string Email, string DeliveryStatus);
    private sealed record PagedList<T>(List<T> Items, int Page, int PageSize, long TotalCount);
    private sealed record RoleItem(Guid Id, string Name);

    [Fact]
    public async Task Login_with_valid_admin_credentials_returns_access_token()
    {
        var client = factory.CreateClient();

        var token = await IdentityApiTestData.LoginAsAdminAsync(client);

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
            new { name = roleName, description = "test", permissions = new[] { "identity.roles.view" } });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);

        var list = await client.GetFromJsonAsync<PagedList<RoleItem>>($"{Base}/roles?pageSize=100");
        list!.Items.ShouldContain(r => r.Name == roleName);
    }

    [Fact]
    public async Task User_without_required_permission_gets_403()
    {
        var client = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);

        // A role with no permissions (admin-compatible, so it can be assigned to a created admin).
        var roleName = $"NoPerms-{Guid.NewGuid():N}";
        var createRole = await client.PostAsJsonAsync($"{Base}/roles",
            new { name = roleName, description = (string?)null, permissions = Array.Empty<string>() });
        var roleId = await createRole.Content.ReadFromJsonAsync<Guid>();

        // Direct creation always makes a full administrator; invite + activate, then demote to the
        // empty role so we can prove permission enforcement.
        var email = $"limited-{Guid.NewGuid():N}@nags.sa";
        var invite = await client.PostAsJsonAsync($"{Base}/users/invite",
            new { email, displayName = "Limited User" });
        var invited = await invite.Content.ReadFromJsonAsync<InvitedResponse>();
        invited.ShouldNotBeNull();

        var invitationToken = await factory.GetInvitationTokenAsync(email);
        invitationToken.ShouldNotBeNull();

        var activate = await client.PostAsJsonAsync($"{Base}/auth/activate",
            new { email, invitationToken, newPassword = "Limited#12345" });
        activate.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.PutAsJsonAsync($"{Base}/users/{invited!.Id}/role", new { roleId }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

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
    public async Task Seeded_demo_data_supports_multi_page_users_and_roles_lists()
    {
        var client = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);

        var rolesBefore = await client.GetFromJsonAsync<PagedList<RoleItem>>($"{Base}/roles?page=1&pageSize=1");
        var usersBefore = await client.GetFromJsonAsync<PagedList<UserItem>>($"{Base}/users?page=1&pageSize=1");

        await IdentityApiTestData.SeedDemoRolesAsync(client);
        var userRoleId = await IdentityApiTestData.EnsureDemoUserRoleAsync(client);
        await IdentityApiTestData.SeedDemoUsersAsync(factory, client, userRoleId);

        const int pageSize = 20;
        var expectedRoleTotal = rolesBefore!.TotalCount + IdentityApiTestData.DemoRoleCount + 1; // Shared demo user role.
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
}
