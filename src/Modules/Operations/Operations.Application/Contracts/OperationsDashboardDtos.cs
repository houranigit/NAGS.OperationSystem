namespace Operations.Application.Contracts;

public sealed record OperationsDashboardDto(
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    long TotalFlights,
    long FlightsWithPerformedServices,
    IReadOnlyList<DashboardStatusItemDto> Statuses,
    IReadOnlyList<DashboardBreakdownItemDto> Stations,
    IReadOnlyList<DashboardBreakdownItemDto> Customers,
    IReadOnlyList<DashboardBreakdownItemDto> Services,
    IReadOnlyList<DashboardBreakdownItemDto> OperationTypes,
    IReadOnlyList<DashboardBreakdownItemDto> ServiceCategories,
    IReadOnlyList<DashboardTimelinePointDto> Timeline,
    string TimelineGranularity,
    IReadOnlyList<DashboardTrendPointDto> Hourly,
    IReadOnlyList<DashboardTrendPointDto> Monthly,
    IReadOnlyList<DashboardTrendPointDto> Yearly,
    IReadOnlyList<DashboardFilterOptionDto> StationOptions,
    IReadOnlyList<DashboardFilterOptionDto> CustomerOptions,
    IReadOnlyList<DashboardFilterOptionDto> ServiceOptions);

public sealed record DashboardStatusItemDto(string Status, long FlightCount, double Percentage);

public sealed record DashboardBreakdownItemDto(
    Guid? Id, string Label, string? Code, long FlightCount, double Percentage, bool IsOther, int GroupedItemCount);

public sealed record DashboardTrendPointDto(string Key, string Label, int SortOrder, long FlightCount);

public sealed record DashboardTimelinePointDto(DateTimeOffset BucketUtc, long FlightCount);

public sealed record DashboardFilterOptionDto(Guid Id, string Label, string? Code);

public sealed record DashboardFlightRowDto(
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
    IReadOnlyList<string> PerformedServiceNames,
    string? WorkOrderNumber,
    string WorkOrderStatus);
