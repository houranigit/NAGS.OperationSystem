using System.Reflection;
using System.Text.RegularExpressions;
using Audit.Application.Authorization;
using Audit.Domain.Authorization;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Contracts.Authorization;
using Identity.Application.Authorization;
using Identity.Domain.Authorization;
using MasterData.Application.Authorization;
using MasterData.Domain.Authorization;
using Operations.Application.Authorization;
using Operations.Domain.Authorization;
using Shouldly;

namespace Identity.Application.UnitTests.Authorization;

public sealed class PermissionCatalogIntegrityTests
{
    private const string RetiredUserCreatePermission = "identity.users.create";
    private const string RetiredCustomerContactViewPermission = "masterdata.customer-contacts.view";

    private static readonly Regex PermissionCodePattern = new(
        "^[a-z][a-z0-9]*(?:\\.[a-z0-9]+(?:-[a-z0-9]+)*){2}$",
        RegexOptions.CultureInvariant);

    [Fact]
    public void Module_catalogs_expose_every_declared_permission_exactly_once()
    {
        var catalogs = Catalogs();
        var descriptors = catalogs.SelectMany(entry => entry.Catalog.Permissions).ToList();

        catalogs.Select(entry => entry.Catalog.Module)
            .Distinct(StringComparer.Ordinal)
            .Count()
            .ShouldBe(catalogs.Count, "each permission catalog must have a unique module name");

        var duplicateCodes = descriptors
            .GroupBy(descriptor => descriptor.Code, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();

        duplicateCodes.ShouldBeEmpty("permission codes must be globally unique");

        foreach (var (catalog, permissionContainer) in catalogs)
        {
            var declaredCodes = DeclaredPermissionCodes(permissionContainer);
            var catalogCodes = catalog.Permissions
                .Select(descriptor => descriptor.Code)
                .OrderBy(code => code, StringComparer.Ordinal)
                .ToArray();

            catalogCodes.ShouldBe(
                declaredCodes,
                $"the {catalog.Module} catalog must include every permission declared by its domain module");

            catalogCodes.ShouldAllBe(
                code => code.StartsWith($"{catalog.Module}.", StringComparison.Ordinal),
                $"every {catalog.Module} permission must use its module prefix");

            catalogCodes.ShouldAllBe(
                code => PermissionCodePattern.IsMatch(code),
                $"every {catalog.Module} permission must follow module.resource.action naming");
        }

        var registry = Registry();
        registry.All.Count.ShouldBe(descriptors.Count, "the composed registry must not silently discard a permission");
    }

    [Fact]
    public void Every_grantable_permission_is_available_to_system_administrator_roles()
    {
        var registry = Registry();
        var allCodes = registry.All.Select(descriptor => descriptor.Code).ToArray();

        registry.CompatiblePermissions(UserType.SystemAdministrator)
            .ShouldBe(allCodes, ignoreOrder: true);

        foreach (var descriptor in registry.All)
        {
            descriptor.CompatibleUserTypes.ShouldNotBeEmpty(
                $"{descriptor.Code} must be compatible with at least one user type");
            descriptor.CompatibleUserTypes.Distinct().Count().ShouldBe(
                descriptor.CompatibleUserTypes.Count,
                $"{descriptor.Code} must not repeat compatible user types");
        }
    }

    [Fact]
    public void Staff_allocation_permissions_have_expected_user_type_compatibility()
    {
        var registry = Registry();
        string[] permissions =
        [
            MasterDataPermissions.StaffAllocation.View,
            MasterDataPermissions.StaffAllocation.Reassign
        ];

        permissions.ShouldBe(
        [
            "masterdata.staff-allocation.view",
            "masterdata.staff-allocation.reassign"
        ]);

        foreach (var permission in permissions)
        {
            registry.IsKnown(permission).ShouldBeTrue();
            registry.IsCompatibleWith(permission, UserType.SystemAdministrator).ShouldBeTrue();
            registry.IsCompatibleWith(permission, UserType.StationStaff).ShouldBeFalse();
            registry.IsCompatibleWith(permission, UserType.CustomerContact).ShouldBeFalse();
            registry.IsCompatibleWith(permission, UserType.ViewerOnly)
                .ShouldBe(permission == MasterDataPermissions.StaffAllocation.View);
        }
    }

    [Fact]
    public void Viewer_only_permission_allowlist_and_portal_pages_are_exact()
    {
        var registry = Registry();
        string[] expectedPermissions =
        [
            IdentityPermissions.Users.View,
            IdentityPermissions.Roles.View,
            IdentityPermissions.Sessions.View,
            AuditPermissions.Trails.View,
            MasterDataPermissions.Reference.ViewOptions,
            MasterDataPermissions.Countries.View,
            MasterDataPermissions.ManpowerTypes.View,
            MasterDataPermissions.Licenses.View,
            MasterDataPermissions.Services.View,
            MasterDataPermissions.OperationTypes.View,
            MasterDataPermissions.AircraftTypes.View,
            MasterDataPermissions.Tools.View,
            MasterDataPermissions.Materials.View,
            MasterDataPermissions.GeneralSupports.View,
            MasterDataPermissions.Stations.View,
            MasterDataPermissions.StaffMembers.View,
            MasterDataPermissions.StaffAllocation.View,
            MasterDataPermissions.Customers.View,
            OperationsPermissions.Dashboard.View,
            OperationsPermissions.Dashboard.ViewAnalytics,
            OperationsPermissions.Dashboard.Export,
            OperationsPermissions.Flights.View,
            OperationsPermissions.Flights.Export,
            OperationsPermissions.WorkOrders.View
        ];
        string[] expectedPortalPages =
        [
            IdentityPermissions.Users.View,
            IdentityPermissions.Roles.View,
            AuditPermissions.Trails.View,
            MasterDataPermissions.Countries.View,
            MasterDataPermissions.ManpowerTypes.View,
            MasterDataPermissions.Licenses.View,
            MasterDataPermissions.Services.View,
            MasterDataPermissions.OperationTypes.View,
            MasterDataPermissions.AircraftTypes.View,
            MasterDataPermissions.Tools.View,
            MasterDataPermissions.Materials.View,
            MasterDataPermissions.GeneralSupports.View,
            MasterDataPermissions.Stations.View,
            MasterDataPermissions.StaffMembers.View,
            MasterDataPermissions.StaffAllocation.View,
            MasterDataPermissions.Customers.View,
            OperationsPermissions.Dashboard.View,
            OperationsPermissions.Dashboard.ViewAnalytics,
            OperationsPermissions.Flights.View,
            OperationsPermissions.WorkOrders.View
        ];

        registry.CompatiblePermissions(UserType.ViewerOnly)
            .ShouldBe(expectedPermissions, ignoreOrder: true);
        registry.All
            .Where(descriptor => descriptor.IsCompatibleWith(UserType.ViewerOnly) && descriptor.GrantsPortalPage)
            .Select(descriptor => descriptor.Code)
            .ShouldBe(expectedPortalPages, ignoreOrder: true);
    }

    [Theory]
    [InlineData(RetiredUserCreatePermission)]
    [InlineData(RetiredCustomerContactViewPermission)]
    public void Retired_no_op_permissions_are_not_grantable(string retiredPermission)
    {
        var registry = Registry();

        registry.IsKnown(retiredPermission).ShouldBeFalse();
        registry.CompatiblePermissions(UserType.SystemAdministrator).ShouldNotContain(retiredPermission);

        var validation = RolePermissionValidator.Validate(
            [retiredPermission],
            UserType.SystemAdministrator,
            registry);

        validation.IsFailure.ShouldBeTrue();
        validation.Error.Code.ShouldBe("Identity.Role.UnknownPermission");
    }

    private static PermissionRegistry Registry() =>
        new(Catalogs().Select(entry => entry.Catalog));

    private static IReadOnlyList<(IPermissionCatalog Catalog, Type PermissionContainer)> Catalogs() =>
    [
        (new IdentityPermissionCatalog(), typeof(IdentityPermissions)),
        (new MasterDataPermissionCatalog(), typeof(MasterDataPermissions)),
        (new OperationsPermissionCatalog(), typeof(OperationsPermissions)),
        (new AuditPermissionCatalog(), typeof(AuditPermissions))
    ];

    private static string[] DeclaredPermissionCodes(Type permissionContainer) =>
        permissionContainer
            .GetNestedTypes(BindingFlags.Public)
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field is { FieldType: not null, IsLiteral: true, IsInitOnly: false }
                && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();
}
