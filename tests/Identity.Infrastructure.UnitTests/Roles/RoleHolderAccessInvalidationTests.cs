using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Contracts.Authorization;
using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Application.Authorization;
using Identity.Application.Features.Roles;
using Identity.Domain.Authorization;
using Identity.Domain.Roles;
using Identity.Domain.Sessions;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Identity.Infrastructure.UnitTests.Roles;

public sealed class RoleHolderAccessInvalidationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 24, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Permission_change_rotates_and_redelivers_invitations_for_role_holders()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db, activeTarget: false);
        var oldStamp = seed.Target.SecurityStamp;
        var notifier = new RecordingInvitationNotifier();

        var result = await Handler(db, seed, notifier).Handle(
            new UpdateRolePermissionsCommand(
                seed.TargetRole.Id,
                [IdentityPermissions.Users.View]),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        seed.Target.SecurityStamp.ShouldNotBe(oldStamp);
        seed.Target.InvitationToken.ShouldBe(TestTokenService.ReplacementHash);
        seed.Target.ValidateInvitation("old-invitation-hash", Now.AddMinutes(1))
            .IsFailure.ShouldBeTrue();
        seed.Target.ValidateInvitation(TestTokenService.ReplacementHash, Now.AddMinutes(1))
            .IsSuccess.ShouldBeTrue();
        notifier.RawTokens.ShouldBe([TestTokenService.ReplacementValue]);
    }

    [Fact]
    public async Task Permission_change_clears_pending_reset_and_revokes_active_sessions()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db, activeTarget: true);
        seed.Target.RequestPasswordReset(
            "old-reset-hash",
            Now.AddHours(1),
            Now).IsSuccess.ShouldBeTrue();
        var session = UserSession.Issue(
            seed.Target.Id,
            seed.Target.SecurityStamp,
            "refresh-hash",
            Now.AddDays(1),
            Now).Value;
        var oldStamp = seed.Target.SecurityStamp;
        db.Sessions.Add(session);
        await db.SaveChangesAsync();

        var result = await Handler(
                db,
                seed,
                new RecordingInvitationNotifier())
            .Handle(
                new UpdateRolePermissionsCommand(
                    seed.TargetRole.Id,
                    [IdentityPermissions.Users.View]),
                CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        seed.Target.PasswordResetToken.ShouldBeNull();
        seed.Target.PasswordResetExpiresAtUtc.ShouldBeNull();
        seed.Target.SecurityStamp.ShouldNotBe(oldStamp);
        session.RevokedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task Unchanged_permission_set_does_not_rotate_credentials_or_send_email()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db, activeTarget: false);
        var oldStamp = seed.Target.SecurityStamp;
        var oldInvitation = seed.Target.InvitationToken;
        var notifier = new RecordingInvitationNotifier();

        var result = await Handler(db, seed, notifier).Handle(
            new UpdateRolePermissionsCommand(
                seed.TargetRole.Id,
                [IdentityPermissions.Roles.View]),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        seed.Target.SecurityStamp.ShouldBe(oldStamp);
        seed.Target.InvitationToken.ShouldBe(oldInvitation);
        notifier.RawTokens.ShouldBeEmpty();
    }

    [Fact]
    public async Task Invitation_queue_failure_does_not_persist_role_or_credential_changes()
    {
        var databaseName = $"identity-role-credential-rollback-{Guid.NewGuid():N}";
        await using (var db = CreateDb(databaseName))
        {
            var seed = await SeedAsync(db, activeTarget: false);

            var result = await Handler(
                    db,
                    seed,
                    new ThrowingInvitationNotifier())
                .Handle(
                    new UpdateRolePermissionsCommand(
                        seed.TargetRole.Id,
                        [IdentityPermissions.Users.View]),
                    CancellationToken.None);

            result.IsFailure.ShouldBeTrue();
            result.Error.Code.ShouldBe("Identity.User.InvitationDeliveryFailed");
        }

        await using var verification = CreateDb(databaseName);
        var persistedRole = await verification.Roles.SingleAsync(
            role => !role.IsSystem && role.Name == "Target role");
        var persistedTarget = await verification.Users.SingleAsync(
            user => user.DisplayName == "Target user");
        persistedRole.Permissions.ShouldBe([IdentityPermissions.Roles.View]);
        persistedTarget.InvitationToken.ShouldBe("old-invitation-hash");
    }

    private static UpdateRolePermissionsCommandHandler Handler(
        IdentityDbContext db,
        Seed seed,
        IInvitationNotifier notifier) =>
        new(
            db,
            new TestUserContext(
                seed.Actor.Id,
                [
                    IdentityPermissions.Roles.ManagePermissions,
                    IdentityPermissions.Roles.View,
                    IdentityPermissions.Users.View
                ]),
            Registry(),
            notifier,
            new TestTokenService(),
            new FixedTimeProvider(Now),
            Options.Create(new IdentityModuleOptions
            {
                InvitationExpiryHours = 72
            }),
            NullLogger<UpdateRolePermissionsCommandHandler>.Instance);

    private static async Task<Seed> SeedAsync(
        IdentityDbContext db,
        bool activeTarget)
    {
        var actorPermissions = new[]
        {
            IdentityPermissions.Roles.ManagePermissions,
            IdentityPermissions.Roles.View,
            IdentityPermissions.Users.View
        };
        var actorRole = Role.Create(
            "Actor role",
            null,
            actorPermissions,
            UserType.SystemAdministrator,
            Now).Value;
        var targetRole = Role.Create(
            "Target role",
            null,
            [IdentityPermissions.Roles.View],
            UserType.SystemAdministrator,
            Now).Value;
        var actor = User.CreateActive(
            Email.Create($"actor-{Guid.NewGuid():N}@nags.sa").Value,
            "Actor user",
            actorRole.Id,
            "password-hash",
            Now).Value;
        var target = activeTarget
            ? User.CreateActive(
                Email.Create($"target-{Guid.NewGuid():N}@nags.sa").Value,
                "Target user",
                targetRole.Id,
                "password-hash",
                Now).Value
            : User.Invite(
                Email.Create($"target-{Guid.NewGuid():N}@nags.sa").Value,
                "Target user",
                targetRole.Id,
                "old-invitation-hash",
                Now.AddDays(1),
                Now).Value;

        db.Roles.AddRange(actorRole, targetRole);
        db.Users.AddRange(actor, target);
        await db.SaveChangesAsync();
        return new Seed(actor, target, targetRole);
    }

    private static IdentityDbContext CreateDb(string? databaseName = null) =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(
                databaseName ?? $"identity-role-credentials-{Guid.NewGuid():N}")
            .Options);

    private static PermissionRegistry Registry() =>
        new([new IdentityPermissionCatalog()]);

    private sealed record Seed(User Actor, User Target, Role TargetRole);

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

    private sealed class RecordingInvitationNotifier : IInvitationNotifier
    {
        public List<string> RawTokens { get; } = [];

        public Task SendInvitationAsync(
            string email,
            string displayName,
            Guid userId,
            string invitationToken,
            CancellationToken cancellationToken = default)
        {
            RawTokens.Add(invitationToken);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingInvitationNotifier : IInvitationNotifier
    {
        public Task SendInvitationAsync(
            string email,
            string displayName,
            Guid userId,
            string invitationToken,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Synthetic invitation queue failure.");
    }

    private sealed class TestTokenService : ITokenService
    {
        public const string ReplacementValue = "replacement-invitation";
        public const string ReplacementHash = "replacement-invitation-hash";

        public AccessToken CreateAccessToken(
            User user,
            IReadOnlyCollection<string> permissions,
            Guid sessionId) =>
            throw new NotSupportedException();

        public RefreshToken CreateRefreshToken() =>
            throw new NotSupportedException();

        public string HashRefreshToken(string rawToken) =>
            throw new NotSupportedException();

        public SecureToken CreateSecureToken() =>
            new(ReplacementValue, ReplacementHash);

        public string HashToken(string rawToken) =>
            throw new NotSupportedException();

        public string CreateMfaChallengeToken(User user) =>
            throw new NotSupportedException();

        public MfaChallenge? ValidateMfaChallengeToken(string token) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
