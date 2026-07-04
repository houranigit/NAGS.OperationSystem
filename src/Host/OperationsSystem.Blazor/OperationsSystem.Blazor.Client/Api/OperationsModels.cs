namespace OperationsSystem.Blazor.Client.Api;

// Client-side mirrors of Operations API contracts (the backend stays authoritative).

public sealed record FlightListItem(
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

public sealed record CalendarFlight(
    Guid Id,
    string FlightNumber,
    string CustomerName,
    string Status,
    bool IsPerLanding,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc);

public sealed record ReviewQueueItem(
    Guid WorkOrderId,
    Guid FlightId,
    string FlightNumber,
    string StationIata,
    string CustomerName,
    string Type,
    string Status,
    DateTimeOffset CreatedAtUtc,
    string RowVersion);

public sealed record DuplicateCandidate(
    Guid FlightId,
    string FlightNumber,
    string CustomerName,
    string StationIata,
    DateTimeOffset ScheduledArrivalUtc,
    int Score);

public sealed record WorkOrderServiceLineModel(
    Guid Id,
    Guid ServiceId,
    string ServiceName,
    string Origin,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string? Description,
    bool ReturnToRamp,
    IReadOnlyList<AssignedEmployeeModel> Employees);

public sealed record WorkOrderResourceModel(Guid Id, string Name, decimal Quantity);

public sealed record WorkOrderTaskModel(
    Guid Id,
    string TaskType,
    string? Description,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    bool ReturnToRamp,
    IReadOnlyList<AssignedEmployeeModel> Employees,
    IReadOnlyList<WorkOrderResourceModel> Tools,
    IReadOnlyList<WorkOrderResourceModel> Materials,
    IReadOnlyList<WorkOrderResourceModel> GeneralSupports);

public sealed record WorkOrderDetail(
    Guid Id,
    Guid FlightId,
    string Type,
    string Status,
    string? Number,
    string FlightNumber,
    string CustomerName,
    string StationIata,
    string? AircraftTailNumber,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    DateTimeOffset? ActualArrivalUtc,
    DateTimeOffset? ActualDepartureUtc,
    string? Remarks,
    string? CustomerSignatureReference,
    DateTimeOffset? CanceledAtUtc,
    string? CancellationReason,
    IReadOnlyList<WorkOrderServiceLineModel> ServiceLines,
    IReadOnlyList<WorkOrderTaskModel> Tasks,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string RowVersion);

public sealed record OperationsDashboard(
    int ScheduledFlights,
    int InProgressFlights,
    int PendingReviewWorkOrders,
    int CompletedFlights,
    int CanceledFlights);

public sealed record PlannedServiceModel(Guid ServiceId, string Name, bool IsAircraftPerLanding);
public sealed record AssignedEmployeeModel(Guid StaffMemberId, string FullName, string EmployeeId);
public sealed record WorkOrderSummaryModel(Guid Id, string Type, string Status, string? Number);

public sealed record FlightDetail(
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
    IReadOnlyList<PlannedServiceModel> PlannedServices,
    IReadOnlyList<AssignedEmployeeModel> AssignedEmployees,
    IReadOnlyList<WorkOrderSummaryModel> WorkOrders,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string RowVersion);

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

public sealed record CancelFlightRequestModel(DateTimeOffset CanceledAtUtc, string? Reason);

public sealed record CreateAdHocFlightRequestModel(
    Guid CustomerId,
    Guid OperationTypeId,
    string FlightNumber,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    Guid? AircraftTypeId,
    IReadOnlyList<Guid> PlannedServiceIds,
    bool AcknowledgeDuplicates);

public sealed record AdHocFlightResultModel(
    Guid FlightId,
    Guid WorkOrderId,
    IReadOnlyList<DuplicateCandidate> DuplicateCandidates);

public enum WorkOrderServiceLineOriginModel
{
    Planned,
    Extra
}

public enum WorkOrderTaskTypeModel
{
    Major,
    Minor
}

public sealed record ServiceLineRequestModel(
    Guid ServiceId,
    WorkOrderServiceLineOriginModel Origin,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string? Description,
    bool ReturnToRamp,
    IReadOnlyList<Guid> EmployeeIds);

public sealed record ResourceUsageRequestModel(Guid Id, decimal Quantity);

public sealed record TaskRequestModel(
    WorkOrderTaskTypeModel TaskType,
    string? Description,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    bool ReturnToRamp,
    IReadOnlyList<Guid> EmployeeIds,
    IReadOnlyList<ResourceUsageRequestModel> Tools,
    IReadOnlyList<ResourceUsageRequestModel> Materials,
    IReadOnlyList<ResourceUsageRequestModel> GeneralSupports,
    IReadOnlyList<object> Attachments);

public sealed record UpdateWorkOrderRequestModel(
    IReadOnlyList<ServiceLineRequestModel> ServiceLines,
    IReadOnlyList<TaskRequestModel> Tasks,
    DateTimeOffset? ActualArrivalUtc,
    DateTimeOffset? ActualDepartureUtc,
    string? AircraftTailNumber,
    string? Remarks,
    string? CustomerSignatureReference);

public sealed record MergeFlightsRequestModel(Guid SurvivorFlightId, Guid LoserFlightId);

public sealed record MergeWorkOrdersRequestModel(Guid SurvivorWorkOrderId, Guid LoserWorkOrderId);
