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

public sealed record ScheduleFlightsRequest(
    Guid CustomerId,
    Guid StationId,
    Guid OperationTypeId,
    string FlightNumber,
    TimeOnly ScheduledArrivalTimeUtc,
    TimeOnly ScheduledDepartureTimeUtc,
    IReadOnlyList<DateOnly>? SelectedDates,
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

public sealed record AssignEmployeesRequest(IReadOnlyList<Guid>? StaffMemberIds);

public sealed record MergeFlightsRequest(Guid SurvivorFlightId, Guid LoserFlightId);
