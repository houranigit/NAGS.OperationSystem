using BuildingBlocks.Application.Abstractions.Mobile.Sync;
using Core.Contracts.Seeding;
using FlightAggregate = Operations.Domain.Aggregates.Flight.Flight;

namespace Operations.Application.Features.Flight.Mobile;

/// <summary>
/// Centralised flight-change → mobile-sync envelope mapping. Every flight command
/// handler routes its broadcasts through this class instead of building envelopes
/// inline so the audience rules (per-employee for "my flights", per-station for
/// the AOG tab) live in one place and stay consistent across handlers.
/// </summary>
/// <remarks>
/// One flight mutation can produce multiple envelopes:
/// <list type="bullet">
///   <item>One <c>flights</c> envelope per assigned employee — that flight may have
///         just appeared / disappeared on each of their "my flights" lists.</item>
///   <item>One <c>flights-aog</c> envelope to the station group when the flight
///         carries an AOG service — the AOG tab on every station employee's phone
///         needs to refresh.</item>
///   <item>One <c>flights-ad-hoc</c> envelope to the station group when the flight's
///         operation type is Ad Hoc — the Ad Hoc tab uses the same station-wide rule.</item>
/// </list>
/// On <c>Op = delete</c> we still emit the same fan-out (the row needs to be
/// removed from every cache that may hold it).
/// </remarks>
public static class FlightMobileSyncBroadcasts
{
    /// <summary>
    /// Emit upsert envelopes for a flight that was just created or modified. Each
    /// currently-assigned employee gets a <c>flights</c> upsert; the station group
    /// gets a <c>flights-aog</c> upsert iff the flight carries an AOG service, and a
    /// <c>flights-ad-hoc</c> upsert iff the operation type is Ad Hoc.
    /// </summary>
    /// <param name="originMutationId">
    /// Optional client-generated mutation id (the <c>ClientMutationId</c> of the work
    /// order / flight that produced this change). Echoed onto every envelope so the
    /// originating device can correlate the realtime push with its local outbox row and
    /// drop the optimistic chip the moment the echo arrives — instead of waiting for the
    /// next periodic refresh. Always <c>null</c> for non-mobile-originated mutations.
    /// </param>
    /// <param name="originClientId">
    /// Optional per-installation device id. Reserved so we can echo-filter pushes when
    /// the same employee is signed in on two devices. Today the mobile client doesn't
    /// send one; left in the signature so adding it later is a one-line change.
    /// </param>
    public static void EnqueueUpsert(
        IMobileSyncBroadcaster broadcaster,
        FlightAggregate flight,
        string? originMutationId = null,
        string? originClientId = null) =>
        EnqueueAll(broadcaster, flight, MobileSyncOps.Upsert, originMutationId, originClientId);

    /// <summary>
    /// Emit delete envelopes for a flight that was cancelled / removed. Same fan-out
    /// as <see cref="EnqueueUpsert"/> so every cache that may hold the row gets the
    /// drop signal.
    /// </summary>
    public static void EnqueueDelete(
        IMobileSyncBroadcaster broadcaster,
        FlightAggregate flight,
        string? originMutationId = null,
        string? originClientId = null) =>
        EnqueueAll(broadcaster, flight, MobileSyncOps.Delete, originMutationId, originClientId);

    /// <summary>
    /// Emit a per-employee <c>flights</c> envelope for a single employee — used when
    /// an assignment change makes the flight appear on, or disappear from, that one
    /// employee's "my flights" list without affecting the rest of the roster.
    /// </summary>
    public static void EnqueueFlightForEmployee(
        IMobileSyncBroadcaster broadcaster,
        FlightAggregate flight,
        Guid employeeId,
        string op)
    {
        broadcaster.Enqueue(new MobileSyncChange(
            Table: MobileSyncTables.Flights,
            Op: op,
            EntityId: flight.Id.Value.ToString(),
            Audience: $"{MobileSyncAudience.EmployeePrefix}{employeeId}",
            Version: flight.UpdatedAt));
    }

    private static void EnqueueAll(
        IMobileSyncBroadcaster broadcaster,
        FlightAggregate flight,
        string op,
        string? originMutationId = null,
        string? originClientId = null)
    {
        var flightId = flight.Id.Value.ToString();
        var version = flight.UpdatedAt;

        foreach (var assignment in flight.AssignedEmployees)
        {
            broadcaster.Enqueue(new MobileSyncChange(
                Table: MobileSyncTables.Flights,
                Op: op,
                EntityId: flightId,
                Audience: $"{MobileSyncAudience.EmployeePrefix}{assignment.Employee.EmployeeId}",
                Version: version,
                Payload: null,
                OriginMutationId: originMutationId,
                OriginClientId: originClientId));
        }

        // The AOG tab on mobile is opt-in by station, not by assignment — every
        // station employee sees every AOG flight at their station regardless of
        // whether they're rostered. So we broadcast to the whole station group
        // when (and only when) the flight is on the AOG plane.
        if (flight.Services.Any(s => s.Service.IsAog))
        {
            broadcaster.Enqueue(new MobileSyncChange(
                Table: MobileSyncTables.FlightsAog,
                Op: op,
                EntityId: flightId,
                Audience: $"{MobileSyncAudience.StationPrefix}{flight.Station.IataCode}",
                Version: version,
                Payload: null,
                OriginMutationId: originMutationId,
                OriginClientId: originClientId));
        }

        if (flight.OperationType.OperationTypeId == CoreSeedIds.AdHocOperationType)
        {
            broadcaster.Enqueue(new MobileSyncChange(
                Table: MobileSyncTables.FlightsAdHoc,
                Op: op,
                EntityId: flightId,
                Audience: $"{MobileSyncAudience.StationPrefix}{flight.Station.IataCode}",
                Version: version,
                Payload: null,
                OriginMutationId: originMutationId,
                OriginClientId: originClientId));
        }
    }
}
