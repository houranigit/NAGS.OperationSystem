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
    private static readonly UserType[] AdminAndViewer = [SystemAdministrator, ViewerOnly];
    private static readonly UserType[] AdminStationViewer = [SystemAdministrator, StationStaff, ViewerOnly];
    private static readonly UserType[] AdminCustomerViewer = [SystemAdministrator, CustomerContact, ViewerOnly];
    private static readonly UserType[] AdminStationCustomerViewer =
        [SystemAdministrator, StationStaff, CustomerContact, ViewerOnly];

    public string Module => "masterdata";

    public IReadOnlyList<PermissionDescriptor> Permissions { get; } =
    [
        // Read-only reference lookups for forms (station staff included); not catalog management.
        new(MasterDataPermissions.Reference.ViewOptions, AdminStationViewer),

        // Countries: everyone can view; admin maintains.
        new(MasterDataPermissions.Countries.View, AdminStationCustomerViewer, GrantsPortalPage: true),
        new(MasterDataPermissions.Countries.Create, AdminOnly),
        new(MasterDataPermissions.Countries.Update, AdminOnly),
        new(MasterDataPermissions.Countries.Activate, AdminOnly),
        new(MasterDataPermissions.Countries.Deactivate, AdminOnly),

        // ManpowerTypes: station staff may view; admin maintains.
        new(MasterDataPermissions.ManpowerTypes.View, AdminStationViewer, GrantsPortalPage: true),
        new(MasterDataPermissions.ManpowerTypes.Create, AdminOnly),
        new(MasterDataPermissions.ManpowerTypes.Update, AdminOnly),
        new(MasterDataPermissions.ManpowerTypes.Activate, AdminOnly),
        new(MasterDataPermissions.ManpowerTypes.Deactivate, AdminOnly),

        // Licenses: station staff may view; admin maintains.
        new(MasterDataPermissions.Licenses.View, AdminStationViewer, GrantsPortalPage: true),
        new(MasterDataPermissions.Licenses.Create, AdminOnly),
        new(MasterDataPermissions.Licenses.Update, AdminOnly),
        new(MasterDataPermissions.Licenses.Activate, AdminOnly),
        new(MasterDataPermissions.Licenses.Deactivate, AdminOnly),

        // Operations catalogs: station staff may view/select; admin maintains.
        new(MasterDataPermissions.Services.View, AdminStationViewer, GrantsPortalPage: true),
        new(MasterDataPermissions.Services.Create, AdminOnly),
        new(MasterDataPermissions.Services.Update, AdminOnly),
        new(MasterDataPermissions.Services.Activate, AdminOnly),
        new(MasterDataPermissions.Services.Deactivate, AdminOnly),

        new(MasterDataPermissions.OperationTypes.View, AdminStationViewer, GrantsPortalPage: true),
        new(MasterDataPermissions.OperationTypes.Create, AdminOnly),
        new(MasterDataPermissions.OperationTypes.Update, AdminOnly),
        new(MasterDataPermissions.OperationTypes.Activate, AdminOnly),
        new(MasterDataPermissions.OperationTypes.Deactivate, AdminOnly),

        new(MasterDataPermissions.AircraftTypes.View, AdminStationViewer, GrantsPortalPage: true),
        new(MasterDataPermissions.AircraftTypes.Create, AdminOnly),
        new(MasterDataPermissions.AircraftTypes.Update, AdminOnly),
        new(MasterDataPermissions.AircraftTypes.Activate, AdminOnly),
        new(MasterDataPermissions.AircraftTypes.Deactivate, AdminOnly),

        new(MasterDataPermissions.Tools.View, AdminStationViewer, GrantsPortalPage: true),
        new(MasterDataPermissions.Tools.Create, AdminOnly),
        new(MasterDataPermissions.Tools.Update, AdminOnly),
        new(MasterDataPermissions.Tools.Activate, AdminOnly),
        new(MasterDataPermissions.Tools.Deactivate, AdminOnly),

        new(MasterDataPermissions.Materials.View, AdminStationViewer, GrantsPortalPage: true),
        new(MasterDataPermissions.Materials.Create, AdminOnly),
        new(MasterDataPermissions.Materials.Update, AdminOnly),
        new(MasterDataPermissions.Materials.Activate, AdminOnly),
        new(MasterDataPermissions.Materials.Deactivate, AdminOnly),

        new(MasterDataPermissions.GeneralSupports.View, AdminStationViewer, GrantsPortalPage: true),
        new(MasterDataPermissions.GeneralSupports.Create, AdminOnly),
        new(MasterDataPermissions.GeneralSupports.Update, AdminOnly),
        new(MasterDataPermissions.GeneralSupports.Activate, AdminOnly),
        new(MasterDataPermissions.GeneralSupports.Deactivate, AdminOnly),

        // Stations: station staff may view and update their station; admin creates/lifecycle.
        new(MasterDataPermissions.Stations.View, AdminStationViewer, GrantsPortalPage: true),
        new(MasterDataPermissions.Stations.Update, AdminAndStation),
        new(MasterDataPermissions.Stations.Create, AdminOnly),
        new(MasterDataPermissions.Stations.Activate, AdminOnly),
        new(MasterDataPermissions.Stations.Deactivate, AdminOnly),

        // StaffMembers: station staff may manage within their station; grant-access admin-only.
        new(MasterDataPermissions.StaffMembers.View, AdminStationViewer, GrantsPortalPage: true),
        new(MasterDataPermissions.StaffMembers.Create, AdminAndStation),
        new(MasterDataPermissions.StaffMembers.Update, AdminAndStation),
        new(MasterDataPermissions.StaffMembers.Activate, AdminAndStation),
        new(MasterDataPermissions.StaffMembers.Deactivate, AdminAndStation),
        new(MasterDataPermissions.StaffMembers.GrantAccess, AdminOnly),

        // Cross-station allocation is globally readable by viewer roles; moves remain admin-only.
        new(MasterDataPermissions.StaffAllocation.View, AdminAndViewer, GrantsPortalPage: true),
        new(MasterDataPermissions.StaffAllocation.Reassign, AdminOnly),

        // Customers: customer contacts may view/update their customer; admin creates/lifecycle.
        new(MasterDataPermissions.Customers.View, AdminCustomerViewer, GrantsPortalPage: true),
        new(MasterDataPermissions.Customers.Update, AdminAndCustomer),
        new(MasterDataPermissions.Customers.Create, AdminOnly),
        new(MasterDataPermissions.Customers.Activate, AdminOnly),
        new(MasterDataPermissions.Customers.Deactivate, AdminOnly),

        // CustomerContacts: customer contacts may manage within their customer; grant-access admin-only.
        new(MasterDataPermissions.CustomerContacts.Create, AdminAndCustomer),
        new(MasterDataPermissions.CustomerContacts.Update, AdminAndCustomer),
        new(MasterDataPermissions.CustomerContacts.Remove, AdminAndCustomer),
        new(MasterDataPermissions.CustomerContacts.GrantAccess, AdminOnly),
    ];
}
