using System.Text.Json.Serialization;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;

namespace Operations.Application.Contracts;

public enum FlightReportStatus
{
    Scheduled,
    DueSoon,
    ArrivalNotRecorded,
    OnGround,
    ReturnedToRamp,
    Departed,
    Canceled,
    Conflict,
    AutoClosed
}

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
    string StationTimeZoneId,
    string? AircraftTypeModel = null,
    Guid? CustomerId = null,
    string? CustomerLogoVersion = null,
    Guid? StationId = null,
    bool HasArrivalSubmission = false,
    bool HasDepartureSubmission = false,
    bool HasCancellationSubmission = false,
    string Status = nameof(FlightReportStatus.Scheduled),
    [property: JsonConverter(typeof(JsonStringEnumConverter<FlightApprovalStatus>))]
    FlightApprovalStatus ApprovalStatus = FlightApprovalStatus.NotApproved,
    string? ApprovedByDisplayName = null,
    DateTimeOffset? ApprovedAtUtc = null,
    int ApprovedReturnToRampSequence = 0,
    string? WorkOrderNumber = null,
    bool IsAdHocFlight = false,
    DateTimeOffset? ActualArrivalUtc = null,
    DateTimeOffset? ReturnedToRampUtc = null,
    bool IsOnCall = false,
    bool HasOperationalRecords = false,
    DateTimeOffset? ActualDepartureUtc = null,
    DateTimeOffset? CanceledAtUtc = null,
    bool HasWorkOrderRecords = false,
    string? CancellationType = null,
    int ServiceRecordCount = 0,
    int TaskRecordCount = 0);

public sealed record PlannedServiceDto(Guid ServiceId, string Name, bool IsAircraftPerLanding);
public sealed record AssignedEmployeeDto(Guid StaffMemberId, string FullName, string EmployeeId)
{
    internal static AssignedEmployeeDto? MapSubmitter(StaffMemberSnapshot? employee, string? creatorName, bool legacyStaffAttribution)
    {
        if (employee is not null)
            return new(employee.StaffMemberId, employee.FullName, employee.EmployeeId);

        // Older Android clients require an employee object. An empty ID cannot match a staff account.
        return legacyStaffAttribution ? new(Guid.Empty, creatorName ?? "System administrator", string.Empty) : null;
    }
}

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
    string RowVersion,
    string StationTimeZoneId,
    IReadOnlyList<FlightArrivalSubmissionDto>? ArrivalSubmissions = null,
    Guid? CurrentStaffMemberId = null,
    bool CanSaveArrival = false,
    bool HasCancellationSubmission = false,
    IReadOnlyList<FlightDepartureSubmissionDto>? DepartureSubmissions = null,
    bool CanSaveDeparture = false,
    IReadOnlyList<FlightCancellationSubmissionDto>? CancellationSubmissions = null,
    bool CanSaveCancellation = false,
    IReadOnlyList<FlightServiceDto>? Services = null,
    bool CanSaveServices = false,
    IReadOnlyList<Guid>? AllowedServiceIds = null,
    IReadOnlyList<FlightTaskDto>? Tasks = null,
    bool CanSaveTasks = false,
    Guid? CreatedByUserId = null,
    string? CreatedByDisplayName = null,
    bool CanEditFlightDetails = false,
    bool IsAdHocFlight = false,
    bool IsUnassignedSearchFlight = false,
    bool? CanAddServices = null,
    bool? CanAddTasks = null,
    IReadOnlyList<FlightReturnToRampDto>? ReturnToRamps = null,
    bool CanSaveReturnsToRamp = false,
    int LastReturnToRampSequence = 0,
    bool CanShare = false,
    [property: JsonConverter(typeof(JsonStringEnumConverter<FlightApprovalStatus>))]
    FlightApprovalStatus ApprovalStatus = FlightApprovalStatus.NotApproved,
    string? ApprovedByDisplayName = null,
    DateTimeOffset? ApprovedAtUtc = null,
    int ApprovedReturnToRampSequence = 0,
    bool CanApprove = false,
    bool CanUnlock = false,
    string? WorkOrderNumber = null,
    bool CanEditSchedule = false,
    bool IsAutoClosed = false);
