namespace OperationsSystem.Blazor.Client.Api;

// Client-side mirrors of Operations API contracts (the backend stays authoritative).

public sealed record OperationsDashboard(
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    long TotalFlights,
    long FlightsWithPerformedServices,
    IReadOnlyList<DashboardStatusItem> Statuses,
    IReadOnlyList<DashboardBreakdownItem> Stations,
    IReadOnlyList<DashboardBreakdownItem> Customers,
    IReadOnlyList<DashboardBreakdownItem> Services,
    IReadOnlyList<DashboardTrendPoint> Hourly,
    IReadOnlyList<DashboardTrendPoint> Monthly,
    IReadOnlyList<DashboardTrendPoint> Yearly,
    IReadOnlyList<DashboardFilterOption> StationOptions,
    IReadOnlyList<DashboardFilterOption> CustomerOptions,
    IReadOnlyList<DashboardFilterOption> ServiceOptions)
{
    // Compatibility properties keep the personalized home dashboard decoupled from the richer
    // analytics payload used by the dedicated Operations dashboard.
    public long ScheduledFlights => StatusCount("Scheduled");
    public long InProgressFlights => StatusCount("InProgress");
    public long CompletedFlights => StatusCount("Completed");
    public long CanceledFlights => StatusCount("Canceled");

    private long StatusCount(string status) =>
        Statuses.FirstOrDefault(item => string.Equals(item.Status, status, StringComparison.Ordinal))?.FlightCount ?? 0;
}

public sealed record DashboardStatusItem(string Status, long FlightCount, double Percentage);

public sealed record DashboardBreakdownItem(
    Guid? Id,
    string Label,
    string? Code,
    long FlightCount,
    double Percentage,
    bool IsOther,
    int GroupedItemCount);

public sealed record DashboardTrendPoint(string Key, string Label, int SortOrder, long FlightCount);

public sealed record DashboardFilterOption(Guid Id, string Label, string? Code);

public sealed record DashboardFlightRow(
    Guid Id,
    string FlightNumber,
    string? CustomerIataCode,
    string CustomerName,
    Guid StationId,
    string StationIata,
    string StationName,
    string OperationTypeName,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    string Status,
    IReadOnlyList<string> PerformedServiceNames);

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

public sealed record PerLandingExtractionItem(
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

public sealed record PerLandingApprovalSelectionModel(Guid FlightId, Guid WorkOrderId, string RowVersion);

public sealed record ApprovePerLandingFlightsRequestModel(IReadOnlyList<PerLandingApprovalSelectionModel> Selections);

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
    IReadOnlyList<WorkOrderServiceLinePerformerModel> PerformedBy,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string? Description,
    bool IsReturnToRamp = false,
    IReadOnlyList<WorkOrderServiceLineAttachmentModel>? Attachments = null);

public sealed record WorkOrderServiceLinePerformerModel(Guid StaffMemberId, string FullName, string EmployeeId);

public sealed record WorkOrderServiceLineAttachmentModel(Guid Id, string Kind, string OriginalFileName, string ContentType, long Size);

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
    IReadOnlyList<WorkOrderTaskAttachmentModel> Attachments,
    bool IsReturnToRamp = false);

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
    IReadOnlyList<Guid> PerformedByStaffMemberIds,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string? Description,
    bool IsReturnToRamp = false,
    Guid? Id = null,
    IReadOnlyList<WorkOrderServiceLineAttachmentRequestModel>? Attachments = null);

public sealed record WorkOrderServiceLineAttachmentRequestModel(
    string Kind,
    string Base64Content,
    string FileName,
    string ContentType);

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
    IReadOnlyList<WorkOrderTaskAttachmentRequestModel>? Attachments = null,
    bool IsReturnToRamp = false);

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
