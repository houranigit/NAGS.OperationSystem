using Operations.Application.Features.WorkOrders;

namespace Operations.Api.Endpoints;

public sealed record ScheduleFlightRequest(
    Guid CustomerId,
    Guid StationId,
    Guid OperationTypeId,
    string FlightNumber,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    Guid? AircraftTypeId,
    IReadOnlyList<Guid>? PlannedServiceIds,
    IReadOnlyList<Guid>? AssignedStaffMemberIds);

public sealed record UpdateScheduledFlightRequest(
    Guid CustomerId,
    Guid StationId,
    Guid OperationTypeId,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    Guid? AircraftTypeId,
    IReadOnlyList<Guid>? PlannedServiceIds);

public sealed record ChangeFlightNumberRequest(string FlightNumber);

public sealed record AssignEmployeesRequest(IReadOnlyList<Guid> StaffMemberIds);

public sealed record CancelFlightRequest(DateTimeOffset CanceledAtUtc, string? Reason);

public sealed record CreateAdHocFlightRequest(
    Guid CustomerId,
    Guid OperationTypeId,
    string FlightNumber,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    Guid? AircraftTypeId,
    IReadOnlyList<Guid>? PlannedServiceIds,
    bool AcknowledgeDuplicates,
    bool IsCancellation,
    DateTimeOffset? CancellationAtUtc,
    string? CancellationReason,
    string? ActualFlightNumber,
    Guid? ActualAircraftTypeId,
    string? AircraftTailNumber,
    DateTimeOffset? ActualArrivalUtc,
    DateTimeOffset? ActualDepartureUtc,
    IReadOnlyList<ServiceLineRequest>? ServiceLines,
    IReadOnlyList<TaskRequest>? Tasks,
    string? Remarks,
    string? CustomerSignatureReference);

public sealed record MergeFlightsRequest(Guid SurvivorFlightId, Guid LoserFlightId);

public sealed record UpdateWorkOrderRequest(
    IReadOnlyList<ServiceLineRequest>? ServiceLines,
    IReadOnlyList<TaskRequest>? Tasks,
    string? ActualFlightNumber,
    Guid? ActualAircraftTypeId,
    DateTimeOffset? ActualArrivalUtc,
    DateTimeOffset? ActualDepartureUtc,
    string? AircraftTailNumber,
    string? Remarks,
    string? CustomerSignatureReference);

public sealed record MergeWorkOrdersRequest(Guid SurvivorWorkOrderId, Guid LoserWorkOrderId);
