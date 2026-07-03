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

public sealed record CancelFlightRequestModel(DateTimeOffset CanceledAtUtc, string? Reason);
