using BuildingBlocks.Contracts.Authorization;
using Operations.Domain.Authorization;
using static BuildingBlocks.Contracts.Authorization.UserType;

namespace Operations.Application.Authorization;

public sealed class OperationsPermissionCatalog : IPermissionCatalog
{
    private static readonly UserType[] AdminAndStation = [SystemAdministrator, StationStaff];
    private static readonly UserType[] AdminStationViewer = [SystemAdministrator, StationStaff, ViewerOnly];

    public string Module => "operations";

    public IReadOnlyList<PermissionDescriptor> Permissions { get; } =
    [
        new(OperationsPermissions.Flights.View, AdminStationViewer, GrantsPortalPage: true),
        new(OperationsPermissions.Flights.ViewStation, AdminAndStation),
        new(OperationsPermissions.Flights.Schedule, AdminAndStation),
        new(OperationsPermissions.Flights.Update, AdminAndStation),
        new(OperationsPermissions.Flights.Assign, AdminAndStation),
    ];
}
