using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Readers;
using Operations.Domain.Flights;

namespace Operations.Application.Authorization;

/// <summary>
/// The data boundary the current caller may act within. A <see cref="UserType.SystemAdministrator"/>
/// is unrestricted; a <see cref="UserType.StationStaff"/> is confined to <see cref="StationId"/> and,
/// for non-Per-Landing flights, to flights they are assigned to. CustomerContacts have no Operations
/// access in this release.
/// </summary>
public sealed record OperationsScopeContext(UserType UserType, Guid? StationId, Guid? StaffMemberId)
{
    public bool IsAdministrator => UserType == UserType.SystemAdministrator;

    public Result EnsureStation(Guid stationId) =>
        IsAdministrator || StationId == stationId
            ? Result.Success()
            : Error.Forbidden("This flight is outside your station scope.", "Operations.Scope.Forbidden");

    /// <summary>
    /// True when the caller may see/act on <paramref name="flight"/>: administrators always; station
    /// staff when the flight is at their station AND is Per-Landing (station-wide visibility) or has
    /// them on its assigned-employee roster. Requires <c>PlannedServices</c> and
    /// <c>AssignedEmployees</c> to be loaded on the flight.
    /// </summary>
    public bool CanAccessFlight(Flight flight)
    {
        if (IsAdministrator)
            return true;

        if (StationId != flight.Station.StationId)
            return false;

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

                return new OperationsScopeContext(userType, staff.StationId, staffId);
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
