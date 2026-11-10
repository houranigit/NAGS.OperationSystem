namespace OperationsSystem.Blazor.Client.Api;

// Client-side mirrors of flight scheduling API contracts.

public sealed record FlightListItem(
    Guid Id,
    string FlightNumber,
    string? CustomerIataCode,
    string CustomerName,
    string StationIataCode,
    string OperationTypeName,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    bool IsPerLanding,
    string StationTimeZoneId,
    Guid? StationId = null);

public sealed record PlannedServiceModel(Guid ServiceId, string Name, bool IsAircraftPerLanding);
public sealed record AssignedEmployeeModel(Guid StaffMemberId, string FullName, string EmployeeId);

public sealed record FlightDetail(
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
    IReadOnlyList<PlannedServiceModel> PlannedServices,
    IReadOnlyList<AssignedEmployeeModel> AssignedEmployees,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string RowVersion,
    string StationTimeZoneId);

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
    IReadOnlyList<Guid> AssignedStaffMemberIds,
    string? TimeZoneId = null);

public sealed record UpdateScheduledFlightRequestModel(
    Guid CustomerId,
    Guid StationId,
    Guid OperationTypeId,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    Guid? AircraftTypeId,
    IReadOnlyList<Guid> PlannedServiceIds,
    IReadOnlyList<Guid>? AssignedStaffMemberIds = null);

public sealed record ChangeFlightNumberRequestModel(string FlightNumber);
