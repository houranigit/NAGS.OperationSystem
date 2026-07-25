using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Application.Authorization;
using Identity.Application.Features.Auth;
using Identity.Application.Features.Users;
using Identity.Domain.Authorization;
using Identity.Domain.Roles;
using Identity.Domain.Sessions;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Identity.Infrastructure.UnitTests.Users;

public sealed class PositiveUserActionAuthorizationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(PositiveUserAction.Unlock)]
    [InlineData(PositiveUserAction.RestoreAccess)]
    [InlineData(PositiveUserAction.ResendInvitation)]
    [InlineData(PositiveUserAction.ResetMfa)]
    public async Task Positive_action_revalidates_the_actors_live_permission(
        PositiveUserAction action)
    {
        await using var db = CreateDb();
        var requiredPermission = PermissionFor(action);
        var seed = await SeedAsync(
            db,
            action,
            liveActorPermissions: [IdentityPermissions.Users.View]);
        var originalInvitation = seed.Target.InvitationToken;
        var originalSecurityStamp = seed.Target.SecurityStamp;

        var result = await HandleAsync(
            action,
            db,
            new TestUserContext(seed.Actor.Id, [requiredPermission]),
            new NoopInvitationNotifier(),
            seed.Target.Id);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.AccessManagementForbidden");

        switch (action)
        {
            case PositiveUserAction.Unlock:
                seed.Target.IsLockedOut(Now).ShouldBeTrue();
                break;
            case PositiveUserAction.RestoreAccess:
                seed.Target.Status.ShouldBe(UserStatus.Suspended);
                break;
            case PositiveUserAction.ResendInvitation:
                seed.Target.InvitationToken.ShouldBe(originalInvitation);
                break;
            case PositiveUserAction.ResetMfa:
                seed.Target.MfaEnabled.ShouldBeTrue();
                seed.Target.SecurityStamp.ShouldBe(originalSecurityStamp);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    [Theory]
    [InlineData(PositiveUserAction.Unlock)]
    [InlineData(PositiveUserAction.RestoreAccess)]
    [InlineData(PositiveUserAction.ResendInvitation)]
    [InlineData(PositiveUserAction.ResetMfa)]
    public async Task Positive_action_maps_access_lock_concurrency_to_stale(
        PositiveUserAction action)
    {
        await using var inner = CreateDb();
        var db = new ConcurrencyOnLockDbContext(inner);
        var requiredPermission = PermissionFor(action);

        var result = await HandleAsync(
            action,
            db,
            new TestUserContext(Guid.NewGuid(), [requiredPermission]),
            new NoopInvitationNotifier(),
            Guid.NewGuid());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ConcurrencyErrors.Stale);
    }

    [Theory]
    [InlineData(PositiveUserAction.RestoreAccess)]
    [InlineData(PositiveUserAction.ResendInvitation)]
    public async Task Invitation_queue_failure_does_not_persist_the_new_access_state_or_token(
        PositiveUserAction action)
    {
        var options = CreateOptions();
        Guid targetId;
        string? originalInvitation;

        await using (var db = new IdentityDbContext(options))
        {
            var requiredPermission = PermissionFor(action);
            var seed = await SeedAsync(db, action, [requiredPermission]);
            targetId = seed.Target.Id;
            originalInvitation = seed.Target.InvitationToken;

            var result = await HandleAsync(
                action,
                db,
                new TestUserContext(seed.Actor.Id, [requiredPermission]),
                new ThrowingInvitationNotifier(),
                seed.Target.Id);

            result.IsFailure.ShouldBeTrue();
            result.Error.Code.ShouldBe("Identity.User.InvitationDeliveryFailed");
        }

        await using var verificationDb = new IdentityDbContext(options);
        var persisted = await verificationDb.Users.SingleAsync(user => user.Id == targetId);
        persisted.InvitationToken.ShouldBe(originalInvitation);
        persisted.Status.ShouldBe(
            action == PositiveUserAction.RestoreAccess
                ? UserStatus.Suspended
                : UserStatus.Invited);
    }

    private static async Task<Seed> SeedAsync(
        IdentityDbContext db,
        PositiveUserAction action,
        IReadOnlyList<string> liveActorPermissions)
    {
        var actorRole = Role.Create(
            $"Positive action actor-{Guid.NewGuid():N}",
            null,
            liveActorPermissions,
            UserType.SystemAdministrator,
            Now).Value;
        var targetRole = Role.Create(
            $"Positive action target-{Guid.NewGuid():N}",
            null,
            [],
            UserType.SystemAdministrator,
            Now).Value;
        var actor = CreateActiveAdministrator("positive-action-actor", actorRole.Id);
        var target = action is PositiveUserAction.RestoreAccess or PositiveUserAction.ResendInvitation
            ? User.Invite(
                Email.Create($"positive-action-target-{Guid.NewGuid():N}@nags.sa").Value,
                "Positive Action Target",
                targetRole.Id,
                "original-invitation-hash",
                Now.AddHours(24),
                Now).Value
            : CreateActiveAdministrator("positive-action-target", targetRole.Id);

        switch (action)
        {
            case PositiveUserAction.Unlock:
                target.Lock(Now.AddMinutes(-1)).IsSuccess.ShouldBeTrue();
                break;
            case PositiveUserAction.RestoreAccess:
                target.Suspend(Now.AddMinutes(-1)).IsSuccess.ShouldBeTrue();
                break;
            case PositiveUserAction.ResendInvitation:
                break;
            case PositiveUserAction.ResetMfa:
                target.BeginMfaEnrollment("protected-mfa-secret", Now.AddMinutes(-2))
                    .IsSuccess.ShouldBeTrue();
                target.ConfirmMfaEnrollment(["recovery-code-hash"], Now.AddMinutes(-1))
                    .IsSuccess.ShouldBeTrue();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }

        db.Roles.AddRange(actorRole, targetRole);
        db.Users.AddRange(actor, target);
        await db.SaveChangesAsync();
        return new Seed(actor, target);
    }

    private static User CreateActiveAdministrator(string prefix, Guid roleId) =>
        User.CreateActive(
            Email.Create($"{prefix}-{Guid.NewGuid():N}@nags.sa").Value,
            "Administrator",
            roleId,
            "password-hash",
            Now).Value;

    private static Task<Result> HandleAsync(
        PositiveUserAction action,
        IIdentityDbContext db,
        IUserContext userContext,
        IInvitationNotifier invitationNotifier,
        Guid targetId) =>
        action switch
        {
            PositiveUserAction.Unlock =>
                new UnlockUserCommandHandler(
                        db,
                        userContext,
                        Registry(),
                        new FixedTimeProvider(Now))
                    .Handle(new UnlockUserCommand(targetId), CancellationToken.None),
            PositiveUserAction.RestoreAccess =>
                new RestoreAccessCommandHandler(
                        db,
                        userContext,
                        Registry(),
                        invitationNotifier,
                        new TestTokenService(),
                        new FixedTimeProvider(Now),
                        Options.Create(new IdentityModuleOptions()),
                        NullLogger<RestoreAccessCommandHandler>.Instance)
                    .Handle(new RestoreAccessCommand(targetId), CancellationToken.None),
            PositiveUserAction.ResendInvitation =>
                new ResendInvitationCommandHandler(
                        db,
                        userContext,
                        Registry(),
                        invitationNotifier,
                        new TestTokenService(),
                        new FixedTimeProvider(Now),
                        Options.Create(new IdentityModuleOptions()),
                        NullLogger<ResendInvitationCommandHandler>.Instance)
                    .Handle(new ResendInvitationCommand(targetId), CancellationToken.None),
            PositiveUserAction.ResetMfa =>
                new ResetUserMfaCommandHandler(
                        db,
                        userContext,
                        Registry(),
                        new FixedTimeProvider(Now))
                    .Handle(new ResetUserMfaCommand(targetId), CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

    private static string PermissionFor(PositiveUserAction action) =>
        action switch
        {
            PositiveUserAction.Unlock => IdentityPermissions.Users.Unlock,
            PositiveUserAction.RestoreAccess => IdentityPermissions.Users.RestoreAccess,
            PositiveUserAction.ResendInvitation => IdentityPermissions.Users.Invite,
            PositiveUserAction.ResetMfa => IdentityPermissions.Users.ResetMfa,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

    private static PermissionRegistry Registry() =>
        new([new IdentityPermissionCatalog()]);

    private static IdentityDbContext CreateDb() =>
        new(CreateOptions());

    private static DbContextOptions<IdentityDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"identity-positive-actions-{Guid.NewGuid():N}")
            .Options;

    public enum PositiveUserAction
    {
        Unlock,
        RestoreAccess,
        ResendInvitation,
        ResetMfa
    }

    private sealed record Seed(User Actor, User Target);

    private sealed class TestUserContext(
        Guid userId,
        IReadOnlyList<string> permissions) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => userId;
        public UserType? UserType => BuildingBlocks.Contracts.Authorization.UserType.SystemAdministrator;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => permissions.Contains(permission);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NoopInvitationNotifier : IInvitationNotifier
    {
        public Task SendInvitationAsync(
            string email,
            string displayName,
            Guid userId,
            string invitationToken,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ThrowingInvitationNotifier : IInvitationNotifier
    {
        public Task SendInvitationAsync(
            string email,
            string displayName,
            Guid userId,
            string invitationToken,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Invitation queue unavailable.");
    }

    private sealed class TestTokenService : ITokenService
    {
        public AccessToken CreateAccessToken(
            User user,
            IReadOnlyCollection<string> permissions,
            Guid sessionId) =>
            throw new NotSupportedException();

        public RefreshToken CreateRefreshToken() => throw new NotSupportedException();

        public string HashRefreshToken(string rawToken) => throw new NotSupportedException();

        public SecureToken CreateSecureToken() =>
            new("new-invitation-token", "new-invitation-hash");

        public string HashToken(string rawToken) => throw new NotSupportedException();

        public string CreateMfaChallengeToken(User user) => throw new NotSupportedException();

        public MfaChallenge? ValidateMfaChallengeToken(string token) =>
            throw new NotSupportedException();
    }

    private sealed class ConcurrencyOnLockDbContext(IIdentityDbContext inner)
        : IIdentityDbContext
    {
        public DbSet<User> Users => inner.Users;
        public DbSet<Role> Roles => inner.Roles;
        public DbSet<UserSession> Sessions => inner.Sessions;
        public DbSet<OutboxMessage> OutboxMessages => inner.OutboxMessages;
        public DbSet<InboxMessage> InboxMessages => inner.InboxMessages;

        public Task<IIdentityTransaction> BeginAccessManagementTransactionAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromException<IIdentityTransaction>(
                new DbUpdateConcurrencyException("Simulated access-lock contention."));

        public Task<IIdentityTransaction> BeginSessionFamilyTransactionAsync(
            Guid familyId,
            CancellationToken cancellationToken = default) =>
            inner.BeginSessionFamilyTransactionAsync(familyId, cancellationToken);

        public Task AcquireSessionFamilyLockAsync(
            Guid familyId,
            CancellationToken cancellationToken = default) =>
            inner.AcquireSessionFamilyLockAsync(familyId, cancellationToken);

        public void SetOriginalRowVersion<TEntity>(TEntity entity, byte[] rowVersion)
            where TEntity : class =>
            inner.SetOriginalRowVersion(entity, rowVersion);

        public Task ReloadAsync<TEntity>(
            TEntity entity,
            CancellationToken cancellationToken = default)
            where TEntity : class =>
            inner.ReloadAsync(entity, cancellationToken);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            inner.SaveChangesAsync(cancellationToken);
    }
}
