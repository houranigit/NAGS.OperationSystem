using Core.Contracts.Features.AircraftType;
using Core.Contracts.Features.Employee;
using Core.Contracts.Features.Service;
using Operations.Contracts.WorkOrder;
using Operations.Domain.Enumerations;

namespace Operations.Contracts.Mobile;

/// <summary>
/// Bundles everything the mobile flight-actions screen needs to decide which downstream
/// flow to open and to populate any lookups the work-order forms need.
/// </summary>
/// <param name="Flight">Read-only summary of the flight (header, schedule, status).</param>
/// <param name="MyWorkOrder">
/// The caller's own under-review work order on this flight, if any.  Resolved by the
/// query handler via <c>WorkOrder.CreatedByEmployeeId == myEmployeeId &amp;&amp; Status == UnderReview</c>.
/// Approved work orders are <b>not</b> exposed here; once an approval is in place
/// the flight is considered settled and only the portal can revoke it.
/// </param>
/// <param name="OtherWorkOrdersExist">
/// True if there is at least one work order on this flight that does <b>not</b> belong to
/// the caller — used by the UI to switch from "Update / Return to ramp" to "Create work
/// order" mode (the portal merges or selects which one to apply).
/// </param>
/// <param name="AircraftTypes">All aircraft types — the create/update form uses these.</param>
/// <param name="Services">Active non-AOG services for the work-order create/update forms.</param>
/// <param name="AssignedEmployees">
/// Snapshot of the flight's currently assigned employees (so the create-WO and
/// return-to-ramp screens can pick from the planned crew).
/// </param>
public sealed record MobileFlightContextDto(
    MobileFlightDetailDto Flight,
    MobileMyWorkOrderDto? MyWorkOrder,
    bool OtherWorkOrdersExist,
    IReadOnlyList<AircraftTypeSnapshot> AircraftTypes,
    IReadOnlyList<ServiceSnapshot> Services,
    IReadOnlyList<EmployeeSnapshot> AssignedEmployees);

/// <summary>Compact flight detail returned inside the mobile context payload.</summary>
public sealed record MobileFlightDetailDto(
    Guid Id,
    string FlightNumber,
    string CustomerName,
    string CustomerIataCode,
    Guid CustomerId,
    Guid StationId,
    string StationCode,
    string OperationTypeCode,
    Guid OperationTypeId,
    DateTimeOffset Sta,
    DateTimeOffset Std,
    Guid? AircraftTypeId,
    string? AircraftModel,
    FlightStatus Status,
    DateTimeOffset? CanceledAt,
    bool IsAcceptedWorkOrderInPlace);

/// <summary>
/// Slim snapshot of the caller's under-review work order on this flight, including its
/// existing line collections so the "Update" tab can pre-fill and the "Return to ramp"
/// tab can list what is already there.
/// </summary>
public sealed record MobileMyWorkOrderDto(
    Guid Id,
    WorkOrderStatus Status,
    Guid? AircraftTypeId,
    string? AircraftTailNumber,
    DateTimeOffset? Ata,
    DateTimeOffset? Atd,
    bool IsCanceled,
    DateTimeOffset? CanceledAt,
    string? Remarks,
    IReadOnlyList<WorkOrderServiceLineDto> ServiceLines,
    IReadOnlyList<WorkOrderTaskDto> Tasks,
    byte[]? CustomerSignature = null);
