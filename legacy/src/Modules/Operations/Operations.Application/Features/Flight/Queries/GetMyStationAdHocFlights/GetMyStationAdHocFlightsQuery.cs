using BuildingBlocks.Application.Abstractions.Queries;
using Operations.Contracts.Mobile;

namespace Operations.Application.Features.Flight.Queries.GetMyStationAdHocFlights;

/// <summary>
/// Mobile "Ad Hoc" tab list. Returns every Scheduled / InProgress flight at the calling
/// employee's home station whose operation type is the seeded Ad Hoc type (scratch-created
/// flights). Same wire shape as <see cref="MobileAogFlightSummaryDto"/> used by the AOG tab endpoint.
/// Only flights whose STA falls within ±<see cref="WindowHours"/> of <c>UtcNow</c> are returned.
/// </summary>
public sealed record GetMyStationAdHocFlightsQuery(
    Guid EmployeeId,
    int WindowHours = 12) : IQuery<IReadOnlyList<MobileAogFlightSummaryDto>>;
