using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using Shouldly;

namespace Identity.IntegrationTests;

public class IdentityApiTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>
{
    private const string Base = "/api/v1/identity";

    private sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);
    private sealed record InvitedResponse(Guid Id, string Email, Guid InvitationToken);

    private async Task<string> LoginAsAdminAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync($"{Base}/auth/login",
            new { email = IdentityApiFactory.AdminEmail, password = IdentityApiFactory.AdminPassword });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return token!.AccessToken;
    }

    [Fact]
    public async Task Login_with_valid_admin_credentials_returns_access_token()
    {
        var client = factory.CreateClient();

        var token = await LoginAsAdminAsync(client);

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
        var client = factory.CreateClient();
        var token = await LoginAsAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var roleName = $"Role-{Guid.NewGuid():N}";
        var create = await client.PostAsJsonAsync($"{Base}/roles",
            new { name = roleName, description = "test", permissions = new[] { "identity.roles.view" } });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);

        var list = await client.GetFromJsonAsync<PagedRoles>($"{Base}/roles?pageSize=100");
        list!.Items.ShouldContain(r => r.Name == roleName);
    }

    [Fact]
    public async Task User_without_required_permission_gets_403()
    {
        var client = factory.CreateClient();
        var adminToken = await LoginAsAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // A role with no permissions.
        var roleName = $"NoPerms-{Guid.NewGuid():N}";
        var createRole = await client.PostAsJsonAsync($"{Base}/roles",
            new { name = roleName, description = (string?)null, permissions = Array.Empty<string>() });
        var roleId = await createRole.Content.ReadFromJsonAsync<Guid>();

        // Invite + activate a user in that role.
        var email = $"limited-{Guid.NewGuid():N}@nags.sa";
        var invite = await client.PostAsJsonAsync($"{Base}/users/invite",
            new { email, displayName = "Limited User", roleId });
        var invited = await invite.Content.ReadFromJsonAsync<InvitedResponse>();

        var activate = await client.PostAsJsonAsync($"{Base}/auth/activate",
            new { email, invitationToken = invited!.InvitationToken, newPassword = "Limited#12345" });
        activate.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Log in as the limited user and attempt a permission-gated endpoint.
        var limitedClient = factory.CreateClient();
        var login = await limitedClient.PostAsJsonAsync($"{Base}/auth/login",
            new { email, password = "Limited#12345" });
        var limitedToken = (await login.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;
        limitedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", limitedToken);

        var forbidden = await limitedClient.GetAsync($"{Base}/roles");
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private sealed record PagedRoles(List<RoleItem> Items);
    private sealed record RoleItem(Guid Id, string Name);
}
