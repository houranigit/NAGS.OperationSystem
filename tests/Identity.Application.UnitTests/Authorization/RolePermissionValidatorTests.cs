using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Contracts.Authorization;
using Identity.Application.Authorization;
using Identity.Domain.Authorization;
using MasterData.Application.Authorization;
using MasterData.Domain.Authorization;
using Shouldly;

namespace Identity.Application.UnitTests.Authorization;

public sealed class RolePermissionValidatorTests
{
    private static PermissionRegistry Registry() =>
        new([new IdentityPermissionCatalog(), new MasterDataPermissionCatalog()]);

    [Fact]
    public void Composed_catalog_exposes_masterdata_permissions_by_user_type()
    {
        var registry = Registry();

        var stationPermissions = registry.CompatiblePermissions(UserType.StationStaff);
        stationPermissions.ShouldContain(MasterDataPermissions.StaffMembers.View);
        stationPermissions.ShouldContain(MasterDataPermissions.Reference.ViewOptions);
        stationPermissions.ShouldNotContain(IdentityPermissions.Roles.View);
        stationPermissions.ShouldNotContain(MasterDataPermissions.CustomerContacts.View);
        stationPermissions.ShouldNotContain(MasterDataPermissions.StaffMembers.GrantAccess);
        stationPermissions.ShouldNotContain(MasterDataPermissions.Stations.Activate);
        stationPermissions.ShouldNotContain(MasterDataPermissions.Stations.Deactivate);

        var customerPermissions = registry.CompatiblePermissions(UserType.CustomerContact);
        customerPermissions.ShouldContain(MasterDataPermissions.Customers.View);
        customerPermissions.ShouldContain(MasterDataPermissions.CustomerContacts.Update);
        customerPermissions.ShouldNotContain(MasterDataPermissions.StaffMembers.View);
        customerPermissions.ShouldNotContain(MasterDataPermissions.CustomerContacts.GrantAccess);
        customerPermissions.ShouldNotContain(MasterDataPermissions.Customers.Activate);
        customerPermissions.ShouldNotContain(MasterDataPermissions.Customers.Deactivate);
    }

    [Fact]
    public void Role_validation_accepts_permissions_compatible_with_role_user_type()
    {
        var result = RolePermissionValidator.Validate(
            [MasterDataPermissions.StaffMembers.View, MasterDataPermissions.StaffMembers.Update],
            UserType.StationStaff,
            Registry());

        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(MasterDataPermissions.StaffMembers.GrantAccess)]
    [InlineData(MasterDataPermissions.CustomerContacts.View)]
    [InlineData(IdentityPermissions.Roles.View)]
    public void Role_validation_rejects_known_permissions_incompatible_with_role_user_type(string permission)
    {
        var result = RolePermissionValidator.Validate([permission], UserType.StationStaff, Registry());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.Role.IncompatiblePermission");
    }

    [Fact]
    public void Role_validation_rejects_unknown_permissions()
    {
        var result = RolePermissionValidator.Validate(["masterdata.unknown.view"], UserType.SystemAdministrator, Registry());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.Role.UnknownPermission");
    }
}
