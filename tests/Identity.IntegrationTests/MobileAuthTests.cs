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

    private sealed record MobileTokens(
        string AccessToken,
        DateTimeOffset AccessTokenExpiresAtUtc,
        string RefreshToken,
        DateTimeOffset RefreshTokenExpiresAtUtc);
}
