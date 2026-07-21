using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;
using Identity.Application.Features.Users;
using Identity.Domain.Authorization;
using Identity.Domain.Roles;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Identity.Infrastructure.UnitTests.Users;

public sealed class AssignRoleAuthorizationTests
{
    [Fact]
    public async Task Direct_assignment_requires_assign_role_authority()
    {
        await using var db = CreateDb();
        var handler = new AssignRoleCommandHandler(
            db,
            new TestUserContext(Guid.NewGuid(), [IdentityPermissions.Users.View]),
            TimeProvider.System);

        var result = await handler.Handle(
            new AssignRoleCommand(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.AssignRoleForbidden");
    }

    [Fact]
    public async Task Direct_assignment_rejects_a_target_role_above_the_callers_permission_ceiling()
    {
        await using var db = CreateDb();
        var (user, targetRole) = await SeedAssignmentAsync(
            db,
            [IdentityPermissions.Users.View, IdentityPermissions.Users.Update]);
        var originalRoleId = user.RoleId;
        var handler = new AssignRoleCommandHandler(
            db,
            new TestUserContext(Guid.NewGuid(), [
                IdentityPermissions.Users.AssignRole,
                IdentityPermissions.Users.View
            ]),
            TimeProvider.System);

        var result = await handler.Handle(
            new AssignRoleCommand(user.Id, targetRole.Id),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.PermissionDelegationForbidden");
        user.RoleId.ShouldBe(originalRoleId);
    }

    [Fact]
    public async Task Direct_assignment_allows_a_compatible_role_within_the_callers_permission_ceiling()
    {
        await using var db = CreateDb();
        var (user, targetRole) = await SeedAssignmentAsync(db, [IdentityPermissions.Users.View]);
        var handler = new AssignRoleCommandHandler(
            db,
            new TestUserContext(Guid.NewGuid(), [
                IdentityPermissions.Users.AssignRole,
                IdentityPermissions.Users.View,
                IdentityPermissions.Users.Update
            ]),
            TimeProvider.System);

        var result = await handler.Handle(
            new AssignRoleCommand(user.Id, targetRole.Id),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        user.RoleId.ShouldBe(targetRole.Id);
    }

    [Fact]
    public async Task Direct_assignment_preserves_own_role_protection()
    {
        await using var db = CreateDb();
        var callerId = Guid.NewGuid();
        var handler = new AssignRoleCommandHandler(
            db,
            new TestUserContext(callerId, [IdentityPermissions.Users.AssignRole]),
            TimeProvider.System);

        var result = await handler.Handle(
            new AssignRoleCommand(callerId, Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.CannotAssignRoleSelf");
    }

    private static IdentityDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"identity-role-assignment-{Guid.NewGuid():N}")
            .Options);

    private static async Task<(User User, Role TargetRole)> SeedAssignmentAsync(
        IdentityDbContext db,
        IReadOnlyList<string> targetPermissions)
    {
        var now = TimeProvider.System.GetUtcNow();
        var currentRole = Role.Create("Current", null, [], UserType.SystemAdministrator, now).Value;
        var targetRole = Role.Create("Target", null, targetPermissions, UserType.SystemAdministrator, now).Value;
        var user = User.Invite(
            Email.Create("role.target@example.com").Value,
            "Role Target",
            currentRole.Id,
            "invite-hash",
            now.AddHours(24),
            now,
            UserType.SystemAdministrator).Value;

        db.Roles.AddRange(currentRole, targetRole);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user, targetRole);
    }

    private sealed class TestUserContext(Guid userId, string[] permissions) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => userId;
        public UserType? UserType => BuildingBlocks.Contracts.Authorization.UserType.SystemAdministrator;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => permissions.Contains(permission);
    }
}
