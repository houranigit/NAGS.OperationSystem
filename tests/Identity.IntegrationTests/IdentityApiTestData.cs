using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
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

    private static readonly ConditionalWeakTable<IdentityApiFactory, AdminMfaState> AdminMfaStates = new();

    private sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);
    private sealed record LoginResponse(bool MfaRequired, string? MfaToken, string? AccessToken, DateTimeOffset? ExpiresAtUtc);
    private sealed record EnrollmentResponse(string Secret, string OtpAuthUri);
    private sealed record MeResponse(bool MfaEnabled, bool MfaEnrollmentRequired, List<string> Permissions);

    public static async Task<HttpClient> CreateAuthenticatedAdminClientAsync(IdentityApiFactory factory, bool ensureMfa = true)
    {
        var client = factory.CreateClient();
        var token = await LoginAsAdminAsync(client, factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (ensureMfa)
            await EnsureAdminMfaAsync(factory, client);

        return client;
    }

    public static async Task<string> LoginAsAdminAsync(HttpClient client, IdentityApiFactory? factory = null)
    {
        var response = await client.PostAsJsonAsync($"{Base}/auth/login",
            new { email = IdentityApiFactory.AdminEmail, password = IdentityApiFactory.AdminPassword });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        login.ShouldNotBeNull();

        if (login!.MfaRequired)
            return await CompleteAdminMfaLoginAsync(client, factory, login.MfaToken);

        login.AccessToken.ShouldNotBeNullOrWhiteSpace();
        return login.AccessToken!;
    }

    private static async Task EnsureAdminMfaAsync(IdentityApiFactory factory, HttpClient client)
    {
        var me = await client.GetFromJsonAsync<MeResponse>($"{Base}/me");
        me.ShouldNotBeNull();

        if (me!.MfaEnrollmentRequired)
        {
            var enrollmentResponse = await client.PostAsync($"{Base}/auth/mfa/enroll", content: null);
            enrollmentResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var enrollment = await enrollmentResponse.Content.ReadFromJsonAsync<EnrollmentResponse>();
            enrollment.ShouldNotBeNull();

            AdminMfaStates.GetOrCreateValue(factory).Secret = enrollment!.Secret;

            var confirm = await client.PostAsJsonAsync($"{Base}/auth/mfa/confirm", new { code = Totp(enrollment.Secret) });
            confirm.StatusCode.ShouldBe(HttpStatusCode.OK);

            var refresh = await client.PostAsync($"{Base}/auth/refresh", content: null);
            refresh.StatusCode.ShouldBe(HttpStatusCode.OK);
            var token = await refresh.Content.ReadFromJsonAsync<TokenResponse>();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

            me = await client.GetFromJsonAsync<MeResponse>($"{Base}/me");
        }

        me!.MfaEnabled.ShouldBeTrue();
        me.Permissions.ShouldNotBeEmpty();
    }

    private static async Task<string> CompleteAdminMfaLoginAsync(HttpClient client, IdentityApiFactory? factory, string? mfaToken)
    {
        mfaToken.ShouldNotBeNullOrWhiteSpace();

        if (factory is null || AdminMfaStates.TryGetValue(factory, out var state) is false || string.IsNullOrWhiteSpace(state.Secret))
            throw new InvalidOperationException("The seeded admin has MFA enabled, but the test helper does not know its MFA secret.");

        var response = await client.PostAsJsonAsync($"{Base}/auth/login/mfa", new { mfaToken, code = Totp(state.Secret) });
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
                    compatibleUserType = "SystemAdministrator",
                    permissions = new[] { IdentityPermissions.Roles.View }
                });

            if (create.StatusCode == HttpStatusCode.Conflict)
                continue;

            create.StatusCode.ShouldBe(HttpStatusCode.Created);
        }
    }

    public static async Task SeedDemoUsersAsync(IdentityApiFactory factory, HttpClient client, int count = DemoUserCount)
    {
        for (var i = 1; i <= count; i++)
        {
            var email = DemoUserEmail(i);
            var displayName = DemoUserDisplayName(i);

            var invite = await client.PostAsJsonAsync($"{Base}/users/invite",
                new { email, displayName });

            if (invite.StatusCode == HttpStatusCode.Conflict)
                continue;

            invite.StatusCode.ShouldBe(HttpStatusCode.Created);

            var invitationToken = await factory.GetInvitationTokenAsync(email);
            invitationToken.ShouldNotBeNull();

            var activate = await client.PostAsJsonAsync($"{Base}/auth/activate",
                new { email, invitationToken, newPassword = DemoUserPassword });
            activate.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
    }

    public static string DemoRoleName(int index) => $"Demo Role {index:000}";

    public static string DemoUserEmail(int index) => $"demo-user-{index:000}@nags.sa";

    public static string DemoUserDisplayName(int index) => $"Demo User {index:000}";

    private sealed class AdminMfaState
    {
        public string? Secret { get; set; }
    }

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

        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var value = 0;
        var output = new List<byte>();

        foreach (var c in input.TrimEnd('=').ToUpperInvariant())
        {
            var index = alphabet.IndexOf(c);
            if (index < 0)
                continue;

            value = (value << 5) | index;
            bits += 5;

            if (bits < 8)
                continue;

            output.Add((byte)((value >> (bits - 8)) & 0xFF));
            bits -= 8;
        }

        return output.ToArray();
    }
}
