using Operations.Domain.Enumerations;

namespace Operations.Contracts.Mobile;

/// <summary>
/// Lean projection of a flight for the mobile list (one row per flight). Includes the
/// status fields the UI needs to colour-code each card and decide which downstream flow
/// to open. <see cref="MyWorkOrder"/> reflects the caller's <b>own</b> under-review work
/// order on this flight (filtered by CreatedByEmployeeId in the query); other
/// employees' work orders are deliberately not exposed.
/// <para>
/// <see cref="Services"/> carries the flight's contract services (the immutable
/// snapshot copied off the contract at flight creation, see
/// <c>Operations.Domain.Entities.FlightService</c>). The mobile UI uses this to badge
/// "AOG / Cargo / Catering / …" chips on every flight card and to decide work-order
/// service-line eligibility while offline.
/// </para>
/// <para>
/// <see cref="MyWorkOrder"/> is the caller's own under-review work order on this flight
/// (same resolution rules as <see cref="MobileFlightContextDto.MyWorkOrder"/>). When
/// present, mobile can open the update flow without a separate fetch.
/// </para>
/// </summary>
public sealed record MobileFlightSummaryDto(
    Guid Id,
    string FlightNumber,
    string CustomerName,
    string CustomerIataCode,
    string StationCode,
    string OperationTypeCode,
    DateTimeOffset Sta,
    DateTimeOffset Std,
    string? AircraftModel,
    FlightStatus Status,
    DateTimeOffset? CanceledAt,
    int AssignedEmployeesCount,
    MobileMyWorkOrderDto? MyWorkOrder,
    bool OtherWorkOrdersExist,
    IReadOnlyList<MobileFlightServiceDto> Services,
    IReadOnlyList<MobileFlightAssignedEmployeeDto> AssignedEmployees);

/// <summary>
/// Lean projection of one employee assigned to a flight. Mobile caches this list on the
/// flight row so the invite screen can show "already assigned" colleagues offline and the
/// invite picker can exclude them. Only the fields the UI renders are included
/// (id for selection/exclusion, name + role for display).
/// </summary>
public sealed record MobileFlightAssignedEmployeeDto(
    Guid EmployeeId,
    string FullName,
    string ManpowerTypeName);

/// <summary>
/// One contract service attached to a flight (lean projection of the server's
/// <c>FlightService</c> child entity). <see cref="IsAog"/> is folded into the row
/// so the mobile UI can render the AOG chip / route to the AOG flow without
/// re-checking against the AOG seed id.
/// </summary>
public sealed record MobileFlightServiceDto(
    Guid ServiceId,
    string Name,
    bool IsAog);

/// <summary>
/// Projection used by the AOG-flights endpoint. Same shape as
/// <see cref="MobileFlightSummaryDto"/> with the per-flight <c>Services</c> list
/// deliberately removed: an AOG flight on the mobile AOG tab is, by definition,
/// always served by the AOG service, so the chip list adds no information. Keeping
/// the AOG response lean also lets the query handler skip the extra JOIN against
/// <c>FlightService</c>.
/// </summary>
public sealed record MobileAogFlightSummaryDto(
    Guid Id,
    string FlightNumber,
    string CustomerName,
    string CustomerIataCode,
    string StationCode,
    string OperationTypeCode,
    DateTimeOffset Sta,
    DateTimeOffset Std,
    string? AircraftModel,
    FlightStatus Status,
    DateTimeOffset? CanceledAt,
    int AssignedEmployeesCount,
    MobileMyWorkOrderDto? MyWorkOrder,
    bool OtherWorkOrdersExist);
