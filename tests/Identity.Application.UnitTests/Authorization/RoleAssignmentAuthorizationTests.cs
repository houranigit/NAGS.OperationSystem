using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;
using Identity.Application.Authorization;
using Identity.Domain.Authorization;
using Identity.Domain.Roles;
using Shouldly;

namespace Identity.Application.UnitTests.Authorization;

public sealed class RoleAssignmentAuthorizationTests
{
    [Fact]
    public void Role_assignment_requires_assign_role_permission()
    {
        var access = RoleAssignmentAuthorization.EnsureCanAssignRole(
            new TestUserContext([IdentityPermissions.Users.Invite]));

        access.IsFailure.ShouldBeTrue();
        access.Error.Code.ShouldBe("Identity.User.AssignRoleForbidden");
    }

    [Fact]
    public void Role_assignment_rejects_a_role_with_permissions_the_caller_does_not_hold()
    {
        var targetRole = CreateRole([
            IdentityPermissions.Users.View,
            IdentityPermissions.Users.Update
        ]);
        var caller = new TestUserContext([
            IdentityPermissions.Users.AssignRole,
            IdentityPermissions.Users.View
        ]);

        var access = RoleAssignmentAuthorization.EnsureWithinPermissionCeiling(caller, targetRole);

        access.IsFailure.ShouldBeTrue();
        access.Error.Code.ShouldBe("Identity.User.PermissionDelegationForbidden");
    }

    [Fact]
    public void Role_assignment_allows_a_role_whose_permissions_are_a_subset_of_the_callers()
    {
        var targetRole = CreateRole([IdentityPermissions.Users.View]);
        var caller = new TestUserContext([
            IdentityPermissions.Users.AssignRole,
            IdentityPermissions.Users.View,
            IdentityPermissions.Users.Update
        ]);

        RoleAssignmentAuthorization.EnsureWithinPermissionCeiling(caller, targetRole).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Permission_ceiling_does_not_replace_assign_role_authority()
    {
        var emptyRole = CreateRole([]);
        var caller = new TestUserContext([]);

        RoleAssignmentAuthorization.EnsureWithinPermissionCeiling(caller, emptyRole).IsSuccess.ShouldBeTrue();

        var access = RoleAssignmentAuthorization.EnsureCanAssignRole(caller);
        access.IsFailure.ShouldBeTrue();
        access.Error.Code.ShouldBe("Identity.User.AssignRoleForbidden");
    }

    private static Role CreateRole(IReadOnlyList<string> permissions) =>
        Role.Create(
            "Delegated administrator",
            description: null,
            permissions,
            UserType.SystemAdministrator,
            DateTimeOffset.UtcNow).Value;

    private sealed class TestUserContext(string[] permissions) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; } = Guid.NewGuid();
        public UserType? UserType => BuildingBlocks.Contracts.Authorization.UserType.SystemAdministrator;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => permissions.Contains(permission);
    }
}
