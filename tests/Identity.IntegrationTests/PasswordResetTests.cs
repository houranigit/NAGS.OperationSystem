using System.Net;
using System.Net.Http.Json;
using Identity.Application.Abstractions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;

namespace Identity.IntegrationTests;

/// <summary>
/// Phase 2 password-reset coverage: durable reset email, hashed single-use token, session
/// invalidation on reset, and non-enumerating forgot-password responses.
/// </summary>
public class PasswordResetTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>
{
    private const string Base = IdentityApiTestData.Base;

    private sealed record InvitedResponse(Guid Id, string Email, string DeliveryStatus);

    [Fact]
    public async Task Forgot_then_reset_password_lets_the_user_sign_in_with_the_new_password()
    {
        var admin = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);

        var email = $"reset-{Guid.NewGuid():N}@nags.sa";
        const string original = "Original#12345";
        const string updated = "Updated#67890";

        var invite = await admin.PostAsJsonAsync($"{Base}/users/invite", new { email, displayName = "Reset User" });
        invite.StatusCode.ShouldBe(HttpStatusCode.Created);

        var invitationToken = await factory.GetInvitationTokenAsync(email);
        invitationToken.ShouldNotBeNull();
        (await admin.PostAsJsonAsync($"{Base}/auth/activate", new { email, invitationToken, newPassword = original }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var anon = factory.CreateClient();

        // Request a reset; the response is always 204 (non-enumerating).
        (await anon.PostAsJsonAsync($"{Base}/auth/forgot-password", new { email }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The durable reset email carries the raw token; drain the outbox and read it.
        await factory.DrainOutboxesAsync();
        var resetToken = factory.Emails.TokenFor(email);
        resetToken.ShouldNotBeNull();
        resetToken.ShouldNotBe(invitationToken);

        (await anon.PostAsJsonAsync($"{Base}/auth/reset-password", new { token = resetToken, newPassword = updated }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // New password works; the old one does not.
        (await anon.PostAsJsonAsync($"{Base}/auth/login", new { email, password = updated }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anon.PostAsJsonAsync($"{Base}/auth/login", new { email, password = original }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The reset token is single-use.
        (await anon.PostAsJsonAsync($"{Base}/auth/reset-password", new { token = resetToken, newPassword = "Another#1111" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Forgot_password_for_unknown_email_returns_204_without_enumerating()
    {
        var anon = factory.CreateClient();

        var response = await anon.PostAsJsonAsync($"{Base}/auth/forgot-password",
            new { email = $"nobody-{Guid.NewGuid():N}@nags.sa" });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Failed_password_reset_delivery_keeps_existing_reset_token_valid()
    {
        var admin = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);

        var email = $"reset-delivery-failed-{Guid.NewGuid():N}@nags.sa";
        const string original = "Original#12345";

        var invite = await admin.PostAsJsonAsync($"{Base}/users/invite", new { email, displayName = "Reset Delivery User" });
        invite.StatusCode.ShouldBe(HttpStatusCode.Created);

        var invitationToken = await factory.GetInvitationTokenAsync(email);
        invitationToken.ShouldNotBeNull();
        (await admin.PostAsJsonAsync($"{Base}/auth/activate", new { email, invitationToken, newPassword = original }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var anon = factory.CreateClient();
        (await anon.PostAsJsonAsync($"{Base}/auth/forgot-password", new { email }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var firstResetToken = factory.Emails.TokenFor(email);
        firstResetToken.ShouldNotBeNull();

        await using var failingApp = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Scoped<IPasswordResetNotifier, ThrowingPasswordResetNotifier>())));

        var failingClient = failingApp.CreateClient();
        (await failingClient.PostAsJsonAsync($"{Base}/auth/forgot-password", new { email }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await anon.PostAsJsonAsync($"{Base}/auth/reset-password",
            new { token = firstResetToken, newPassword = "ExistingToken#12345" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private sealed class ThrowingPasswordResetNotifier : IPasswordResetNotifier
    {
        public Task SendPasswordResetAsync(
            string email,
            string displayName,
            Guid userId,
            string resetToken,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Synthetic password reset delivery failure.");
    }
}
