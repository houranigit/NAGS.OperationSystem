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
        var handler = CreateHandler(
            db,
            notifier,
            [
                IdentityPermissions.Users.AssignRole,
                "operations.dashboard.view"
            ]);

        var result = await handler.Handle(
            new InviteUserCommand("ceo@example.com", "CEO Viewer", role.Id),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var user = await db.Users.SingleAsync();
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
        var handler = CreateHandler(
            db,
            new TestInvitationNotifier(),
            [IdentityPermissions.Users.AssignRole]);

        var result = await handler.Handle(
            new InviteUserCommand("linked@example.com", "Linked User", role.Id),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.IncompatibleRole");
        (await db.Users.CountAsync()).ShouldBe(0);
    }

    private static InviteUserCommandHandler CreateHandler(
        IdentityDbContext db,
        TestInvitationNotifier notifier,
        string[] callerPermissions) =>
        new(
            db,
            new TestUserContext(callerPermissions),
            notifier,
            new TestTokenService(),
            TimeProvider.System,
            Options.Create(new IdentityModuleOptions()),
            NullLogger<InviteUserCommandHandler>.Instance);

    private static IdentityDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"identity-direct-invite-{Guid.NewGuid():N}")
            .Options);

    private sealed class TestUserContext(string[] permissions) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => Guid.NewGuid();
        public UserType? UserType => BuildingBlocks.Contracts.Authorization.UserType.SystemAdministrator;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => permissions.Contains(permission);
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
