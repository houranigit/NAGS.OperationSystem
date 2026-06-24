using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Shouldly;

namespace Identity.IntegrationTests;

/// <summary>
/// Phase 2 MFA coverage: TOTP enrollment + confirmation, two-step sign-in, recovery-code redemption,
/// and administrator MFA reset. Codes are computed locally with the same RFC 6238 algorithm.
/// </summary>
public class MfaTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>
{
    private const string Base = IdentityApiTestData.Base;

    private sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);
    private sealed record EnrollmentResponse(string Secret, string OtpAuthUri);
    private sealed record RecoveryCodesResponse(List<string> RecoveryCodes);
    private sealed record ChallengeResponse(bool MfaRequired, string MfaToken);
    private sealed record MeResponse(Guid Id, bool MfaEnabled);

    [Fact]
    public async Task Admin_can_enroll_confirm_sign_in_with_mfa_recover_and_reset()
    {
        var admin = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);

        // Enroll: obtain the shared secret, then confirm with a freshly computed code.
        var enrollment = await (await admin.PostAsync($"{Base}/auth/mfa/enroll", content: null))
            .Content.ReadFromJsonAsync<EnrollmentResponse>();
        enrollment.ShouldNotBeNull();

        var confirm = await admin.PostAsJsonAsync($"{Base}/auth/mfa/confirm",
            new { code = Totp(enrollment!.Secret) });
        confirm.StatusCode.ShouldBe(HttpStatusCode.OK);
        var recovery = await confirm.Content.ReadFromJsonAsync<RecoveryCodesResponse>();
        recovery!.RecoveryCodes.Count.ShouldBe(10);

        // A first login step now returns an MFA challenge instead of tokens.
        var anon = factory.CreateClient();
        var challenge = await (await anon.PostAsJsonAsync($"{Base}/auth/login",
            new { email = IdentityApiFactory.AdminEmail, password = IdentityApiFactory.AdminPassword }))
            .Content.ReadFromJsonAsync<ChallengeResponse>();
        challenge!.MfaRequired.ShouldBeTrue();
        challenge.MfaToken.ShouldNotBeNullOrWhiteSpace();

        // Completing the second step with a TOTP code yields real tokens.
        var mfaLogin = await anon.PostAsJsonAsync($"{Base}/auth/login/mfa",
            new { mfaToken = challenge.MfaToken, code = Totp(enrollment.Secret) });
        mfaLogin.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = await mfaLogin.Content.ReadFromJsonAsync<TokenResponse>();
        anon.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        var me = await anon.GetFromJsonAsync<MeResponse>($"{Base}/me");
        me!.MfaEnabled.ShouldBeTrue();

        // A recovery code also completes the second step (single-use).
        var anon2 = factory.CreateClient();
        var challenge2 = await (await anon2.PostAsJsonAsync($"{Base}/auth/login",
            new { email = IdentityApiFactory.AdminEmail, password = IdentityApiFactory.AdminPassword }))
            .Content.ReadFromJsonAsync<ChallengeResponse>();
        var recoveryLogin = await anon2.PostAsJsonAsync($"{Base}/auth/login/mfa",
            new { mfaToken = challenge2!.MfaToken, code = recovery.RecoveryCodes[0] });
        recoveryLogin.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Administrator resets MFA; sign-in no longer requires the second step.
        var freshAdmin = factory.CreateClient();
        var freshChallenge = await (await freshAdmin.PostAsJsonAsync($"{Base}/auth/login",
            new { email = IdentityApiFactory.AdminEmail, password = IdentityApiFactory.AdminPassword }))
            .Content.ReadFromJsonAsync<ChallengeResponse>();
        var adminToken = (await (await freshAdmin.PostAsJsonAsync($"{Base}/auth/login/mfa",
            new { mfaToken = freshChallenge!.MfaToken, code = Totp(enrollment.Secret) }))
            .Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;
        freshAdmin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        (await freshAdmin.PostAsync($"{Base}/users/{me.Id}/mfa/reset", content: null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterReset = factory.CreateClient();
        var loginAfterReset = await afterReset.PostAsJsonAsync($"{Base}/auth/login",
            new { email = IdentityApiFactory.AdminEmail, password = IdentityApiFactory.AdminPassword });
        var tokenAfterReset = await loginAfterReset.Content.ReadFromJsonAsync<TokenResponse>();
        tokenAfterReset!.AccessToken.ShouldNotBeNullOrWhiteSpace();
    }

    // --- Local RFC 6238 TOTP (mirrors the server) ---------------------------

    private static string Totp(string base32Secret)
    {
        var key = Base32Decode(base32Secret);
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(counterBytes);

        var hash = HMACSHA1.HashData(key, counterBytes);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            | (hash[offset + 3] & 0xFF);
        return (binary % 1_000_000).ToString().PadLeft(6, '0');
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        input = input.TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>(input.Length * 5 / 8);
        int buffer = 0, bitsLeft = 0;
        foreach (var c in input)
        {
            var value = alphabet.IndexOf(c);
            if (value < 0)
                continue;
            buffer = (buffer << 5) | value;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                output.Add((byte)((buffer >> bitsLeft) & 0xFF));
            }
        }

        return [.. output];
    }
}
