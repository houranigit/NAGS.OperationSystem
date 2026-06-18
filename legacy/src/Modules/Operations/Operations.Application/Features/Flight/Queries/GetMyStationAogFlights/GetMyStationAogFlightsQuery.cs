using BuildingBlocks.Application.Abstractions.Queries;
using Operations.Contracts.Mobile;

namespace Operations.Application.Features.Flight.Queries.GetMyStationAogFlights;

/// <summary>
/// Mobile "AOG" tab list. Returns every Scheduled / InProgress flight at the calling
/// employee's home station whose contract services include the AOG service — i.e. flights
/// any station employee may claim and work on, even when not yet assigned. Ad Hoc
/// operation-type flights are excluded. AOG volume is
/// low by definition so the response is unpaged. Only flights whose STA falls within
/// ±<see cref="WindowHours"/> of <c>UtcNow</c> are returned.
/// </summary>
public sealed record GetMyStationAogFlightsQuery(
    Guid EmployeeId,
    int WindowHours = 12) : IQuery<IReadOnlyList<MobileAogFlightSummaryDto>>;
