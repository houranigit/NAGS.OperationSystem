using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using Shouldly;

namespace Identity.IntegrationTests;

public class SessionApiTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>
{
    private const string Base = "/api/v1/identity";

    private sealed record MeResponse(Guid Id, string Email, string DisplayName, Guid RoleId, string RoleName, List<string> Permissions);
    private sealed record PagedList<T>(List<T> Items, int Page, int PageSize, long TotalCount);
    private sealed record SessionItem(Guid Id, Guid UserId, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc, DateTimeOffset? RevokedAtUtc, bool IsActive, bool IsCurrent, string? CreatedByIp, string? UserAgent);

    private static Task<string> LoginAsAdminAsync(HttpClient client, IdentityApiFactory factory)
    {
        return IdentityApiTestData.LoginAsAdminAsync(client, factory);
    }

    [Fact]
    public async Task Login_creates_a_session_visible_in_my_sessions_and_marked_current()
    {
        var client = factory.CreateClient();
        var token = await LoginAsAdminAsync(client, factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var sessions = await client.GetFromJsonAsync<PagedList<SessionItem>>($"{Base}/me/sessions");

        sessions!.Items.Count.ShouldBeGreaterThanOrEqualTo(1);
        sessions.Items.ShouldContain(s => s.IsCurrent && s.IsActive);
    }

    [Fact]
    public async Task Revoke_other_sessions_keeps_current_and_revokes_the_rest()
    {
        // Two independent logins => two sessions for the same admin user.
        var primary = factory.CreateClient();
        var secondary = factory.CreateClient();

        var primaryToken = await LoginAsAdminAsync(primary, factory);
        await LoginAsAdminAsync(secondary, factory);

        primary.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", primaryToken);

        var before = await primary.GetFromJsonAsync<PagedList<SessionItem>>($"{Base}/me/sessions");
        before!.Items.Count(s => s.IsActive).ShouldBeGreaterThanOrEqualTo(2);

        var revoke = await primary.PostAsync($"{Base}/me/sessions/revoke-others", null);
        revoke.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await primary.GetFromJsonAsync<PagedList<SessionItem>>($"{Base}/me/sessions");
        after!.Items.Count(s => s.IsActive).ShouldBe(1);
        after.Items.Single(s => s.IsActive).IsCurrent.ShouldBeTrue();
    }

    [Fact]
    public async Task Admin_can_list_and_revoke_all_sessions_for_a_user()
    {
        var admin = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);

        // Use a separate target user so revoking all of THEIR sessions does not invalidate the
        // admin's own (now session-bound) access token.
        var email = $"sessions-{Guid.NewGuid():N}@nags.sa";
        const string password = "Sessions#12345";

        var invite = await admin.PostAsJsonAsync($"{Base}/users/invite",
            new { email, displayName = "Session User" });
        invite.StatusCode.ShouldBe(HttpStatusCode.Created);
        var invited = await invite.Content.ReadFromJsonAsync<InvitedItem>();

        var invitationToken = await factory.GetInvitationTokenAsync(email);
        invitationToken.ShouldNotBeNull();
        (await admin.PostAsJsonAsync($"{Base}/auth/activate", new { email, invitationToken, newPassword = password }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var target = factory.CreateClient();
        await target.PostAsJsonAsync($"{Base}/auth/login", new { email, password });

        var sessions = await admin.GetFromJsonAsync<PagedList<SessionItem>>($"{Base}/users/{invited!.Id}/sessions");
        sessions!.Items.ShouldNotBeEmpty();

        var revoke = await admin.PostAsync($"{Base}/users/{invited.Id}/sessions/revoke-all", null);
        revoke.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var active = await admin.GetFromJsonAsync<PagedList<SessionItem>>($"{Base}/users/{invited.Id}/sessions?activeOnly=true");
        active!.Items.ShouldBeEmpty();
    }

    private sealed record InvitedItem(Guid Id, string Email, string DeliveryStatus);

    [Fact]
    public async Task Sessions_for_unknown_user_returns_404()
    {
        var admin = factory.CreateClient();
        var adminToken = await LoginAsAdminAsync(admin, factory);
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await admin.GetAsync($"{Base}/users/{Guid.NewGuid()}/sessions");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task My_sessions_without_token_returns_401()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync($"{Base}/me/sessions");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
