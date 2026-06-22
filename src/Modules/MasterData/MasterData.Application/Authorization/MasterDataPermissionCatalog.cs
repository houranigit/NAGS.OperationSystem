using BuildingBlocks.Contracts.Authorization;
using MasterData.Domain.Authorization;
using static BuildingBlocks.Contracts.Authorization.UserType;

namespace MasterData.Application.Authorization;

/// <summary>
/// Contributes MasterData permissions and their UserType compatibility to the composed registry.
/// Compatibility is the maximum set a role of that type may select; SystemAdministrator may select
/// everything. <c>grant-access</c> permissions are administrator-only by design.
/// </summary>
public sealed class MasterDataPermissionCatalog : IPermissionCatalog
{
    private static readonly UserType[] AdminOnly = [SystemAdministrator];
    private static readonly UserType[] AdminAndStation = [SystemAdministrator, StationStaff];
    private static readonly UserType[] AdminAndCustomer = [SystemAdministrator, CustomerContact];
    private static readonly UserType[] AdminStationCustomer = [SystemAdministrator, StationStaff, CustomerContact];

    public string Module => "masterdata";

    public IReadOnlyList<PermissionDescriptor> Permissions { get; } =
    [
        // Countries: everyone can view; admin maintains.
        new(MasterDataPermissions.Countries.View, AdminStationCustomer),
        new(MasterDataPermissions.Countries.Create, AdminOnly),
        new(MasterDataPermissions.Countries.Update, AdminOnly),
        new(MasterDataPermissions.Countries.Activate, AdminOnly),
        new(MasterDataPermissions.Countries.Deactivate, AdminOnly),

        // ManpowerTypes: station staff may view; admin maintains.
        new(MasterDataPermissions.ManpowerTypes.View, AdminAndStation),
        new(MasterDataPermissions.ManpowerTypes.Create, AdminOnly),
        new(MasterDataPermissions.ManpowerTypes.Update, AdminOnly),
        new(MasterDataPermissions.ManpowerTypes.Activate, AdminOnly),
        new(MasterDataPermissions.ManpowerTypes.Deactivate, AdminOnly),

        // Licenses: station staff may view; admin maintains.
        new(MasterDataPermissions.Licenses.View, AdminAndStation),
        new(MasterDataPermissions.Licenses.Create, AdminOnly),
        new(MasterDataPermissions.Licenses.Update, AdminOnly),
        new(MasterDataPermissions.Licenses.Activate, AdminOnly),
        new(MasterDataPermissions.Licenses.Deactivate, AdminOnly),

        // Stations: station staff may view and update their station; admin creates/lifecycle.
        new(MasterDataPermissions.Stations.View, AdminAndStation),
        new(MasterDataPermissions.Stations.Update, AdminAndStation),
        new(MasterDataPermissions.Stations.Create, AdminOnly),
        new(MasterDataPermissions.Stations.Activate, AdminOnly),
        new(MasterDataPermissions.Stations.Deactivate, AdminOnly),

        // StaffMembers: station staff may manage within their station; grant-access admin-only.
        new(MasterDataPermissions.StaffMembers.View, AdminAndStation),
        new(MasterDataPermissions.StaffMembers.Create, AdminAndStation),
        new(MasterDataPermissions.StaffMembers.Update, AdminAndStation),
        new(MasterDataPermissions.StaffMembers.Activate, AdminAndStation),
        new(MasterDataPermissions.StaffMembers.Deactivate, AdminAndStation),
        new(MasterDataPermissions.StaffMembers.GrantAccess, AdminOnly),

        // Customers: customer contacts may view/update their customer; admin creates/lifecycle.
        new(MasterDataPermissions.Customers.View, AdminAndCustomer),
        new(MasterDataPermissions.Customers.Update, AdminAndCustomer),
        new(MasterDataPermissions.Customers.Create, AdminOnly),
        new(MasterDataPermissions.Customers.Activate, AdminOnly),
        new(MasterDataPermissions.Customers.Deactivate, AdminOnly),

        // CustomerContacts: customer contacts may manage within their customer; grant-access admin-only.
        new(MasterDataPermissions.CustomerContacts.View, AdminAndCustomer),
        new(MasterDataPermissions.CustomerContacts.Create, AdminAndCustomer),
        new(MasterDataPermissions.CustomerContacts.Update, AdminAndCustomer),
        new(MasterDataPermissions.CustomerContacts.Remove, AdminAndCustomer),
        new(MasterDataPermissions.CustomerContacts.GrantAccess, AdminOnly),
    ];
}
