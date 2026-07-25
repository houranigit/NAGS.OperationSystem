using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;
using Identity.Application.Authorization;
using Identity.Domain.Roles;
using Identity.Domain.Users;
using Shouldly;

namespace Identity.Application.UnitTests.Authorization;

public sealed class EffectiveUserPermissionsTests
{
    private const string DashboardView = "operations.dashboard.view";
    private const string SessionsView = "identity.sessions.view";
    private const string UsersUpdate = "identity.users.update";
    private static readonly DateTimeOffset Now =
        new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    private static readonly TestPermissionRegistry Registry = new(
    [
        new PermissionDescriptor(
            DashboardView,
            [UserType.SystemAdministrator, UserType.ViewerOnly],
            GrantsPortalPage: true),
        new PermissionDescriptor(
            SessionsView,
            [UserType.SystemAdministrator, UserType.ViewerOnly]),
        new PermissionDescriptor(
            UsersUpdate,
            [UserType.SystemAdministrator])
    ]);

    [Fact]
    public void Missing_role_fails_closed()
    {
        var user = Invite(UserType.ViewerOnly, Guid.NewGuid());

        var result = EffectiveUserPermissions.For(user, role: null, Registry);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.Auth.InvalidRoleConfiguration");
    }

    [Fact]
    public void Role_with_a_different_account_type_fails_closed()
    {
        var viewerRole = CreateRole(UserType.ViewerOnly, [DashboardView]);
        var administrator = Invite(UserType.SystemAdministrator, viewerRole.Id);

        var result = EffectiveUserPermissions.For(administrator, viewerRole, Registry);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.Auth.InvalidRoleConfiguration");
    }

    [Fact]
    public void Role_with_an_incompatible_permission_fails_closed()
    {
        var viewerRole = CreateRole(UserType.ViewerOnly, [DashboardView, UsersUpdate]);
        var viewer = Invite(UserType.ViewerOnly, viewerRole.Id);

        var result = EffectiveUserPermissions.For(viewer, viewerRole, Registry);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.Auth.InvalidRoleConfiguration");
    }

    [Fact]
    public void Viewer_role_without_a_portal_page_fails_closed()
    {
        var viewerRole = CreateRole(UserType.ViewerOnly, [SessionsView]);
        var viewer = Invite(UserType.ViewerOnly, viewerRole.Id);

        var result = EffectiveUserPermissions.For(viewer, viewerRole, Registry);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.Auth.InvalidRoleConfiguration");
    }

    [Fact]
    public void Matching_valid_role_returns_its_permissions()
    {
        var viewerRole = CreateRole(UserType.ViewerOnly, [DashboardView, SessionsView]);
        var viewer = Invite(UserType.ViewerOnly, viewerRole.Id);

        var result = EffectiveUserPermissions.For(viewer, viewerRole, Registry);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe([DashboardView, SessionsView], ignoreOrder: true);
    }

    private static Role CreateRole(UserType userType, IReadOnlyList<string> permissions) =>
        Role.Create(
            $"Role-{Guid.NewGuid():N}",
            description: null,
            permissions,
            userType,
            Now).Value;

    private static User Invite(UserType userType, Guid roleId) =>
        User.Invite(
            Email.Create($"{Guid.NewGuid():N}@example.com").Value,
            "Permission Test",
            roleId,
            "invitation-hash",
            Now.AddDays(1),
            Now,
            userType).Value;

    private sealed class TestPermissionRegistry(IReadOnlyList<PermissionDescriptor> descriptors)
        : IPermissionRegistry
    {
        public IReadOnlyList<PermissionDescriptor> All { get; } = descriptors;

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
}
