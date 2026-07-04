namespace Operations.Application.Contracts;

public sealed record FlightListItemDto(
    Guid Id,
    string FlightNumber,
    string OriginalFlightNumber,
    string CustomerName,
    string StationIata,
    string OperationTypeName,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    string Status,
    bool IsPerLanding);

public sealed record CalendarFlightDto(
    Guid Id,
    string FlightNumber,
    string CustomerName,
    string Status,
    bool IsPerLanding,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc);

public sealed record PlannedServiceDto(Guid ServiceId, string Name, bool IsAircraftPerLanding);

public sealed record AssignedEmployeeDto(Guid StaffMemberId, string FullName, string EmployeeId);

public sealed record WorkOrderSummaryDto(
    Guid Id,
    string Type,
    string Status,
    string? Number,
    Guid? OwnerStaffMemberId,
    string? OwnerName,
    DateTimeOffset CreatedAtUtc);

/// <summary>The approved work order values captured onto the flight (billing-ready scalars). The
/// actual service lines/tasks are read from the referenced approved work order.</summary>
public sealed record ApprovedWorkOrderDto(
    Guid WorkOrderId,
    string WorkOrderNumber,
    string WorkOrderType,
    string ActualFlightNumber,
    Guid? ActualAircraftTypeId,
    string? ActualAircraftTypeModel,
    string? AircraftTailNumber,
    DateTimeOffset? ActualArrivalUtc,
    DateTimeOffset? ActualDepartureUtc,
    string? Remarks,
    string? CustomerSignatureReference,
    DateTimeOffset? CanceledAtUtc,
    string? CancellationReason,
    DateTimeOffset ApprovedAtUtc);

public sealed record FlightTimelineEntryDto(
    Guid Id,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    string? ActorName,
    Guid? WorkOrderId,
    string? WorkOrderNumber,
    string? Details);

public sealed record FlightDetailDto(
    Guid Id,
    string FlightNumber,
    string OriginalFlightNumber,
    Guid CustomerId,
    string CustomerName,
    Guid StationId,
    string StationIata,
    Guid OperationTypeId,
    string OperationTypeName,
    Guid? AircraftTypeId,
    string? AircraftTypeModel,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    string Status,
    bool IsPerLanding,
    Guid? ContractId,
    string? ContractNumber,
    Guid? MergedIntoFlightId,
    Guid? PotentialDuplicateOfFlightId,
    ApprovedWorkOrderDto? ApprovedWorkOrder,
    IReadOnlyList<PlannedServiceDto> PlannedServices,
    IReadOnlyList<AssignedEmployeeDto> AssignedEmployees,
    IReadOnlyList<WorkOrderSummaryDto> WorkOrders,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string RowVersion);

public sealed record WorkOrderServiceLineDto(
    Guid Id,
    Guid ServiceId,
    string ServiceName,
    string Origin,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string? Description,
    bool ReturnToRamp,
    IReadOnlyList<AssignedEmployeeDto> Employees);

public sealed record WorkOrderResourceDto(Guid Id, string Name, decimal Quantity);

public sealed record WorkOrderTaskDto(
    Guid Id,
    string TaskType,
    string? Description,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    bool ReturnToRamp,
    IReadOnlyList<AssignedEmployeeDto> Employees,
    IReadOnlyList<WorkOrderResourceDto> Tools,
    IReadOnlyList<WorkOrderResourceDto> Materials,
    IReadOnlyList<WorkOrderResourceDto> GeneralSupports);

public sealed record WorkOrderDetailDto(
    Guid Id,
    Guid FlightId,
    string Type,
    string Status,
    string? Number,
    Guid? OwnerStaffMemberId,
    string? OwnerName,
    string FlightNumber,
    string CustomerName,
    string StationIata,
    Guid? AircraftTypeId,
    string? AircraftTypeModel,
    string? AircraftTailNumber,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    DateTimeOffset? ActualArrivalUtc,
    DateTimeOffset? ActualDepartureUtc,
    string? Remarks,
    string? CustomerSignatureReference,
    DateTimeOffset? CanceledAtUtc,
    string? CancellationReason,
    IReadOnlyList<WorkOrderServiceLineDto> ServiceLines,
    IReadOnlyList<WorkOrderTaskDto> Tasks,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string RowVersion);

public sealed record ReviewQueueItemDto(
    Guid WorkOrderId,
    Guid FlightId,
    string FlightNumber,
    string StationIata,
    string CustomerName,
    string Type,
    string Status,
    DateTimeOffset CreatedAtUtc,
    string RowVersion);

public sealed record DuplicateCandidateDto(
    Guid FlightId,
    string FlightNumber,
    string CustomerName,
    string StationIata,
    DateTimeOffset ScheduledArrivalUtc,
    int Score);

public sealed record OperationsDashboardDto(
    int ScheduledFlights,
    int InProgressFlights,
    int PendingReviewWorkOrders,
    int CompletedFlights,
    int CanceledFlights);
