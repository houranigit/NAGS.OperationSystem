using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Readers;
using Operations.Domain.Authorization;
using Operations.Domain.Flights;
using Operations.Domain.WorkOrders;

namespace Operations.Application.Authorization;

/// <summary>
/// The data boundary the current caller may act within. A <see cref="UserType.SystemAdministrator"/>
/// is unrestricted; a <see cref="UserType.StationStaff"/> is confined to <see cref="StationId"/> and,
/// for non-Per-Landing flights, to flights they are assigned to — unless they hold the
/// <c>operations.flights.view-station</c> permission (<see cref="CanViewStationWide"/>), which widens
/// visibility to every flight at their station (station dispatchers). CustomerContacts have no
/// Operations access in this release.
/// </summary>
public sealed record OperationsScopeContext(
    UserType UserType,
    Guid? StationId,
    Guid? StaffMemberId,
    bool CanViewStationWide = false,
    bool CanViewWorkOrdersStationWide = false,
    Guid? UserId = null,
    Guid? ManpowerTypeId = null)
{
    public bool IsAdministrator => UserType == UserType.SystemAdministrator;

    public Result EnsureStation(Guid stationId) =>
        IsAdministrator || StationId == stationId
            ? Result.Success()
            : Error.Forbidden("This flight is outside your station scope.", "Operations.Scope.Forbidden");

    /// <summary>
    /// True when the caller may see/act on <paramref name="flight"/>: administrators always; station
    /// staff when the flight is at their station AND they hold station-wide visibility, or the flight
    /// is Per-Landing (station-wide by nature), or they are on its assigned-employee roster. Requires
    /// <c>PlannedServices</c> and <c>AssignedEmployees</c> to be loaded on the flight.
    /// </summary>
    public bool CanAccessFlight(Flight flight)
    {
        if (IsAdministrator)
            return true;

        if (StationId != flight.Station.StationId)
            return false;

        if (CanViewStationWide)
            return true;

        if (flight.IsPerLanding)
            return true;

        return StaffMemberId is { } staffId &&
               flight.AssignedEmployees.Any(e => e.Employee.StaffMemberId == staffId);
    }

    /// <summary>Fails closed with Forbidden when <see cref="CanAccessFlight"/> is false.</summary>
    public Result EnsureFlightAccess(Flight flight) =>
        CanAccessFlight(flight)
            ? Result.Success()
            : Error.Forbidden(
                "You do not have access to this flight. Non-Per-Landing flights are visible only to their assigned staff.",
                "Operations.Scope.FlightForbidden");

    public bool CanAccessWorkOrder(WorkOrder workOrder)
    {
        if (IsAdministrator)
            return true;

        if (StationId != workOrder.Station.StationId)
            return false;

        if (CanViewWorkOrdersStationWide)
            return true;

        return UserId is { } userId && workOrder.OwnerUserId == userId;
    }

    public Result EnsureWorkOrderAccess(WorkOrder workOrder) =>
        CanAccessWorkOrder(workOrder)
            ? Result.Success()
            : Error.Forbidden("You do not have access to this work order.", "Operations.Scope.WorkOrderForbidden");
}

/// <summary>
/// Resolves and enforces the caller's Operations data scope. Resolution fails closed: a station-staff
/// account is denied whenever its linked StaffMember or its Station is missing or inactive.
/// </summary>
public interface IOperationsScope
{
    public Task<Result<OperationsScopeContext>> ResolveAsync(CancellationToken cancellationToken);
}

public sealed class OperationsScope(IUserContext user, IMasterDataReader masterData) : IOperationsScope
{
    public async Task<Result<OperationsScopeContext>> ResolveAsync(CancellationToken cancellationToken)
    {
        if (!user.IsAuthenticated || user.UserType is not { } userType)
            return Error.Forbidden("The request is not authenticated.", "Operations.Scope.Unauthenticated");

        switch (userType)
        {
            case UserType.SystemAdministrator:
                return new OperationsScopeContext(userType, null, null);

            case UserType.StationStaff:
            {
                if (user.ExternalReferenceId is not { } staffId)
                    return Denied();

                var staff = await masterData.GetStaffMemberAsync(staffId, cancellationToken);
                if (staff is null || !staff.IsActive)
                    return Denied();

                var station = await masterData.GetStationAsync(staff.StationId, cancellationToken);
                if (station is null || !station.IsActive)
                    return Denied();

                // Station dispatchers hold view-station and see every flight at their station
                // without being on the assigned-employee roster.
                var canViewStationWide = user.HasPermission(OperationsPermissions.Flights.ViewStation);
                var canViewWorkOrdersStationWide = user.HasPermission(OperationsPermissions.WorkOrders.ViewOthers);

                return new OperationsScopeContext(
                    userType, staff.StationId, staffId, canViewStationWide, canViewWorkOrdersStationWide, user.UserId, staff.ManpowerTypeId);
            }

            default:
                return Denied();
        }
    }

    private static Error Denied() =>
        Error.Forbidden("Your linked staff record or its station is missing or inactive.", "Operations.Scope.Denied");
}

public static class OperationsScopeExtensions
{
    public static async Task<Result<OperationsScopeContext>> ResolveForWriteAsync(this IOperationsScope scope, CancellationToken cancellationToken) =>
        await scope.ResolveAsync(cancellationToken);
}
