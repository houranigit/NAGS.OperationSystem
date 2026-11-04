namespace Operations.Application.Contracts;

public sealed record FlightListItemDto(
    Guid Id,
    string FlightNumber,
    string? CustomerIataCode,
    string CustomerName,
    string StationIataCode,
    string OperationTypeName,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    bool IsPerLanding,
    string? AircraftTypeModel = null,
    Guid? CustomerId = null);

public sealed record PlannedServiceDto(Guid ServiceId, string Name, bool IsAircraftPerLanding);
public sealed record AssignedEmployeeDto(Guid StaffMemberId, string FullName, string EmployeeId);

public sealed record FlightDetailDto(
    Guid Id,
    string FlightNumber,
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
    bool IsPerLanding,
    IReadOnlyList<PlannedServiceDto> PlannedServices,
    IReadOnlyList<AssignedEmployeeDto> AssignedEmployees,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string RowVersion);
