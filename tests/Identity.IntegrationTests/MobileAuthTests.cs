using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace Identity.IntegrationTests;

public sealed class MobileAuthTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>
{
    private const string Base = IdentityApiTestData.Base;
    private const string Password = "Mobile#12345";

    [Fact]
    public async Task Mobile_logout_revokes_refresh_token_without_bearer_authentication()
    {
        using var admin = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);
        var email = $"mobile-logout-{Guid.NewGuid():N}@nags.sa";

        var invite = await admin.PostAsJsonAsync(
            $"{Base}/users/invite",
            new { email, displayName = "Mobile Logout User" });
        invite.StatusCode.ShouldBe(HttpStatusCode.Created);

        var invitationToken = await factory.GetInvitationTokenAsync(email);
        invitationToken.ShouldNotBeNullOrWhiteSpace();

        var activate = await admin.PostAsJsonAsync(
            $"{Base}/auth/activate",
            new { email, invitationToken, newPassword = Password });
        activate.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var mobile = factory.CreateClient();
        var login = await mobile.PostAsJsonAsync(
            $"{Base}/auth/mobile/login",
            new { email, password = Password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);

        var tokens = await login.Content.ReadFromJsonAsync<MobileTokens>();
        tokens.ShouldNotBeNull();
        tokens!.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        mobile.DefaultRequestHeaders.Authorization.ShouldBeNull();

        var logout = await mobile.PostAsJsonAsync(
            $"{Base}/auth/mobile/logout",
            new { refreshToken = tokens.RefreshToken });
        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var replay = await mobile.PostAsJsonAsync(
            $"{Base}/auth/mobile/refresh",
            new { refreshToken = tokens.RefreshToken });
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Concurrent_mobile_refresh_consumes_a_token_exactly_once()
    {
        using var admin = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);
        var email = $"mobile-single-use-{Guid.NewGuid():N}@nags.sa";
        var invite = await admin.PostAsJsonAsync(
            $"{Base}/users/invite",
            new { email, displayName = "Mobile Single Use User" });
        invite.StatusCode.ShouldBe(HttpStatusCode.Created);

        var invitationToken = await factory.GetInvitationTokenAsync(email);
        var activate = await admin.PostAsJsonAsync(
            $"{Base}/auth/activate",
            new { email, invitationToken, newPassword = Password });
        activate.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var first = factory.CreateClient();
        using var second = factory.CreateClient();
        var login = await first.PostAsJsonAsync(
            $"{Base}/auth/mobile/login",
            new { email, password = Password });
        var tokens = await login.Content.ReadFromJsonAsync<MobileTokens>();
        tokens.ShouldNotBeNull();

        var firstRefresh = first.PostAsJsonAsync(
            $"{Base}/auth/mobile/refresh",
            new { refreshToken = tokens!.RefreshToken });
        var secondRefresh = second.PostAsJsonAsync(
            $"{Base}/auth/mobile/refresh",
            new { refreshToken = tokens.RefreshToken });
        var responses = await Task.WhenAll(firstRefresh, secondRefresh);

        responses.Count(response => response.StatusCode == HttpStatusCode.OK)
            .ShouldBe(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.Unauthorized)
            .ShouldBe(1);

        var winner = responses.Single(response => response.StatusCode == HttpStatusCode.OK);
        var successor = await winner.Content.ReadFromJsonAsync<MobileTokens>();
        successor.ShouldNotBeNull();

        var originalReplay = await first.PostAsJsonAsync(
            $"{Base}/auth/mobile/refresh",
            new { refreshToken = tokens.RefreshToken });
        originalReplay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var successorRefresh = await first.PostAsJsonAsync(
            $"{Base}/auth/mobile/refresh",
            new { refreshToken = successor!.RefreshToken });
        successorRefresh.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Concurrent_web_refresh_does_not_delete_the_winning_shared_cookie()
    {
        using var admin = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);
        var email = $"web-single-use-{Guid.NewGuid():N}@nags.sa";
        var invite = await admin.PostAsJsonAsync(
            $"{Base}/users/invite",
            new { email, displayName = "Web Single Use User" });
        invite.StatusCode.ShouldBe(HttpStatusCode.Created);

        var invitationToken = await factory.GetInvitationTokenAsync(email);
        var activate = await admin.PostAsJsonAsync(
            $"{Base}/auth/activate",
            new { email, invitationToken, newPassword = Password });
        activate.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var web = factory.CreateClient();
        var login = await web.PostAsJsonAsync(
            $"{Base}/auth/login",
            new { email, password = Password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);

        var firstRefresh = web.PostAsync($"{Base}/auth/refresh", content: null);
        var secondRefresh = web.PostAsync($"{Base}/auth/refresh", content: null);
        var responses = await Task.WhenAll(firstRefresh, secondRefresh);

        responses.Count(response => response.StatusCode == HttpStatusCode.OK)
            .ShouldBe(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.Unauthorized)
            .ShouldBe(1);

        // Both requests used predecessor A. The winner set successor B; the later 401 must not
        // emit a deletion that erases B from the tabs' shared cookie jar.
        var nextRefresh = await web.PostAsync($"{Base}/auth/refresh", content: null);
        nextRefresh.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private sealed record MobileTokens(
        string AccessToken,
        DateTimeOffset AccessTokenExpiresAtUtc,
        string RefreshToken,
        DateTimeOffset RefreshTokenExpiresAtUtc);
}
