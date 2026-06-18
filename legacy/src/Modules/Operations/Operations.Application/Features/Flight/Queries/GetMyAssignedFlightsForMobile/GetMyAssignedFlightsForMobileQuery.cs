using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Operations.Contracts.Mobile;
using Operations.Domain.Enumerations;

namespace Operations.Application.Features.Flight.Queries.GetMyAssignedFlightsForMobile;

/// <summary>
/// Mobile "Scheduled" tab list. Returns flights where the calling employee is on the
/// assigned-employee roster and the flight is not Completed / Canceled. AOG flights
/// (those whose contract services include the AOG service) are filtered out by default —
/// the mobile app shows them on a dedicated AOG tab where any station employee can serve
/// them without being assigned. Ad Hoc operation-type flights (mobile scratch-created) are
/// never included. Results are ordered by STA ascending and capped at
/// <see cref="PageSize"/> (max 100). Flights outside the ±<see cref="WindowHours"/>
/// STA window around <c>UtcNow</c> are excluded.
/// </summary>
/// <param name="Status">
/// Optional filter on <see cref="FlightStatus"/>. When <c>null</c>, the query returns
/// Scheduled and InProgress flights only — Completed and Canceled flights are filtered out
/// regardless. Sent by the mobile UI as the user toggles between "All / Scheduled / In progress" chips.
/// </param>
/// <param name="IncludeAog">
/// When <c>false</c> (default), strips flights whose contract services contain the AOG
/// service. The mobile UI calls with <c>false</c> for the Non-AOG / Scheduled tab; the
/// flag exists so admin tools can opt in to the full assigned roster.
/// </param>
public sealed record GetMyAssignedFlightsForMobileQuery(
    Guid EmployeeId,
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    int WindowHours = 12,
    FlightStatus? Status = null,
    bool IncludeAog = false) : IQuery<PaginatedResult<MobileFlightSummaryDto>>;
