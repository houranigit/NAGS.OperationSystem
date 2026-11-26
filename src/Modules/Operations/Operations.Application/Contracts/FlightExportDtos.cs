using MasterData.Contracts.Resources;

namespace Operations.Application.Contracts;

public sealed record FlightExportRowDto(
    Guid Id,
    string FlightNumber,
    string? CustomerIataCode,
    string CustomerName,
    string StationIata,
    string StationName,
    string OperationTypeName,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    string StationTimeZoneId,
    string Status,
    bool IsPerLanding,
    IReadOnlyList<string> PlannedServiceNames,
    IReadOnlyList<string> AssignedEmployeeNames,
    ApprovedWorkOrderExportDto? ApprovedWorkOrder)
{
    public Guid CustomerId { get; init; }
    public Guid StationId { get; init; }
    public Guid OperationTypeId { get; init; }
    public IReadOnlyList<Guid> PlannedServiceIds { get; init; } = [];
    public IReadOnlyList<Guid> AssignedStaffMemberIds { get; init; } = [];
}

// Legacy report naming. This also includes recorded work that has not been approved.
public sealed record ApprovedWorkOrderExportDto(
    string? ApprovalNumber,
    string ActualFlightNumber,
    DateTimeOffset? ActualArrivalUtc,
    DateTimeOffset? ActualDepartureUtc,
    string? AircraftManufacturer,
    string? AircraftModel,
    string? AircraftTailNumber,
    IReadOnlyList<string> ServiceNames,
    IReadOnlyList<string> ToolNames,
    IReadOnlyList<string> MaterialNames,
    IReadOnlyList<string> GeneralSupportNames,
    string? Remarks)
{
    public Guid? AircraftTypeId { get; init; }
    public string WorkOrderStatus { get; init; } = string.Empty;
    public IReadOnlyList<string> TaskNames { get; init; } = [];
    public IReadOnlyList<FlightExportServiceDetailDto> ServiceDetails { get; init; } = [];
    public IReadOnlyList<FlightExportTaskDetailDto> TaskDetails { get; init; } = [];
    public IReadOnlyList<FlightExportManpowerDetailDto> ManpowerDetails { get; init; } = [];
}

public sealed record FlightExportManpowerDetailDto(string Name, DateTimeOffset FromUtc, DateTimeOffset? ToUtc);

public sealed record FlightExportReturnToRampContextDto(
    Guid Id, int Sequence, DateTimeOffset FromUtc, DateTimeOffset? ToUtc, string? Description);

public sealed record FlightExportServiceDetailDto(
    Guid Id, Guid ServiceId, string ServiceName, DateTimeOffset FromUtc, DateTimeOffset? ToUtc,
    IReadOnlyList<string> PerformedByNames, string? Description, FlightExportReturnToRampContextDto? ReturnToRamp);

public sealed record FlightExportResourceUsageDto(
    Guid ResourceId, string Name, ResourceCalculationType CalculationType, decimal? Quantity,
    DateTimeOffset? FromUtc, DateTimeOffset? ToUtc);

public sealed record FlightExportTaskDetailDto(
    Guid Id, string TaskType, string? Description, DateTimeOffset FromUtc, DateTimeOffset? ToUtc,
    IReadOnlyList<string> PerformedByNames, IReadOnlyList<FlightExportResourceUsageDto> Tools,
    IReadOnlyList<FlightExportResourceUsageDto> Materials, IReadOnlyList<FlightExportResourceUsageDto> GeneralSupports,
    FlightExportReturnToRampContextDto? ReturnToRamp);
