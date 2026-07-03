using BuildingBlocks.Contracts.Authorization;
using Operations.Domain.Authorization;
using static BuildingBlocks.Contracts.Authorization.UserType;

namespace Operations.Application.Authorization;

/// <summary>
/// Contributes Operations permissions and their UserType compatibility to the composed registry.
/// Scheduling/approval/merge are administrator-and-scheduler oriented; work-order authoring and
/// cancellation are available to station staff within their station.
/// </summary>
public sealed class OperationsPermissionCatalog : IPermissionCatalog
{
    private static readonly UserType[] AdminOnly = [SystemAdministrator];
    private static readonly UserType[] AdminAndStation = [SystemAdministrator, StationStaff];

    public string Module => "operations";

    public IReadOnlyList<PermissionDescriptor> Permissions { get; } =
    [
        new(OperationsPermissions.Flights.View, AdminAndStation),
        new(OperationsPermissions.Flights.Schedule, AdminAndStation),
        new(OperationsPermissions.Flights.Update, AdminAndStation),
        new(OperationsPermissions.Flights.Assign, AdminAndStation),
        new(OperationsPermissions.Flights.Cancel, AdminAndStation),
        new(OperationsPermissions.Flights.Reopen, AdminOnly),
        new(OperationsPermissions.Flights.Merge, AdminOnly),

        new(OperationsPermissions.WorkOrders.View, AdminAndStation),
        new(OperationsPermissions.WorkOrders.Author, AdminAndStation),
        new(OperationsPermissions.WorkOrders.Submit, AdminAndStation),
        new(OperationsPermissions.WorkOrders.Approve, AdminOnly),
        new(OperationsPermissions.WorkOrders.Reject, AdminOnly),
        new(OperationsPermissions.WorkOrders.Return, AdminOnly),
        new(OperationsPermissions.WorkOrders.Merge, AdminOnly),

        new(OperationsPermissions.Dashboard.View, AdminAndStation),
    ];
}
