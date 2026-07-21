using System.Text.Json;
using BuildingBlocks.Contracts.Authorization;
using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Application.Features.PortalAccess;
using Identity.Contracts;
using Identity.Domain.Roles;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence;
using MasterData.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Identity.Infrastructure.UnitTests.PortalAccess;

public sealed class PortalAccessDelegationTests
{
    [Fact]
    public async Task Provisioning_allows_a_target_role_within_the_initiators_live_permission_ceiling()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db, ["masterdata.staff-members.view"], ["masterdata.staff-members.view"]);
        var integrationEvent = Request(seed.Initiator.Id, seed.TargetRole.Id);

        await CreateHandler(db).HandleAsync(integrationEvent);

        var provisionedUser = await db.Users.SingleAsync(
            user => user.ExternalReferenceId == integrationEvent.ExternalReferenceId);
        provisionedUser.Status.ShouldBe(UserStatus.Invited);
        provisionedUser.RoleId.ShouldBe(seed.TargetRole.Id);

        var provisioned = await OutboxEventAsync<PortalUserProvisioned>(db);
        provisioned.ExternalReferenceId.ShouldBe(integrationEvent.ExternalReferenceId);
        provisioned.UserId.ShouldBe(provisionedUser.Id);
        (await db.InboxMessages.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Provisioning_rejects_a_target_role_above_the_initiators_live_permission_ceiling_and_is_idempotent()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(
            db,
            ["masterdata.staff-members.view"],
            ["masterdata.staff-members.view", "masterdata.staff-members.update"]);
        var integrationEvent = Request(seed.Initiator.Id, seed.TargetRole.Id);
        var handler = CreateHandler(db);

        await handler.HandleAsync(integrationEvent);
        await handler.HandleAsync(integrationEvent);

        (await db.Users.CountAsync(user => user.ExternalReferenceId == integrationEvent.ExternalReferenceId))
            .ShouldBe(0);
        var failure = await OutboxEventAsync<PortalUserProvisioningFailed>(db);
        failure.ExternalReferenceId.ShouldBe(integrationEvent.ExternalReferenceId);
        failure.Reason.ShouldContain("permission ceiling");
        (await db.OutboxMessages.CountAsync(message => message.Type.Contains(nameof(PortalUserProvisioningFailed))))
            .ShouldBe(1);
        (await db.InboxMessages.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Provisioning_rejects_an_inactive_initiator_with_a_visible_failure()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db, ["masterdata.staff-members.view"], ["masterdata.staff-members.view"]);
        seed.Initiator.Suspend(TimeProvider.System.GetUtcNow()).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();
        var integrationEvent = Request(seed.Initiator.Id, seed.TargetRole.Id);

        await CreateHandler(db).HandleAsync(integrationEvent);

        (await db.Users.CountAsync(user => user.ExternalReferenceId == integrationEvent.ExternalReferenceId))
            .ShouldBe(0);
        var failure = await OutboxEventAsync<PortalUserProvisioningFailed>(db);
        failure.Reason.ShouldContain("does not exist or is not active");
    }

    [Fact]
    public async Task Provisioning_rejects_a_missing_initiator_id_with_a_visible_failure()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db, [], []);
        var integrationEvent = Request(Guid.Empty, seed.TargetRole.Id);

        await CreateHandler(db).HandleAsync(integrationEvent);

        (await db.Users.CountAsync(user => user.ExternalReferenceId == integrationEvent.ExternalReferenceId))
            .ShouldBe(0);
        var failure = await OutboxEventAsync<PortalUserProvisioningFailed>(db);
        failure.Reason.ShouldContain("initiating user id is missing");
    }

    [Fact]
    public async Task Existing_account_reannouncement_remains_idempotent_after_the_initiator_becomes_inactive()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db, ["masterdata.staff-members.view"], ["masterdata.staff-members.view"]);
        var externalReferenceId = Guid.NewGuid();
        var now = TimeProvider.System.GetUtcNow();
        var existing = User.Invite(
            Email.Create($"existing-{Guid.NewGuid():N}@example.com").Value,
            "Existing Portal Staff",
            seed.TargetRole.Id,
            "existing-invitation-hash",
            now.AddHours(24),
            now,
            UserType.StationStaff,
            externalReferenceId).Value;
        db.Users.Add(existing);
        seed.Initiator.Suspend(now).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();
        var integrationEvent = Request(seed.Initiator.Id, seed.TargetRole.Id) with
        {
            ExternalReferenceId = externalReferenceId
        };

        await CreateHandler(db).HandleAsync(integrationEvent);

        var provisioned = await OutboxEventAsync<PortalUserProvisioned>(db);
        provisioned.UserId.ShouldBe(existing.Id);
        (await db.OutboxMessages.CountAsync(message =>
            message.Type.Contains(nameof(PortalUserProvisioningFailed)))).ShouldBe(0);
        (await db.Users.CountAsync(user => user.ExternalReferenceId == externalReferenceId)).ShouldBe(1);
    }

    private static PortalAccessRequestedHandler CreateHandler(IdentityDbContext db) =>
        new(
            db,
            new TestInvitationNotifier(),
            new TestTokenService(),
            TimeProvider.System,
            Options.Create(new IdentityModuleOptions()),
            NullLogger<PortalAccessRequestedHandler>.Instance);

    private static IdentityDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"identity-portal-delegation-{Guid.NewGuid():N}")
            .Options);

    private static async Task<(User Initiator, Role TargetRole)> SeedAsync(
        IdentityDbContext db,
        IReadOnlyList<string> initiatingPermissions,
        IReadOnlyList<string> targetPermissions)
    {
        var now = TimeProvider.System.GetUtcNow();
        var initiatingRole = Role.Create(
            "Portal delegator",
            null,
            initiatingPermissions,
            UserType.SystemAdministrator,
            now).Value;
        var targetRole = Role.Create(
            "Portal staff",
            null,
            targetPermissions,
            UserType.StationStaff,
            now).Value;
        var initiator = User.CreateActive(
            Email.Create($"initiator-{Guid.NewGuid():N}@example.com").Value,
            "Portal Delegator",
            initiatingRole.Id,
            "password-hash",
            now).Value;

        db.Roles.AddRange(initiatingRole, targetRole);
        db.Users.Add(initiator);
        await db.SaveChangesAsync();
        return (initiator, targetRole);
    }

    private static PortalAccessRequested Request(Guid initiatedByUserId, Guid targetRoleId) =>
        new()
        {
            InitiatedByUserId = initiatedByUserId,
            ExternalReferenceId = Guid.NewGuid(),
            UserType = UserType.StationStaff,
            RoleId = targetRoleId,
            Email = $"staff-{Guid.NewGuid():N}@example.com",
            DisplayName = "Portal Staff",
            CorrelationId = Guid.NewGuid()
        };

    private static async Task<TEvent> OutboxEventAsync<TEvent>(IdentityDbContext db)
        where TEvent : class
    {
        var message = await db.OutboxMessages.SingleAsync(item => item.Type.Contains(typeof(TEvent).Name));
        return JsonSerializer.Deserialize<TEvent>(message.Content).ShouldNotBeNull();
    }

    private sealed class TestInvitationNotifier : IInvitationNotifier
    {
        public Task SendInvitationAsync(
            string email,
            string displayName,
            Guid userId,
            string invitationToken,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestTokenService : ITokenService
    {
        public AccessToken CreateAccessToken(User user, IReadOnlyCollection<string> permissions, Guid sessionId) =>
            throw new NotSupportedException();

        public RefreshToken CreateRefreshToken() => throw new NotSupportedException();

        public string HashRefreshToken(string rawToken) => throw new NotSupportedException();

        public SecureToken CreateSecureToken() => new("raw-invitation-token", "invitation-token-hash");

        public string HashToken(string rawToken) => throw new NotSupportedException();

        public string CreateMfaChallengeToken(User user) => throw new NotSupportedException();

        public MfaChallenge? ValidateMfaChallengeToken(string token) => throw new NotSupportedException();
    }
}
