using BuildingBlocks.Application.Abstractions.Queries;
using Operations.Contracts.Mobile;

namespace Operations.Application.Features.Flight.Queries.GetFlightSummaryForMobile;

/// <summary>
/// Single-row variant of <c>GetMyAssignedFlightsForMobileQuery</c> used by the real-time
/// sync path: when the server pushes an <c>upsert</c> envelope for a flight the mobile
/// client fetches just that flight through <c>GET /api/mobile/v2/flights/{id}</c>. We
/// keep the same projection shape as the list query so mobile has one apply path,
/// regardless of whether the row arrived via polling or push.
/// </summary>
/// <param name="FlightId">The flight to project.</param>
/// <param name="EmployeeId">
/// The caller whose <c>MyWorkOrder</c> we resolve. Mirrors
/// the list query — every caller sees their own work order, nobody else's.
/// </param>
public sealed record GetFlightSummaryForMobileQuery(
    Guid FlightId,
    Guid EmployeeId) : IQuery<MobileFlightSummaryDto?>;
