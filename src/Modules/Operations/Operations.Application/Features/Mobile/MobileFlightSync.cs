using BuildingBlocks.Application.Mobile;
using MasterData.Contracts.Seeding;
using Operations.Domain.Enumerations;
using Operations.Domain.Flights;

namespace Operations.Application.Features.Mobile;

/// <summary>
/// Centralised flight-change → mobile-sync envelope mapping. Flight and work-order command
/// handlers route broadcasts through this class instead of building envelopes inline so the
/// audience rules stay in one place:
/// <list type="bullet">
///   <item>One <c>flights</c> envelope per assigned staff member — non-Ad-Hoc list members upsert;
///         Ad Hoc or unsupported-status rows actively delete any stale My Flights entry.</item>
///   <item>One <c>flights-per-landing</c> envelope to the station group when the flight is
///         Per-Landing — that tab is station-wide by nature.</item>
///   <item>One <c>flights-ad-hoc</c> envelope to the station group when the operation type is
///         Ad Hoc — same station-wide rule.</item>
/// </list>
/// The flight must have <c>PlannedServices</c> and <c>AssignedEmployees</c> loaded.
/// </summary>
public static class MobileFlightSync
{
    /// <summary>
    /// Emit upsert envelopes for a flight that was created or modified (including embedded
    /// work-order changes — the client re-fetches the whole flight row by id).
    /// </summary>
    /// <param name="originMutationId">
    /// The <c>clientMutationId</c> of the mobile outbox mutation that caused this change, echoed
    /// so the originating device can reconcile its outbox row. Null for portal-originated changes.
    /// </param>
    public static void EnqueueUpsert(IMobileSyncBroadcaster broadcaster, Flight flight, string? originMutationId = null) =>
        EnqueueAll(broadcaster, flight, MobileSyncOps.Upsert, originMutationId);

    /// <summary>Emit delete envelopes with the same fan-out so every cache that may hold the row drops it.</summary>
    public static void EnqueueDelete(IMobileSyncBroadcaster broadcaster, Flight flight, string? originMutationId = null) =>
        EnqueueAll(broadcaster, flight, MobileSyncOps.Delete, originMutationId);

    /// <summary>
    /// A bulk scheduling command can create hundreds of rows for the same audience. One refresh
    /// envelope per affected cache avoids making every connected device issue one by-id request
    /// per new flight while preserving the same eventual list state.
    /// </summary>
    public static void EnqueueBulkRefresh(
        IMobileSyncBroadcaster broadcaster,
        IReadOnlyCollection<Flight> flights,
        DateTimeOffset version)
    {
        foreach (var staffMemberId in flights
                     .SelectMany(flight => flight.AssignedEmployees)
                     .Select(assignment => assignment.Employee.StaffMemberId)
                     .Distinct())
        {
            broadcaster.Enqueue(new MobileSyncChange(
                Table: MobileSyncTables.Flights,
                Op: MobileSyncOps.Refresh,
                EntityId: null,
                Audience: MobileSyncAudience.Employee(staffMemberId),
                Version: version));
        }

        foreach (var stationIata in flights
                     .Where(flight => flight.IsPerLanding)
                     .Select(flight => flight.Station.IataCode)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            broadcaster.Enqueue(new MobileSyncChange(
                Table: MobileSyncTables.FlightsPerLanding,
                Op: MobileSyncOps.Refresh,
                EntityId: null,
                Audience: MobileSyncAudience.Station(stationIata),
                Version: version));
        }

        foreach (var stationIata in flights
                     .Where(flight => flight.OperationType.OperationTypeId == WellKnownMasterDataIds.AdHocOperationType)
                     .Select(flight => flight.Station.IataCode)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            broadcaster.Enqueue(new MobileSyncChange(
                Table: MobileSyncTables.FlightsAdHoc,
                Op: MobileSyncOps.Refresh,
                EntityId: null,
                Audience: MobileSyncAudience.Station(stationIata),
                Version: version));
        }
    }

    /// <summary>
    /// Emit a per-staff-member <c>flights</c> envelope — used when an assignment change makes the
    /// flight appear on, or disappear from, one person's "my flights" list without affecting the
    /// rest of the roster.
    /// </summary>
    public static void EnqueueFlightForStaffMember(IMobileSyncBroadcaster broadcaster, Flight flight, Guid staffMemberId, string op) =>
        broadcaster.Enqueue(new MobileSyncChange(
            Table: MobileSyncTables.Flights,
            Op: ListOp(
                op,
                IsVisibleStatus(flight.Status) &&
                !flight.IsPerLanding &&
                flight.OperationType.OperationTypeId != WellKnownMasterDataIds.AdHocOperationType),
            EntityId: flight.Id.ToString(),
            Audience: MobileSyncAudience.Employee(staffMemberId),
            Version: Version(flight)));

    private static void EnqueueAll(IMobileSyncBroadcaster broadcaster, Flight flight, string op, string? originMutationId)
    {
        var flightId = flight.Id.ToString();
        var version = Version(flight);

        var isVisibleStatus = IsVisibleStatus(flight.Status);
        var isAdHoc = flight.OperationType.OperationTypeId == WellKnownMasterDataIds.AdHocOperationType;

        foreach (var assignment in flight.AssignedEmployees)
        {
            broadcaster.Enqueue(new MobileSyncChange(
                Table: MobileSyncTables.Flights,
                // Ad Hoc and non-list statuses must actively purge older My Flights cache rows.
                Op: ListOp(op, isVisibleStatus && !flight.IsPerLanding && !isAdHoc),
                EntityId: flightId,
                Audience: MobileSyncAudience.Employee(assignment.Employee.StaffMemberId),
                Version: version,
                Payload: null,
                OriginMutationId: originMutationId));
        }

        // The Per-Landing tab is opt-in by station, not by assignment — every staff member at the
        // station sees every Per-Landing flight there regardless of roster.
        if (flight.IsPerLanding)
        {
            broadcaster.Enqueue(new MobileSyncChange(
                Table: MobileSyncTables.FlightsPerLanding,
                Op: ListOp(op, isVisibleStatus && !isAdHoc),
                EntityId: flightId,
                Audience: MobileSyncAudience.Station(flight.Station.IataCode),
                Version: version,
                Payload: null,
                OriginMutationId: originMutationId));
        }

        if (isAdHoc)
        {
            broadcaster.Enqueue(new MobileSyncChange(
                Table: MobileSyncTables.FlightsAdHoc,
                Op: ListOp(op, isVisibleStatus),
                EntityId: flightId,
                Audience: MobileSyncAudience.Station(flight.Station.IataCode),
                Version: version,
                Payload: null,
                OriginMutationId: originMutationId));
        }
    }

    private static string ListOp(string requestedOp, bool isListMember) =>
        requestedOp == MobileSyncOps.Delete || !isListMember
            ? MobileSyncOps.Delete
            : MobileSyncOps.Upsert;

    private static bool IsVisibleStatus(FlightStatus status) =>
        status is FlightStatus.Scheduled or FlightStatus.InProgress or FlightStatus.Completed;

    private static DateTimeOffset Version(Flight flight) => flight.UpdatedAtUtc ?? flight.CreatedAtUtc;
}
