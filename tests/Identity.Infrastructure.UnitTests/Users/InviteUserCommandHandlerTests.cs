using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;
using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Application.Features.Users;
using Identity.Domain.Authorization;
using Identity.Domain.Roles;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Identity.Infrastructure.UnitTests.Users;

public sealed class InviteUserCommandHandlerTests
{
    [Fact]
    public async Task Direct_invite_derives_viewer_only_type_from_selected_role()
    {
        await using var db = CreateDb();
        var role = Role.Create(
            "CEO Viewer",
            null,
            ["operations.dashboard.view"],
            UserType.ViewerOnly,
            TimeProvider.System.GetUtcNow()).Value;
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        var notifier = new TestInvitationNotifier();
        var handler = await CreateHandlerAsync(
            db,
            notifier,
            [
                IdentityPermissions.Users.Invite,
                IdentityPermissions.Users.AssignRole,
                "operations.dashboard.view"
            ]);

        var result = await handler.Handle(
            new InviteUserCommand("ceo@example.com", "CEO Viewer", role.Id),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var user = await db.Users.SingleAsync(candidate => candidate.Email.Value == "ceo@example.com");
        user.RoleId.ShouldBe(role.Id);
        user.UserType.ShouldBe(UserType.ViewerOnly);
        user.ExternalReferenceId.ShouldBeNull();
        notifier.Email.ShouldBe("ceo@example.com");
    }

    [Theory]
    [InlineData(UserType.StationStaff)]
    [InlineData(UserType.CustomerContact)]
    public async Task Direct_invite_rejects_linked_role(UserType userType)
    {
        await using var db = CreateDb();
        var role = Role.Create(
            "Linked Role",
            null,
            [],
            userType,
            TimeProvider.System.GetUtcNow()).Value;
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        var handler = await CreateHandlerAsync(
            db,
            new TestInvitationNotifier(),
            [
                IdentityPermissions.Users.Invite,
                IdentityPermissions.Users.AssignRole
            ]);

        var result = await handler.Handle(
            new InviteUserCommand("linked@example.com", "Linked User", role.Id),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.IncompatibleRole");
        (await db.Users.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Direct_invite_revalidates_the_actors_live_assignment_permission()
    {
        await using var db = CreateDb();
        var role = Role.Create(
            "Viewer target",
            null,
            ["operations.dashboard.view"],
            UserType.ViewerOnly,
            TimeProvider.System.GetUtcNow()).Value;
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        var handler = await CreateHandlerAsync(
            db,
            new TestInvitationNotifier(),
            [
                IdentityPermissions.Users.Invite,
                IdentityPermissions.Users.AssignRole,
                "operations.dashboard.view"
            ],
            livePermissions:
            [
                IdentityPermissions.Users.Invite,
                "operations.dashboard.view"
            ]);

        var result = await handler.Handle(
            new InviteUserCommand("stale-inviter@example.com", "Stale Inviter", role.Id),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.AssignRoleForbidden");
        (await db.Users.AnyAsync(user => user.Email.Value == "stale-inviter@example.com"))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Direct_invite_rejects_a_forged_viewer_role_without_a_portal_page()
    {
        await using var db = CreateDb();
        var forgedViewerRole = Role.Create(
            "Forged empty viewer",
            null,
            [],
            UserType.ViewerOnly,
            TimeProvider.System.GetUtcNow()).Value;
        db.Roles.Add(forgedViewerRole);
        await db.SaveChangesAsync();
        var handler = await CreateHandlerAsync(
            db,
            new TestInvitationNotifier(),
            [
                IdentityPermissions.Users.Invite,
                IdentityPermissions.Users.AssignRole
            ]);

        var result = await handler.Handle(
            new InviteUserCommand(
                "invalid-viewer@example.com",
                "Invalid Viewer",
                forgedViewerRole.Id),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.Role.ViewerPagePermissionRequired");
        (await db.Users.AnyAsync(user => user.Email.Value == "invalid-viewer@example.com"))
            .ShouldBeFalse();
    }

    private static async Task<InviteUserCommandHandler> CreateHandlerAsync(
        IdentityDbContext db,
        TestInvitationNotifier notifier,
        string[] callerPermissions,
        string[]? livePermissions = null)
    {
        var actorPermissions = livePermissions ?? callerPermissions;
        var actorRole = Role.Create(
            $"Inviter-{Guid.NewGuid():N}",
            null,
            actorPermissions,
            UserType.SystemAdministrator,
            TimeProvider.System.GetUtcNow()).Value;
        var actor = User.CreateActive(
            Email.Create($"inviter-{Guid.NewGuid():N}@nags.sa").Value,
            "Inviter",
            actorRole.Id,
            "hash",
            TimeProvider.System.GetUtcNow()).Value;
        db.Roles.Add(actorRole);
        db.Users.Add(actor);
        await db.SaveChangesAsync();

        return new InviteUserCommandHandler(
            db,
            new TestUserContext(actor.Id, callerPermissions),
            TestPermissionRegistry.Instance,
            notifier,
            new TestTokenService(),
            TimeProvider.System,
            Options.Create(new IdentityModuleOptions()),
            NullLogger<InviteUserCommandHandler>.Instance);
    }

    private static IdentityDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"identity-direct-invite-{Guid.NewGuid():N}")
            .Options);

    private sealed class TestUserContext(Guid userId, string[] permissions) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => userId;
        public UserType? UserType => BuildingBlocks.Contracts.Authorization.UserType.SystemAdministrator;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => permissions.Contains(permission);
    }

    private sealed class TestPermissionRegistry : IPermissionRegistry
    {
        public static readonly TestPermissionRegistry Instance = new();

        public IReadOnlyList<PermissionDescriptor> All { get; } =
        [
            new(
                IdentityPermissions.Users.Invite,
                [UserType.SystemAdministrator]),
            new(
                IdentityPermissions.Users.AssignRole,
                [UserType.SystemAdministrator]),
            new(
                "operations.dashboard.view",
                [UserType.SystemAdministrator, UserType.ViewerOnly],
                GrantsPortalPage: true)
        ];

        public bool IsKnown(string permission) =>
            All.Any(descriptor => descriptor.Code == permission);

        public bool IsCompatibleWith(string permission, UserType userType) =>
            All.Any(descriptor =>
                descriptor.Code == permission &&
                descriptor.IsCompatibleWith(userType));

        public IReadOnlyList<string> CompatiblePermissions(UserType userType) =>
            All.Where(descriptor => descriptor.IsCompatibleWith(userType))
                .Select(descriptor => descriptor.Code)
                .ToList();
    }

    private sealed class TestInvitationNotifier : IInvitationNotifier
    {
        public string? Email { get; private set; }

        public Task SendInvitationAsync(
            string email,
            string displayName,
            Guid userId,
            string invitationToken,
            CancellationToken cancellationToken = default)
        {
            Email = email;
            return Task.CompletedTask;
        }
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
