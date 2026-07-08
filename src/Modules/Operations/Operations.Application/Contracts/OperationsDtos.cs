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

public sealed record FlightTimelineEntryDto(
    Guid Id,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    string? ActorName,
    string? Details);

public sealed record FlightDetailDto(
    Guid Id,
    string FlightNumber,
    string OriginalFlightNumber,
    Guid CustomerId,
    string? CustomerIataCode,
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
    IReadOnlyList<PlannedServiceDto> PlannedServices,
    IReadOnlyList<AssignedEmployeeDto> AssignedEmployees,
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
