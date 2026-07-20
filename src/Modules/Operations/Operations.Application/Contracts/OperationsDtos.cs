namespace Operations.Application.Contracts;

public sealed record FlightListItemDto(
    Guid Id,
    string FlightNumber,
    string OriginalFlightNumber,
    string? CustomerIataCode,
    string CustomerName,
    string StationIata,
    string OperationTypeName,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    string Status,
    bool IsPerLanding,
    bool IsOnCall);

public sealed record PerLandingExtractionItemDto(
    Guid FlightId,
    Guid WorkOrderId,
    string WorkOrderRowVersion,
    string FlightNumber,
    string? CustomerIataCode,
    string CustomerName,
    string StationIata,
    string OperationTypeName,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc);

/// <summary>
/// Stable, presentation-neutral row used by the supported flight report formats. Durations and
/// display flight numbers are derived by the renderer so CSV can retain machine-friendly values
/// while PDF and Excel use their richer native formatting.
/// </summary>
public sealed record FlightExportRowDto(
    Guid Id,
    string FlightNumber,
    string OriginalFlightNumber,
    string? CustomerIataCode,
    string CustomerName,
    string StationIata,
    string StationName,
    string OperationTypeName,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    string Status,
    bool IsPerLanding,
    IReadOnlyList<string> PlannedServiceNames,
    IReadOnlyList<string> AssignedEmployeeNames,
    ApprovedWorkOrderExportDto? ApprovedWorkOrder);

public sealed record ApprovedWorkOrderExportDto(
    string? ApprovalNumber,
    string ActualFlightNumber,
    DateTimeOffset? ActualArrivalUtc,
    DateTimeOffset? ActualDepartureUtc,
    string? AircraftManufacturer,
    string? AircraftModel,
    string? AircraftTailNumber,
    IReadOnlyList<string> ServiceNames,
    string? Remarks);

public sealed record CalendarFlightDto(
    Guid Id,
    string FlightNumber,
    string? CustomerIataCode,
    string CustomerName,
    string StationIata,
    string StationName,
    string Status,
    bool IsPerLanding,
    bool IsOnCall,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc);

public sealed record PlannedServiceDto(Guid ServiceId, string Name, bool IsAircraftPerLanding);

public sealed record AssignedEmployeeDto(Guid StaffMemberId, string FullName, string EmployeeId);

public sealed record FlightTimelineEntryDto(
    Guid Id,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    string? ActorName,
    string? Details);

public sealed record WorkOrderSummaryDto(
    Guid Id,
    Guid FlightId,
    string Type,
    string Status,
    string? ApprovalNumber,
    Guid OwnerUserId,
    string? OwnerName,
    string RowVersion);

public sealed record FlightDetailDto(
    Guid Id,
    string FlightNumber,
    string OriginalFlightNumber,
    Guid CustomerId,
    string? CustomerIataCode,
    string CustomerName,
    Guid StationId,
    string StationIata,
    string StationName,
    Guid OperationTypeId,
    string OperationTypeName,
    Guid? AircraftTypeId,
    string? AircraftTypeModel,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    string Status,
    bool IsPerLanding,
    bool IsOnCall,
    Guid? ContractId,
    string? ContractNumber,
    Guid? MergedIntoFlightId,
    IReadOnlyList<PlannedServiceDto> PlannedServices,
    IReadOnlyList<AssignedEmployeeDto> AssignedEmployees,
    IReadOnlyList<WorkOrderSummaryDto> WorkOrders,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
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
    int CompletedFlights,
    int CanceledFlights);

public sealed record WorkOrderListItemDto(
    Guid Id,
    Guid FlightId,
    string PlannedFlightNumber,
    string ActualFlightNumber,
    string? CustomerIataCode,
    string CustomerName,
    Guid StationId,
    string StationIata,
    string Type,
    string Status,
    string? ApprovalNumber,
    int? ApprovalSequence,
    Guid OwnerUserId,
    string? OwnerName,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string RowVersion);

public sealed record WorkOrderDetailDto(
    Guid Id,
    Guid FlightId,
    string Type,
    string Status,
    bool IsMergeGenerated,
    Guid? MergedIntoWorkOrderId,
    Guid OwnerUserId,
    string? OwnerName,
    Guid CustomerId,
    string? CustomerIataCode,
    string CustomerName,
    Guid StationId,
    string StationIata,
    string StationName,
    Guid OperationTypeId,
    string OperationTypeName,
    string PlannedFlightNumber,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    string ActualFlightNumber,
    Guid? AircraftTypeId,
    string? AircraftTypeModel,
    string? AircraftTailNumber,
    DateTimeOffset? ActualArrivalUtc,
    DateTimeOffset? ActualDepartureUtc,
    DateTimeOffset? CanceledAtUtc,
    string? CancellationReason,
    string? Remarks,
    WorkOrderSignatureDto? CustomerSignature,
    int? ApprovalSequence,
    string? ApprovalNumber,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAtUtc,
    IReadOnlyList<WorkOrderServiceLineDto> ServiceLines,
    IReadOnlyList<WorkOrderTaskDto> Tasks,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string RowVersion);

public sealed record ApprovedWorkOrderPrintDto(
    WorkOrderDetailDto WorkOrder,
    string? AircraftManufacturer,
    string? ContractNumber,
    IReadOnlyList<WorkOrderPrintStaffDto> Staff,
    byte[]? CustomerSignatureContent,
    string? CustomerSignatureContentType);

public sealed record WorkOrderPrintStaffDto(
    Guid StaffMemberId,
    string? ManpowerTypeName);

public sealed record WorkOrderServiceLineDto(
    Guid Id,
    Guid ServiceId,
    string ServiceName,
    IReadOnlyList<WorkOrderServiceLinePerformerDto> PerformedBy,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string? Description,
    bool IsReturnToRamp)
{
    // Rolling-deployment aliases for installed clients that predate multi-performer services.
    // PerformedBy is authoritative; these can be removed after those clients are retired.
    public Guid PerformedByStaffMemberId => PerformedBy.FirstOrDefault()?.StaffMemberId ?? Guid.Empty;
    public string PerformedByName => PerformedBy.FirstOrDefault()?.FullName ?? string.Empty;
}

public sealed record WorkOrderServiceLinePerformerDto(Guid StaffMemberId, string FullName, string EmployeeId);

public sealed record WorkOrderTaskDto(
    Guid Id,
    string TaskType,
    string? Description,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    IReadOnlyList<WorkOrderTaskEmployeeDto> Employees,
    IReadOnlyList<WorkOrderTaskToolDto> Tools,
    IReadOnlyList<WorkOrderTaskMaterialDto> Materials,
    IReadOnlyList<WorkOrderTaskGeneralSupportDto> GeneralSupports,
    IReadOnlyList<WorkOrderTaskAttachmentDto> Attachments,
    bool IsReturnToRamp);

public sealed record WorkOrderTaskEmployeeDto(Guid StaffMemberId, string FullName, string EmployeeId);

public sealed record WorkOrderTaskToolDto(Guid ToolId, string Name, decimal Quantity);

public sealed record WorkOrderTaskMaterialDto(Guid MaterialId, string Name, decimal Quantity);

public sealed record WorkOrderTaskGeneralSupportDto(Guid GeneralSupportId, string Name, decimal Quantity);

public sealed record WorkOrderTaskAttachmentDto(
    Guid Id,
    string Kind,
    string OriginalFileName,
    string ContentType,
    long Size);

public sealed record WorkOrderSignatureDto(
    string FileName,
    string ContentType,
    long Size,
    DateTimeOffset SignedAtUtc);

public sealed record WorkOrderTimelineEntryDto(
    Guid Id,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    string? ActorName,
    string? Details);
