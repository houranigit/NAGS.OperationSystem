using BuildingBlocks.Contracts.Authorization;
using Operations.Domain.Authorization;
using static BuildingBlocks.Contracts.Authorization.UserType;

namespace Operations.Application.Authorization;

/// <summary>
/// Contributes Operations permissions and their UserType compatibility to the composed registry.
/// Scheduling and merge are administrator-and-scheduler oriented; station staff remain scoped to
/// their station.
/// </summary>
public sealed class OperationsPermissionCatalog : IPermissionCatalog
{
    private static readonly UserType[] AdminAndStation = [SystemAdministrator, StationStaff];
    private static readonly UserType[] AdminStationViewer = [SystemAdministrator, StationStaff, ViewerOnly];
    private static readonly UserType[] AdminOnly = [SystemAdministrator];

    public string Module => "operations";

    public IReadOnlyList<PermissionDescriptor> Permissions { get; } =
    [
        new(OperationsPermissions.Flights.View, AdminStationViewer, GrantsPortalPage: true),
        new(OperationsPermissions.Flights.Export, AdminStationViewer),
        new(OperationsPermissions.Flights.ViewStation, AdminAndStation),
        new(OperationsPermissions.Flights.Schedule, AdminAndStation),
        new(OperationsPermissions.Flights.Update, AdminAndStation),
        new(OperationsPermissions.Flights.Assign, AdminAndStation),
        new(OperationsPermissions.Flights.Invite, AdminAndStation),
        new(OperationsPermissions.Flights.Merge, AdminOnly),

        new(OperationsPermissions.Dashboard.View, AdminStationViewer, GrantsPortalPage: true),
        new(OperationsPermissions.Dashboard.ViewAnalytics, AdminStationViewer, GrantsPortalPage: true),
        new(OperationsPermissions.Dashboard.Export, AdminStationViewer),

        new(OperationsPermissions.WorkOrders.View, AdminStationViewer, GrantsPortalPage: true),
        new(OperationsPermissions.WorkOrders.ViewOthers, AdminAndStation),
        new(OperationsPermissions.WorkOrders.Author, AdminAndStation),
        new(OperationsPermissions.WorkOrders.ManageOthers, AdminAndStation),
        new(OperationsPermissions.WorkOrders.Approve, AdminAndStation),
        new(OperationsPermissions.WorkOrders.DeleteOthers, AdminAndStation),
        new(OperationsPermissions.WorkOrders.Merge, AdminOnly),
    ];
}
