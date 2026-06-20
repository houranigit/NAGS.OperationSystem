using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Identity.Domain.Authorization;
using Shouldly;

namespace Identity.IntegrationTests;

/// <summary>
/// Helpers for seeding predictable demo data through the public Identity API.
/// </summary>
internal static class IdentityApiTestData
{
    public const string Base = "/api/v1/identity";
    public const int DemoRoleCount = 55;
    public const int DemoUserCount = 55;
    public const string DemoUserPassword = "Demo#12345";

    private sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);
    private sealed record InvitedResponse(Guid Id, string Email, Guid InvitationToken);

    public static async Task<HttpClient> CreateAuthenticatedAdminClientAsync(IdentityApiFactory factory)
    {
        var client = factory.CreateClient();
        var token = await LoginAsAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static async Task<string> LoginAsAdminAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync($"{Base}/auth/login",
            new { email = IdentityApiFactory.AdminEmail, password = IdentityApiFactory.AdminPassword });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return token!.AccessToken;
    }

    public static async Task SeedDemoRolesAsync(HttpClient client, int count = DemoRoleCount)
    {
        for (var i = 1; i <= count; i++)
        {
            var name = DemoRoleName(i);
            var create = await client.PostAsJsonAsync($"{Base}/roles",
                new
                {
                    name,
                    description = $"Pagination demo role {i}",
                    permissions = new[] { IdentityPermissions.Roles.View }
                });

            if (create.StatusCode == HttpStatusCode.Conflict)
                continue;

            create.StatusCode.ShouldBe(HttpStatusCode.Created);
        }
    }

    public static async Task<Guid> EnsureDemoUserRoleAsync(HttpClient client)
    {
        var name = "Demo User Role";
        var create = await client.PostAsJsonAsync($"{Base}/roles",
            new
            {
                name,
                description = "Shared role for pagination demo users.",
                permissions = new[] { IdentityPermissions.Users.View }
            });

        if (create.StatusCode == HttpStatusCode.Created)
            return await create.Content.ReadFromJsonAsync<Guid>();

        create.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var list = await client.GetFromJsonAsync<PagedRoles>($"{Base}/roles?pageSize=100&search=Demo%20User%20Role");
        return list!.Items.Single(r => r.Name == name).Id;
    }

    public static async Task SeedDemoUsersAsync(HttpClient client, Guid roleId, int count = DemoUserCount)
    {
        for (var i = 1; i <= count; i++)
        {
            var email = DemoUserEmail(i);
            var displayName = DemoUserDisplayName(i);

            var invite = await client.PostAsJsonAsync($"{Base}/users/invite",
                new { email, displayName, roleId });

            if (invite.StatusCode == HttpStatusCode.Conflict)
                continue;

            invite.StatusCode.ShouldBe(HttpStatusCode.Created);
            var invited = await invite.Content.ReadFromJsonAsync<InvitedResponse>();

            var activate = await client.PostAsJsonAsync($"{Base}/auth/activate",
                new { email, invitationToken = invited!.InvitationToken, newPassword = DemoUserPassword });
            activate.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
    }

    public static string DemoRoleName(int index) => $"Demo Role {index:000}";

    public static string DemoUserEmail(int index) => $"demo-user-{index:000}@nags.sa";

    public static string DemoUserDisplayName(int index) => $"Demo User {index:000}";

    private sealed record PagedRoles(List<RoleItem> Items);
    private sealed record RoleItem(Guid Id, string Name);
}
