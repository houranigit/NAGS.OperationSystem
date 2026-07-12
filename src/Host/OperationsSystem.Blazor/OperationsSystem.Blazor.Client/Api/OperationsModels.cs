namespace OperationsSystem.Blazor.Client.Api;

// Client-side mirrors of Operations API contracts (the backend stays authoritative).

public sealed record FlightListItem(
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

public sealed record CalendarFlight(
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

public sealed record DuplicateCandidate(
    Guid FlightId,
    string FlightNumber,
    string CustomerName,
    string StationIata,
    DateTimeOffset ScheduledArrivalUtc,
    int Score);

public sealed record PlannedServiceModel(Guid ServiceId, string Name, bool IsAircraftPerLanding);
public sealed record AssignedEmployeeModel(Guid StaffMemberId, string FullName, string EmployeeId);

public sealed record FlightTimelineEntryModel(
    Guid Id,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    string? ActorName,
    string? Details);

public sealed record WorkOrderSummaryModel(
    Guid Id,
    Guid FlightId,
    string Type,
    string Status,
    string? ApprovalNumber,
    Guid OwnerUserId,
    string? OwnerName,
    string RowVersion);

public sealed record FlightDetail(
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
    IReadOnlyList<PlannedServiceModel> PlannedServices,
    IReadOnlyList<AssignedEmployeeModel> AssignedEmployees,
    IReadOnlyList<WorkOrderSummaryModel> WorkOrders,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string RowVersion);

public sealed record WorkOrderListItem(
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

public sealed record WorkOrderDetail(
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
    WorkOrderSignatureModel? CustomerSignature,
    int? ApprovalSequence,
    string? ApprovalNumber,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAtUtc,
    IReadOnlyList<WorkOrderServiceLineModel> ServiceLines,
    IReadOnlyList<WorkOrderTaskModel> Tasks,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string RowVersion);

public sealed record WorkOrderServiceLineModel(
    Guid Id,
    Guid ServiceId,
    string ServiceName,
    Guid PerformedByStaffMemberId,
    string PerformedByName,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string? Description);

public sealed record WorkOrderTaskModel(
    Guid Id,
    string TaskType,
    string? Description,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    IReadOnlyList<WorkOrderTaskEmployeeModel> Employees,
    IReadOnlyList<WorkOrderTaskToolModel> Tools,
    IReadOnlyList<WorkOrderTaskMaterialModel> Materials,
    IReadOnlyList<WorkOrderTaskGeneralSupportModel> GeneralSupports,
    IReadOnlyList<WorkOrderTaskAttachmentModel> Attachments);

public sealed record WorkOrderTaskEmployeeModel(Guid StaffMemberId, string FullName, string EmployeeId);

public sealed record WorkOrderTaskToolModel(Guid ToolId, string Name, decimal Quantity);

public sealed record WorkOrderTaskMaterialModel(Guid MaterialId, string Name, decimal Quantity);

public sealed record WorkOrderTaskGeneralSupportModel(Guid GeneralSupportId, string Name, decimal Quantity);

public sealed record WorkOrderTaskAttachmentModel(Guid Id, string Kind, string OriginalFileName, string ContentType, long Size);

public sealed record WorkOrderSignatureModel(string FileName, string ContentType, long Size, DateTimeOffset SignedAtUtc);

public sealed record WorkOrderTimelineEntryModel(
    Guid Id,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    string? ActorName,
    string? Details);

// Request bodies
public sealed record ScheduleFlightRequestModel(
    Guid CustomerId,
    Guid StationId,
    Guid OperationTypeId,
    string FlightNumber,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    Guid? AircraftTypeId,
    IReadOnlyList<Guid> PlannedServiceIds,
    IReadOnlyList<Guid> AssignedStaffMemberIds);

public sealed record ScheduleFlightsRequestModel(
    Guid CustomerId,
    Guid StationId,
    Guid OperationTypeId,
    string FlightNumber,
    TimeOnly ScheduledArrivalTimeUtc,
    TimeOnly ScheduledDepartureTimeUtc,
    IReadOnlyList<DateOnly> SelectedDates,
    Guid? AircraftTypeId,
    IReadOnlyList<Guid> PlannedServiceIds,
    IReadOnlyList<Guid> AssignedStaffMemberIds);

public sealed record UpdateScheduledFlightRequestModel(
    Guid CustomerId,
    Guid StationId,
    Guid OperationTypeId,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    Guid? AircraftTypeId,
    IReadOnlyList<Guid> PlannedServiceIds);

public sealed record ChangeFlightNumberRequestModel(string FlightNumber);

public sealed record AssignEmployeesRequestModel(IReadOnlyList<Guid> StaffMemberIds);

public sealed record MergeFlightsRequestModel(Guid SurvivorFlightId, Guid LoserFlightId);

public sealed record CreateAdHocWorkOrderRequestModel(
    Guid CustomerId,
    Guid StationId,
    string FlightNumber,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    Guid? AircraftTypeId,
    IReadOnlyList<Guid> PlannedServiceIds,
    IReadOnlyList<Guid> AssignedStaffMemberIds,
    WorkOrderRequestModel WorkOrder);

public sealed record WorkOrderRequestModel(
    string Type,
    string? ActualFlightNumber,
    Guid? AircraftTypeId,
    string? AircraftTailNumber,
    DateTimeOffset? ActualArrivalUtc,
    DateTimeOffset? ActualDepartureUtc,
    DateTimeOffset? CanceledAtUtc,
    string? CancellationReason,
    string? Remarks,
    IReadOnlyList<WorkOrderServiceLineRequestModel> ServiceLines,
    IReadOnlyList<WorkOrderTaskRequestModel> Tasks,
    WorkOrderSignatureRequestModel? CustomerSignature = null);

public sealed record MergeWorkOrdersRequestModel(
    IReadOnlyList<Guid> SourceWorkOrderIds,
    WorkOrderRequestModel WorkOrder,
    bool ApproveImmediately);

public sealed record WorkOrderServiceLineRequestModel(
    Guid ServiceId,
    Guid PerformedByStaffMemberId,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string? Description);

public sealed record WorkOrderTaskRequestModel(
    Guid? Id,
    string TaskType,
    string? Description,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    IReadOnlyList<Guid> EmployeeIds,
    IReadOnlyList<WorkOrderTaskToolRequestModel> Tools,
    IReadOnlyList<WorkOrderTaskMaterialRequestModel> Materials,
    IReadOnlyList<WorkOrderTaskGeneralSupportRequestModel> GeneralSupports,
    IReadOnlyList<WorkOrderTaskAttachmentRequestModel>? Attachments = null);

public sealed record WorkOrderTaskToolRequestModel(Guid ToolId, decimal Quantity);

public sealed record WorkOrderTaskMaterialRequestModel(Guid MaterialId, decimal Quantity);

public sealed record WorkOrderTaskGeneralSupportRequestModel(Guid GeneralSupportId, decimal Quantity);

public sealed record WorkOrderTaskAttachmentRequestModel(
    string Kind,
    string Base64Content,
    string FileName,
    string ContentType);

public sealed record WorkOrderSignatureRequestModel(
    string Base64Content,
    string FileName,
    string ContentType);

public sealed record ReturnWorkOrderRequestModel(string Reason);
