using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Contracts.Authorization;
using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Application.Authorization;
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

public sealed class AssignRoleAuthorizationTests
{
    [Fact]
    public async Task Assignment_requires_claimed_assign_role_authority()
    {
        await using var db = CreateDb();
        var handler = Handler(
            db,
            new TestUserContext(Guid.NewGuid(), [IdentityPermissions.Users.View]));

        var result = await handler.Handle(
            new AssignRoleCommand(Guid.NewGuid(), Guid.NewGuid(), [1]),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.AssignRoleForbidden");
    }

    [Fact]
    public async Task Assignment_revalidates_the_live_actor_role()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(
            db,
            actorRolePermissions: [IdentityPermissions.Users.View],
            currentRolePermissions: [],
            targetRolePermissions: [IdentityPermissions.Users.View]);
        var handler = Handler(
            db,
            new TestUserContext(seed.Actor.Id, [
                IdentityPermissions.Users.AssignRole,
                IdentityPermissions.Users.View
            ]));

        var result = await handler.Handle(
            Command(seed.Target, seed.TargetRole),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.AssignRoleForbidden");
        seed.Target.RoleId.ShouldBe(seed.CurrentRole.Id);
    }

    [Fact]
    public async Task Assignment_rejects_a_target_role_above_both_permission_ceilings()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(
            db,
            actorRolePermissions: [
                IdentityPermissions.Users.AssignRole,
                IdentityPermissions.Users.View
            ],
            currentRolePermissions: [],
            targetRolePermissions: [
                IdentityPermissions.Users.View,
                IdentityPermissions.Users.Update
            ]);
        var handler = Handler(
            db,
            new TestUserContext(seed.Actor.Id, [
                IdentityPermissions.Users.AssignRole,
                IdentityPermissions.Users.View
            ]));

        var result = await handler.Handle(
            Command(seed.Target, seed.TargetRole),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.PermissionDelegationForbidden");
        seed.Target.RoleId.ShouldBe(seed.CurrentRole.Id);
    }

    [Fact]
    public async Task Assignment_rejects_managing_a_current_role_above_the_actor_ceiling()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(
            db,
            actorRolePermissions: [
                IdentityPermissions.Users.AssignRole,
                IdentityPermissions.Users.View
            ],
            currentRolePermissions: [
                IdentityPermissions.Users.View,
                IdentityPermissions.Users.Update
            ],
            targetRolePermissions: [IdentityPermissions.Users.View]);
        var handler = Handler(
            db,
            new TestUserContext(seed.Actor.Id, [
                IdentityPermissions.Users.AssignRole,
                IdentityPermissions.Users.View
            ]));

        var result = await handler.Handle(
            Command(seed.Target, seed.TargetRole),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.ManagedRoleForbidden");
    }

    [Fact]
    public async Task Assignment_reports_invalid_current_role_as_user_drift()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(
            db,
            actorRolePermissions: [IdentityPermissions.Users.AssignRole],
            currentRolePermissions: ["identity.retired.permission"],
            targetRolePermissions: []);
        var handler = Handler(
            db,
            new TestUserContext(
                seed.Actor.Id,
                [IdentityPermissions.Users.AssignRole]));

        var result = await handler.Handle(
            Command(seed.Target, seed.TargetRole),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.CurrentRoleInvalid");
    }

    [Fact]
    public async Task Assignment_reports_invalid_selected_role_as_role_drift()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(
            db,
            actorRolePermissions: [
                IdentityPermissions.Users.AssignRole,
                IdentityPermissions.Users.ChangeAccountType
            ],
            currentRolePermissions: [],
            targetRolePermissions: [],
            targetRoleUserType: UserType.ViewerOnly);
        var handler = Handler(
            db,
            new TestUserContext(seed.Actor.Id, [
                IdentityPermissions.Users.AssignRole,
                IdentityPermissions.Users.ChangeAccountType
            ]));

        var result = await handler.Handle(
            Command(seed.Target, seed.TargetRole),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.SelectedRoleInvalid");
    }

    [Fact]
    public async Task Same_type_assignment_changes_role_and_revokes_sessions()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(
            db,
            actorRolePermissions: [
                IdentityPermissions.Users.AssignRole,
                IdentityPermissions.Users.View
            ],
            currentRolePermissions: [],
            targetRolePermissions: [IdentityPermissions.Users.View]);
        var session = UserSession.Issue(
            seed.Target.Id,
            seed.Target.SecurityStamp,
            "refresh-hash",
            DateTimeOffset.UtcNow.AddDays(1),
            DateTimeOffset.UtcNow).Value;
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        var handler = Handler(
            db,
            new TestUserContext(seed.Actor.Id, [
                IdentityPermissions.Users.AssignRole,
                IdentityPermissions.Users.View
            ]));

        var result = await handler.Handle(
            Command(seed.Target, seed.TargetRole),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        seed.Target.RoleId.ShouldBe(seed.TargetRole.Id);
        seed.Target.UserType.ShouldBe(UserType.SystemAdministrator);
        session.RevokedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task Reassigning_the_same_role_is_a_no_op_and_keeps_sessions()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(
            db,
            actorRolePermissions: [IdentityPermissions.Users.AssignRole],
            currentRolePermissions: [],
            targetRolePermissions: []);
        var originalStamp = seed.Target.SecurityStamp;
        var session = UserSession.Issue(
            seed.Target.Id,
            seed.Target.SecurityStamp,
            "same-role-refresh-hash",
            DateTimeOffset.UtcNow.AddDays(1),
            DateTimeOffset.UtcNow).Value;
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        var handler = Handler(
            db,
            new TestUserContext(
                seed.Actor.Id,
                [IdentityPermissions.Users.AssignRole]));

        var result = await handler.Handle(
            Command(seed.Target, seed.CurrentRole),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        seed.Target.SecurityStamp.ShouldBe(originalStamp);
        session.RevokedAtUtc.ShouldBeNull();
    }

    [Fact]
    public async Task Cross_type_assignment_requires_explicit_claimed_authority()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(
            db,
            actorRolePermissions: [
                IdentityPermissions.Users.AssignRole,
                IdentityPermissions.Users.ChangeAccountType,
                IdentityPermissions.Users.View
            ],
            currentRolePermissions: [],
            targetRolePermissions: [IdentityPermissions.Users.View],
            targetRoleUserType: UserType.ViewerOnly);
        var handler = Handler(
            db,
            new TestUserContext(seed.Actor.Id, [
                IdentityPermissions.Users.AssignRole,
                IdentityPermissions.Users.View
            ]));

        var result = await handler.Handle(
            Command(seed.Target, seed.TargetRole),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.ChangeAccountTypeForbidden");
        seed.Target.UserType.ShouldBe(UserType.SystemAdministrator);
    }

    [Fact]
    public async Task Cross_type_assignment_revalidates_the_live_change_account_type_permission()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(
            db,
            actorRolePermissions: [
                IdentityPermissions.Users.AssignRole,
                IdentityPermissions.Users.View
            ],
            currentRolePermissions: [],
            targetRolePermissions: [IdentityPermissions.Users.View],
            targetRoleUserType: UserType.ViewerOnly);
        var handler = Handler(
            db,
            new TestUserContext(seed.Actor.Id, [
                IdentityPermissions.Users.AssignRole,
                IdentityPermissions.Users.ChangeAccountType,
                IdentityPermissions.Users.View
            ]));

        var result = await handler.Handle(
            Command(seed.Target, seed.TargetRole),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.ChangeAccountTypeForbidden");
        seed.Target.RoleId.ShouldBe(seed.CurrentRole.Id);
        seed.Target.UserType.ShouldBe(UserType.SystemAdministrator);
    }

    [Fact]
    public async Task Direct_cross_type_assignment_uses_the_role_as_authoritative_type()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(
            db,
            actorRolePermissions: [
                IdentityPermissions.Users.AssignRole,
                IdentityPermissions.Users.ChangeAccountType,
                IdentityPermissions.Users.View
            ],
            currentRolePermissions: [],
            targetRolePermissions: [IdentityPermissions.Users.View],
            targetRoleUserType: UserType.ViewerOnly);
        var handler = Handler(
            db,
            new TestUserContext(seed.Actor.Id, [
                IdentityPermissions.Users.AssignRole,
                IdentityPermissions.Users.ChangeAccountType,
                IdentityPermissions.Users.View
            ]));

        var result = await handler.Handle(
            Command(seed.Target, seed.TargetRole),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        seed.Target.RoleId.ShouldBe(seed.TargetRole.Id);
        seed.Target.UserType.ShouldBe(UserType.ViewerOnly);
        seed.Target.ExternalReferenceId.ShouldBeNull();
    }

    [Fact]
    public async Task Invited_user_access_change_rotates_and_redelivers_the_invitation()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(
            db,
            actorRolePermissions: [
                IdentityPermissions.Users.AssignRole,
                IdentityPermissions.Users.ChangeAccountType,
                IdentityPermissions.Users.View
            ],
            currentRolePermissions: [IdentityPermissions.Users.View],
            targetRolePermissions: [],
            currentRoleUserType: UserType.ViewerOnly,
            targetRoleUserType: UserType.SystemAdministrator,
            targetInvited: true);
        var originalInvitation = seed.Target.InvitationToken;
        var notifier = new RecordingInvitationNotifier();
        var handler = Handler(
            db,
            new TestUserContext(seed.Actor.Id, [
                IdentityPermissions.Users.AssignRole,
                IdentityPermissions.Users.ChangeAccountType,
                IdentityPermissions.Users.View
            ]),
            notifier);

        var result = await handler.Handle(
            Command(seed.Target, seed.TargetRole),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        seed.Target.UserType.ShouldBe(UserType.SystemAdministrator);
        seed.Target.InvitationToken.ShouldNotBe(originalInvitation);
        seed.Target.InvitationToken.ShouldBe("replacement-invitation-hash");
        notifier.RawToken.ShouldBe("replacement-invitation");
    }

    [Fact]
    public async Task Assignment_cannot_remove_the_last_protected_system_role_holder()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(
            db,
            actorRolePermissions: [
                IdentityPermissions.Users.AssignRole,
                IdentityPermissions.Users.ChangeAccountType,
                IdentityPermissions.Users.View
            ],
            currentRolePermissions: [IdentityPermissions.Users.View],
            targetRolePermissions: [IdentityPermissions.Users.View],
            targetRoleUserType: UserType.ViewerOnly,
            currentRoleIsSystem: true);
        var handler = Handler(
            db,
            new TestUserContext(seed.Actor.Id, [
                IdentityPermissions.Users.AssignRole,
                IdentityPermissions.Users.ChangeAccountType,
                IdentityPermissions.Users.View
            ]));

        var result = await handler.Handle(
            Command(seed.Target, seed.TargetRole),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.LastAdmin");
        seed.Target.UserType.ShouldBe(UserType.SystemAdministrator);
    }

    [Fact]
    public async Task Assignment_preserves_own_role_protection()
    {
        await using var db = CreateDb();
        var callerId = Guid.NewGuid();
        var handler = Handler(
            db,
            new TestUserContext(callerId, [IdentityPermissions.Users.AssignRole]));

        var result = await handler.Handle(
            new AssignRoleCommand(callerId, Guid.NewGuid(), [1]),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.CannotAssignRoleSelf");
    }

    private static AssignRoleCommandHandler Handler(
        IdentityDbContext db,
        IUserContext context,
        IInvitationNotifier? invitationNotifier = null) =>
        new(
            db,
            context,
            Registry(),
            invitationNotifier ?? new RecordingInvitationNotifier(),
            new TestTokenService(),
            TimeProvider.System,
            Options.Create(new IdentityModuleOptions()),
            NullLogger<AssignRoleCommandHandler>.Instance);

    private static PermissionRegistry Registry() =>
        new([new IdentityPermissionCatalog()]);

    private static AssignRoleCommand Command(User user, Role role) =>
        new(user.Id, role.Id, user.RowVersion);

    private static IdentityDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"identity-role-assignment-{Guid.NewGuid():N}")
            .Options);

    private static async Task<AssignmentSeed> SeedAsync(
        IdentityDbContext db,
        IReadOnlyList<string> actorRolePermissions,
        IReadOnlyList<string> currentRolePermissions,
        IReadOnlyList<string> targetRolePermissions,
        UserType currentRoleUserType = UserType.SystemAdministrator,
        UserType targetRoleUserType = UserType.SystemAdministrator,
        bool currentRoleIsSystem = false,
        bool targetInvited = false)
    {
        var now = TimeProvider.System.GetUtcNow();
        var actorRole = Role.Create(
            $"Actor-{Guid.NewGuid():N}",
            null,
            actorRolePermissions,
            UserType.SystemAdministrator,
            now).Value;
        var currentRole = Role.Create(
            $"Current-{Guid.NewGuid():N}",
            null,
            currentRolePermissions,
            currentRoleUserType,
            now,
            isSystem: currentRoleIsSystem).Value;
        var targetRole = Role.Create(
            $"Target-{Guid.NewGuid():N}",
            null,
            targetRolePermissions,
            targetRoleUserType,
            now).Value;
        var actor = User.CreateActive(
            Email.Create($"actor-{Guid.NewGuid():N}@nags.sa").Value,
            "Actor",
            actorRole.Id,
            "hash",
            now).Value;
        var targetEmail = Email.Create($"target-{Guid.NewGuid():N}@nags.sa").Value;
        var target = targetInvited
            ? User.Invite(
                targetEmail,
                "Target",
                currentRole.Id,
                "original-invitation-hash",
                now.AddDays(1),
                now,
                currentRoleUserType).Value
            : User.CreateActive(
                targetEmail,
                "Target",
                currentRole.Id,
                "hash",
                now).Value;

        db.Roles.AddRange(actorRole, currentRole, targetRole);
        db.Users.AddRange(actor, target);
        await db.SaveChangesAsync();
        return new AssignmentSeed(actor, target, currentRole, targetRole);
    }

    private sealed record AssignmentSeed(
        User Actor,
        User Target,
        Role CurrentRole,
        Role TargetRole);

    private sealed class TestUserContext(Guid userId, string[] permissions) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => userId;
        public UserType? UserType => BuildingBlocks.Contracts.Authorization.UserType.SystemAdministrator;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => permissions.Contains(permission);
    }

    private sealed class RecordingInvitationNotifier : IInvitationNotifier
    {
        public string? RawToken { get; private set; }

        public Task SendInvitationAsync(
            string email,
            string displayName,
            Guid userId,
            string invitationToken,
            CancellationToken cancellationToken = default)
        {
            RawToken = invitationToken;
            return Task.CompletedTask;
        }
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
            new("replacement-invitation", "replacement-invitation-hash");
        public string HashToken(string rawToken) => throw new NotSupportedException();
        public string CreateMfaChallengeToken(User user) => throw new NotSupportedException();
        public MfaChallenge? ValidateMfaChallengeToken(string token) =>
            throw new NotSupportedException();
    }
}
