using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
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
    private sealed record InvitedResponse(Guid Id, string Email, string DeliveryStatus);
    private sealed record AuditTrailItem(Guid Id, string Module, string EntityType, Guid? EntityId, string Action);
    private sealed record AuditChange(string Field, string? Before, string? After);
    private sealed record AuditTrailDetail(Guid Id, string Action, List<AuditChange> Changes);
    private sealed record PagedAudit(List<AuditTrailItem> Items, long TotalCount);

    [Fact]
    public async Task Admin_cannot_deactivate_their_own_account()
    {
        var (client, adminId) = await AuthenticatedAdminAsync();

        var response = await client.PostAsync($"{Base}/users/{adminId}/deactivate", content: null);

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

    private async Task<Guid> InviteAndActivateAdminAsync(HttpClient admin, string email, string password)
    {
        var adminRoleId = await IdentityApiTestData.EnsureDemoUserRoleAsync(admin);
        var invite = await admin.PostAsJsonAsync($"{Base}/users/invite",
            new { email, displayName = "Audited User", roleId = adminRoleId });
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
}
