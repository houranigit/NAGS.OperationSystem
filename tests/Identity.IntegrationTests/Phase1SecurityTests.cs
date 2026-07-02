using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Identity.Application;
using Identity.Domain.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Identity.IntegrationTests;

/// <summary>
/// Phase 1 security and audit acceptance coverage: user lifecycle guardrails (self/last-admin
/// protection, suspension blocking sign-in) and the append-only, administrator-only audit trail.
/// </summary>
public class Phase1SecurityTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>
{
    private const string Base = IdentityApiTestData.Base;

    private sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);
    private sealed record EnrollmentResponse(string Secret, string OtpAuthUri);
    private sealed record InvitedResponse(Guid Id, string Email, string DeliveryStatus);
    private sealed record AuditTrailItem(Guid Id, string Module, string EntityType, Guid? EntityId, string Action);
    private sealed record AuditChange(string Field, string? Before, string? After);
    private sealed record AuditTrailDetail(Guid Id, string Action, List<AuditChange> Changes);
    private sealed record PagedAudit(List<AuditTrailItem> Items, long TotalCount);
    private sealed record PagedSessions(List<SessionItem> Items, long TotalCount);
    private sealed record SessionItem(Guid Id, bool IsActive);

    [Fact]
    public async Task Admin_cannot_deactivate_their_own_account()
    {
        var (client, adminId) = await AuthenticatedAdminAsync();

        var response = await client.PostAsync($"{Base}/users/{adminId}/deactivate", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Admin_cannot_assign_their_own_role()
    {
        var (client, adminId) = await AuthenticatedAdminAsync();
        var roleId = await CreateAdminRoleAsync(client, $"SelfAssign-{Guid.NewGuid():N}",
            [IdentityPermissions.Roles.View]);

        var response = await client.PutAsJsonAsync($"{Base}/users/{adminId}/role", new { roleId });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Admin_cannot_modify_permissions_for_their_own_role()
    {
        var admin = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);
        var roleId = await CreateAdminRoleAsync(admin, $"SelfPerm-{Guid.NewGuid():N}",
            [IdentityPermissions.Roles.View, IdentityPermissions.Roles.ManagePermissions]);
        var user = await InviteActivateAndLoginAdminAsync(admin, roleId);

        var response = await user.PutAsJsonAsync($"{Base}/roles/{roleId}/permissions",
            new { permissions = new[] { IdentityPermissions.Roles.View } });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Suspended_user_cannot_sign_in()
    {
        var admin = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);

        // Provision a second admin so suspending it does not hit the last-admin guard.
        var email = $"suspend-{Guid.NewGuid():N}@nags.sa";
        const string password = "Suspend#12345";
        var userId = await InviteAndActivateAdminAsync(admin, email, password);

        var anon = factory.CreateClient();
        (await anon.PostAsJsonAsync($"{Base}/auth/login", new { email, password }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await admin.PostAsync($"{Base}/users/{userId}/suspend", content: null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // After suspension the same credentials are rejected.
        (await anon.PostAsJsonAsync($"{Base}/auth/login", new { email, password }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Restoring access lets the (already activated) account sign in again.
        (await admin.PostAsync($"{Base}/users/{userId}/restore-access", content: null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await anon.PostAsJsonAsync($"{Base}/auth/login", new { email, password }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Locking_user_revokes_sessions_and_blocks_existing_token()
    {
        var admin = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);
        var email = $"lock-{Guid.NewGuid():N}@nags.sa";
        const string password = "Lock#12345";
        var userId = await InviteAndActivateAdminAsync(admin, email, password);

        var user = factory.CreateClient();
        var login = await user.PostAsJsonAsync($"{Base}/auth/login", new { email, password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = await login.Content.ReadFromJsonAsync<TokenResponse>();
        token.ShouldNotBeNull();
        user.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        var before = await admin.GetFromJsonAsync<PagedSessions>($"{Base}/users/{userId}/sessions?activeOnly=true");
        before!.Items.ShouldContain(s => s.IsActive);

        (await admin.PostAsync($"{Base}/users/{userId}/lock", content: null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await admin.GetFromJsonAsync<PagedSessions>($"{Base}/users/{userId}/sessions?activeOnly=true");
        after!.Items.ShouldBeEmpty();
        (await user.GetAsync($"{Base}/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await user.PostAsync($"{Base}/auth/refresh", content: null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Failed_password_lockout_revokes_sessions_and_blocks_existing_token()
    {
        var admin = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);
        var email = $"auto-lock-{Guid.NewGuid():N}@nags.sa";
        const string password = "AutoLock#12345";
        var userId = await InviteAndActivateAdminAsync(admin, email, password);

        var user = factory.CreateClient();
        var login = await user.PostAsJsonAsync($"{Base}/auth/login", new { email, password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = await login.Content.ReadFromJsonAsync<TokenResponse>();
        token.ShouldNotBeNull();
        user.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        var before = await admin.GetFromJsonAsync<PagedSessions>($"{Base}/users/{userId}/sessions?activeOnly=true");
        before!.Items.ShouldContain(s => s.IsActive);

        await using var scope = factory.Services.CreateAsyncScope();
        var attempts = scope.ServiceProvider.GetRequiredService<IOptions<IdentityModuleOptions>>()
            .Value.MaxFailedSignInAttempts;

        var anon = factory.CreateClient();
        for (var i = 0; i < attempts; i++)
        {
            var failed = await anon.PostAsJsonAsync($"{Base}/auth/login", new { email, password = "Wrong#12345" });
            failed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var after = await admin.GetFromJsonAsync<PagedSessions>($"{Base}/users/{userId}/sessions?activeOnly=true");
        after!.Items.ShouldBeEmpty();
        (await user.GetAsync($"{Base}/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anon.PostAsJsonAsync($"{Base}/auth/login", new { email, password }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Audit_trail_records_user_creation_without_secrets_and_is_admin_only()
    {
        var admin = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);

        var email = $"audited-{Guid.NewGuid():N}@nags.sa";
        var userId = await InviteAndActivateAdminAsync(admin, email, "Audited#12345");

        // The automatic-capture interceptor enqueues audit events to the outbox; drain it.
        await DrainAsync();

        var trails = await admin.GetFromJsonAsync<PagedAudit>(
            $"/api/v1/audit/trails?subjectType=User&subjectId={userId}&pageSize=100");
        trails.ShouldNotBeNull();
        trails!.Items.ShouldContain(t => t.EntityType == "User" && t.Action == "Created");

        // Field-level deltas must never contain secrets.
        var created = trails.Items.First(t => t.Action == "Created");
        var detail = await admin.GetFromJsonAsync<AuditTrailDetail>($"/api/v1/audit/trails/{created.Id}");
        detail!.Changes.ShouldNotContain(c =>
            c.Field.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            c.Field.Contains("hash", StringComparison.OrdinalIgnoreCase) ||
            c.Field.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            c.Field.Contains("securitystamp", StringComparison.OrdinalIgnoreCase));

        // The audit trail is administrator-only.
        var anon = factory.CreateClient();
        (await anon.GetAsync("/api/v1/audit/trails")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<(HttpClient Client, Guid AdminId)> AuthenticatedAdminAsync()
    {
        var client = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);
        var me = await client.GetFromJsonAsync<MeResponse>($"{Base}/me");
        return (client, me!.Id);
    }

    private static async Task<Guid> CreateAdminRoleAsync(HttpClient admin, string name, IReadOnlyList<string> permissions)
    {
        var create = await admin.PostAsJsonAsync($"{Base}/roles",
            new
            {
                name,
                description = (string?)null,
                compatibleUserType = "SystemAdministrator",
                permissions
            });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);

        return await create.Content.ReadFromJsonAsync<Guid>();
    }

    private async Task<HttpClient> InviteActivateAndLoginAdminAsync(HttpClient admin, Guid roleId)
    {
        var email = $"custom-admin-{Guid.NewGuid():N}@nags.sa";
        const string password = "CustomAdmin#12345";

        await InviteAndActivateAdminAsync(admin, email, password, roleId);

        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync($"{Base}/auth/login", new { email, password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = await login.Content.ReadFromJsonAsync<TokenResponse>();
        token.ShouldNotBeNull();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        var enrollmentResponse = await client.PostAsync($"{Base}/auth/mfa/enroll", content: null);
        enrollmentResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var enrollment = await enrollmentResponse.Content.ReadFromJsonAsync<EnrollmentResponse>();
        enrollment.ShouldNotBeNull();

        var confirm = await client.PostAsJsonAsync($"{Base}/auth/mfa/confirm",
            new { code = Totp(enrollment!.Secret) });
        confirm.StatusCode.ShouldBe(HttpStatusCode.OK);

        var refresh = await client.PostAsync($"{Base}/auth/refresh", content: null);
        refresh.StatusCode.ShouldBe(HttpStatusCode.OK);
        token = await refresh.Content.ReadFromJsonAsync<TokenResponse>();
        token.ShouldNotBeNull();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        return client;
    }

    private async Task<Guid> InviteAndActivateAdminAsync(HttpClient admin, string email, string password, Guid? roleId = null)
    {
        var invite = await admin.PostAsJsonAsync($"{Base}/users/invite",
            new { email, displayName = "Audited User", roleId });
        invite.StatusCode.ShouldBe(HttpStatusCode.Created);
        var invited = await invite.Content.ReadFromJsonAsync<InvitedResponse>();

        var token = await factory.GetInvitationTokenAsync(email);
        token.ShouldNotBeNull();

        (await admin.PostAsJsonAsync($"{Base}/auth/activate",
            new { email, invitationToken = token, newPassword = password }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        return invited!.Id;
    }

    private async Task DrainAsync()
    {
        for (var i = 0; i < 5; i++)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            foreach (var processor in scope.ServiceProvider.GetServices<BuildingBlocks.Infrastructure.Messaging.IOutboxProcessor>())
                await processor.ProcessAsync();
        }
    }

    private sealed record MeResponse(Guid Id, string Email, string DisplayName);

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

        return [.. output];
    }
}
