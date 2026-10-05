using System.Globalization;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Contracts;
using Operations.Application.Features.Flights;
using Operations.Application.Features.WorkOrders;
using Operations.Domain.Enumerations;
using Operations.Domain.Flights;
using Operations.Domain.WorkOrders;

namespace Operations.Application.Features.Dashboard;

public sealed record GetOperationsDashboardQuery(
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    IReadOnlyList<Guid>? StationIds = null,
    IReadOnlyList<Guid>? CustomerIds = null,
    IReadOnlyList<Guid>? ServiceIds = null,
    int TopCount = 5,
    bool IncludeAnalytics = true,
    bool IncludeOptions = true,
    string? TimeZoneId = null) : IQuery<OperationsDashboardDto>;

public sealed record GetDashboardFlightsQuery(
    int Page = 1,
    int PageSize = 20,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    IReadOnlyList<Guid>? StationIds = null,
    IReadOnlyList<Guid>? CustomerIds = null,
    IReadOnlyList<Guid>? ServiceIds = null,
    string? Sort = null) : IQuery<PagedResult<DashboardFlightRowDto>>;

/// <summary>
/// Returns every dashboard flight row matching the authorized filters. Pagination is intentionally
/// absent because this query is consumed only by the dashboard's dedicated export endpoint.
/// </summary>
public sealed record GetDashboardFlightsExportQuery(
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    IReadOnlyList<Guid>? StationIds = null,
    IReadOnlyList<Guid>? CustomerIds = null,
    IReadOnlyList<Guid>? ServiceIds = null,
    string? Sort = null) : IQuery<IReadOnlyList<FlightExportRowDto>>;

public sealed class GetOperationsDashboardQueryHandler
    : IQueryHandler<GetOperationsDashboardQuery, OperationsDashboardDto>
{
    private readonly IOperationsDbContext _db;
    private readonly IOperationsScope _scope;
    private readonly TimeProvider _timeProvider;

    public GetOperationsDashboardQueryHandler(IOperationsDbContext db, IOperationsScope scope)
        : this(db, scope, TimeProvider.System)
    {
    }

    public GetOperationsDashboardQueryHandler(
        IOperationsDbContext db,
        IOperationsScope scope,
        TimeProvider timeProvider)
    {
        _db = db;
        _scope = scope;
        _timeProvider = timeProvider;
    }

    public async Task<Result<OperationsDashboardDto>> Handle(GetOperationsDashboardQuery request, CancellationToken cancellationToken)
    {
        if (DashboardQueryValidation.ValidateDates(request.FromUtc, request.ToUtc) is { } dateError)
            return dateError;
        if (!DashboardQueryValidation.TryResolveTimeZone(request.TimeZoneId, out var timeZone))
        {
            return Error.Validation(
                new Dictionary<string, string[]>
                {
                    ["timeZoneId"] = ["Time zone must be a valid IANA, Windows, or Browser UTC±HH:mm identifier."]
                },
                code: "Operations.Dashboard.TimeZoneInvalid");
        }

        if (request.TopCount < 1)
        {
            return Error.Validation(
                new Dictionary<string, string[]>
                {
                    ["topCount"] = ["Top count must be greater than zero."]
                },
                code: "Operations.Dashboard.TopCountInvalid");
        }

        var scopeResult = await _scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        var filter = DashboardFilter.Create(
            request.FromUtc,
            request.ToUtc,
            request.StationIds,
            request.CustomerIds,
            request.ServiceIds);
        var performedWorkOrders = DashboardFlightQuery.PerformedWorkOrders(_db);
        var performedServiceLines = DashboardFlightQuery.PerformedServiceLines(_db);
        var scopedFlights = DashboardFlightQuery.ApplyScope(
            _db.Flights.AsNoTracking(),
            scopeResult.Value,
            performedWorkOrders);
        var flights = DashboardFlightQuery.ApplyFilters(
            scopedFlights,
            filter,
            performedWorkOrders,
            performedServiceLines);

        var statusCounts = await flights
            .GroupBy(flight => flight.Status)
            .Select(group => new DashboardStatusCount(group.Key, group.LongCount()))
            .ToListAsync(cancellationToken);
        var totalFlights = statusCounts.Sum(item => item.Count);
        var statuses = DashboardProjection.BuildStatuses(statusCounts, totalFlights);
        var timelineGranularity = DashboardTimelineProjection.SelectGranularity(
            filter.FromUtc,
            filter.ToUtc,
            timeZone);

        if (!request.IncludeAnalytics)
        {
            return new OperationsDashboardDto(
                _timeProvider.GetUtcNow(),
                filter.FromUtc,
                filter.ToUtc,
                totalFlights,
                FlightsWithPerformedServices: 0,
                statuses,
                Stations: [],
                Customers: [],
                Services: [],
                OperationTypes: [],
                ServiceCategories: [],
                Timeline: [],
                TimelineGranularity: timelineGranularity.ToString(),
                Hourly: [],
                Monthly: [],
                Yearly: [],
                StationOptions: [],
                CustomerOptions: [],
                ServiceOptions: []);
        }

        // Local calendar grouping is provider independent. Load only authorized STA scalars, never
        // flight graphs; omitted zones retain the existing SQL aggregate path for UTC callers.
        var useLocalCalendar = !string.IsNullOrWhiteSpace(request.TimeZoneId);
        var arrivalInstants = useLocalCalendar
            ? await flights.Select(flight => flight.Schedule.Sta).ToListAsync(cancellationToken)
            : null;

        if (filter.FromUtc is null && filter.ToUtc is null && totalFlights > 0)
        {
            var timelineBounds = arrivalInstants is not null
                ? new DashboardTimelineBounds(arrivalInstants.Min(), arrivalInstants.Max())
                : await flights
                    .GroupBy(_ => 1)
                    .Select(group => new DashboardTimelineBounds(
                        group.Min(flight => flight.Schedule.Sta),
                        group.Max(flight => flight.Schedule.Sta)))
                    .SingleAsync(cancellationToken);
            timelineGranularity = DashboardTimelineProjection.SelectMaxGranularity(
                timelineBounds.FirstFlightUtc,
                timelineBounds.LastFlightUtc,
                timeZone);
        }

        var stationGroups = await flights
            .GroupBy(flight => flight.Station.StationId)
            .Select(group => new DashboardGroupRow(
                group.Key,
                group.Max(flight => flight.Station.Name)!,
                group.Max(flight => flight.Station.IataCode),
                group.LongCount()))
            .ToListAsync(cancellationToken);

        var customerGroups = await flights
            .GroupBy(flight => flight.Customer.CustomerId)
            .Select(group => new DashboardGroupRow(
                group.Key,
                group.Max(flight => flight.Customer.Name)!,
                group.Max(flight => flight.Customer.IataCode),
                group.LongCount()))
            .ToListAsync(cancellationToken);

        var operationTypeGroups = await flights
            .GroupBy(flight => flight.OperationType.OperationTypeId)
            .Select(group => new DashboardGroupRow(
                group.Key,
                group.Max(flight => flight.OperationType.Name)!,
                Code: null,
                group.LongCount()))
            .ToListAsync(cancellationToken);

        var perLandingFlights = flights.Where(flight => flight.PlannedServices.Any(service =>
            service.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService));
        var perLandingFlightCount = await perLandingFlights.LongCountAsync(cancellationToken);
        var qualifyingOnCallWorkOrders = _db.WorkOrders.AsNoTracking().QualifyingForOnCall();
        var onCallFlightCount = await perLandingFlights.LongCountAsync(
            flight => qualifyingOnCallWorkOrders.Any(workOrder => workOrder.FlightId == flight.Id),
            cancellationToken);

        var performedServicesForFlights =
            from workOrder in performedWorkOrders
            join line in performedServiceLines
                on workOrder.Id equals line.WorkOrderId
            where flights.Any(flight => flight.Id == workOrder.FlightId)
            select new
            {
                workOrder.FlightId,
                ServiceId = line.Service.ServiceId,
                ServiceName = line.Service.Name
            };
        var serviceGroups = await performedServicesForFlights
            .GroupBy(service => service.ServiceId)
            .Select(group => new DashboardGroupRow(
                group.Key,
                group.Max(service => service.ServiceName)!,
                Code: null,
                group.Select(service => service.FlightId).Distinct().LongCount()))
            .ToListAsync(cancellationToken);
        var totalServiceFlightPairs = serviceGroups.Sum(group => group.Count);
        var flightsWithPerformedServices = await performedServicesForFlights
            .Select(service => service.FlightId)
            .Distinct()
            .LongCountAsync(cancellationToken);

        IReadOnlyList<DashboardTrendCount> hourlyCounts;
        IReadOnlyList<DashboardTrendCount> monthlyCounts;
        IReadOnlyList<DashboardTrendCount> yearlyCounts;
        IReadOnlyList<DashboardTimelinePointDto> timeline;
        if (arrivalInstants is not null)
        {
            var localArrivals = arrivalInstants.Select(instant => TimeZoneInfo.ConvertTime(instant, timeZone)).ToList();
            hourlyCounts = localArrivals.GroupBy(instant => instant.Hour)
                .Select(group => new DashboardTrendCount(group.Key, group.LongCount())).ToList();
            monthlyCounts = localArrivals.GroupBy(instant => instant.Month)
                .Select(group => new DashboardTrendCount(group.Key, group.LongCount())).ToList();
            yearlyCounts = localArrivals.GroupBy(instant => instant.Year)
                .Select(group => new DashboardTrendCount(group.Key, group.LongCount())).ToList();
            timeline = DashboardTimelineProjection.BuildLocal(
                arrivalInstants, timelineGranularity, filter.FromUtc, filter.ToUtc, timeZone);
        }
        else
        {
            hourlyCounts = await flights
                .GroupBy(flight => flight.Schedule.Sta.Hour)
                .Select(group => new DashboardTrendCount(group.Key, group.LongCount()))
                .ToListAsync(cancellationToken);
            monthlyCounts = await flights
                .GroupBy(flight => flight.Schedule.Sta.Month)
                .Select(group => new DashboardTrendCount(group.Key, group.LongCount()))
                .ToListAsync(cancellationToken);
            yearlyCounts = await flights
                .GroupBy(flight => flight.Schedule.Sta.Year)
                .Select(group => new DashboardTrendCount(group.Key, group.LongCount()))
                .ToListAsync(cancellationToken);
            var timelineCounts = await DashboardTimelineProjection.LoadCountsAsync(
                flights,
                timelineGranularity,
                cancellationToken);
            timeline = DashboardTimelineProjection.Build(
                timelineCounts, timelineGranularity, filter.FromUtc, filter.ToUtc);
        }

        IReadOnlyList<DashboardFilterOptionDto> stationOptions = [];
        IReadOnlyList<DashboardFilterOptionDto> customerOptions = [];
        IReadOnlyList<DashboardFilterOptionDto> serviceOptions = [];
        if (request.IncludeOptions)
        {
            // Options come from the caller's complete visible scope, not from MasterData endpoints
            // and not from selected filters, so changing one filter never removes valid peers.
            stationOptions = DashboardProjection.SortOptions(await scopedFlights
                .GroupBy(flight => flight.Station.StationId)
                .Select(group => new DashboardFilterOptionDto(
                    group.Key,
                    group.Max(flight => flight.Station.Name)!,
                    group.Max(flight => flight.Station.IataCode)))
                .ToListAsync(cancellationToken));
            customerOptions = DashboardProjection.SortOptions(await scopedFlights
                .GroupBy(flight => flight.Customer.CustomerId)
                .Select(group => new DashboardFilterOptionDto(
                    group.Key,
                    group.Max(flight => flight.Customer.Name)!,
                    group.Max(flight => flight.Customer.IataCode)))
                .ToListAsync(cancellationToken));
            serviceOptions = DashboardProjection.SortOptions(await (
                    from workOrder in performedWorkOrders
                    join line in performedServiceLines
                        on workOrder.Id equals line.WorkOrderId
                    where scopedFlights.Any(flight => flight.Id == workOrder.FlightId)
                    group line by line.Service.ServiceId
                    into serviceGroup
                    select new DashboardFilterOptionDto(
                        serviceGroup.Key,
                        serviceGroup.Max(line => line.Service.Name)!,
                        null))
                .ToListAsync(cancellationToken));
        }

        return new OperationsDashboardDto(
            _timeProvider.GetUtcNow(),
            filter.FromUtc,
            filter.ToUtc,
            totalFlights,
            flightsWithPerformedServices,
            statuses,
            DashboardProjection.BuildCompleteBreakdown(stationGroups, totalFlights),
            DashboardProjection.BuildBreakdown(customerGroups, request.TopCount, totalFlights),
            DashboardProjection.BuildBreakdown(serviceGroups, request.TopCount, totalServiceFlightPairs),
            DashboardProjection.BuildCompleteBreakdown(
                operationTypeGroups,
                totalFlights,
                WellKnownMasterDataIds.AdHocOperationType),
            DashboardProjection.BuildServiceCategories(perLandingFlightCount, onCallFlightCount),
            timeline,
            timelineGranularity.ToString(),
            DashboardProjection.BuildHourly(hourlyCounts),
            DashboardProjection.BuildMonthly(monthlyCounts),
            DashboardProjection.BuildYearly(
                yearlyCounts,
                filter.FromUtc is { } from ? TimeZoneInfo.ConvertTime(from, timeZone) : null,
                filter.ToUtc is { } to ? TimeZoneInfo.ConvertTime(to, timeZone) : null),
            stationOptions,
            customerOptions,
            serviceOptions);
    }
}

public sealed class GetDashboardFlightsQueryHandler(IOperationsDbContext db, IOperationsScope scope)
    : IQueryHandler<GetDashboardFlightsQuery, PagedResult<DashboardFlightRowDto>>
{
    public async Task<Result<PagedResult<DashboardFlightRowDto>>> Handle(
        GetDashboardFlightsQuery request,
        CancellationToken cancellationToken)
    {
        if (DashboardQueryValidation.ValidateDates(request.FromUtc, request.ToUtc) is { } dateError)
            return dateError;

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        var paging = PageRequest.From(request.Page, request.PageSize);
        var performedWorkOrders = DashboardFlightQuery.PerformedWorkOrders(db);
        var performedServiceLines = DashboardFlightQuery.PerformedServiceLines(db);
        var flights = DashboardFlightQuery.ApplyFilters(
            DashboardFlightQuery.ApplyScope(db.Flights.AsNoTracking(), scopeResult.Value, performedWorkOrders),
            DashboardFilter.Create(
                request.FromUtc,
                request.ToUtc,
                request.StationIds,
                request.CustomerIds,
                request.ServiceIds),
            performedWorkOrders,
            performedServiceLines);

        var total = await flights.LongCountAsync(cancellationToken);
        if (paging.IsOutOfRange(total))
            return paging.Empty<DashboardFlightRowDto>(total);

        var rows = await DashboardFlightQuery.LoadRowsAsync(
            flights,
            performedWorkOrders,
            performedServiceLines,
            request.Sort,
            paging.Skip,
            paging.PageSize,
            cancellationToken);
        return paging.ToResult(rows, total);
    }
}

public sealed class GetDashboardFlightsExportQueryHandler(IOperationsDbContext db, IOperationsScope scope)
    : IQueryHandler<GetDashboardFlightsExportQuery, IReadOnlyList<FlightExportRowDto>>
{
    public async Task<Result<IReadOnlyList<FlightExportRowDto>>> Handle(
        GetDashboardFlightsExportQuery request,
        CancellationToken cancellationToken)
    {
        if (DashboardQueryValidation.ValidateDates(request.FromUtc, request.ToUtc) is { } dateError)
            return dateError;

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        var performedWorkOrders = DashboardFlightQuery.PerformedWorkOrders(db);
        var performedServiceLines = DashboardFlightQuery.PerformedServiceLines(db);
        var flights = DashboardFlightQuery.ApplyFilters(
            DashboardFlightQuery.ApplyScope(db.Flights.AsNoTracking(), scopeResult.Value, performedWorkOrders),
            DashboardFilter.Create(
                request.FromUtc,
                request.ToUtc,
                request.StationIds,
                request.CustomerIds,
                request.ServiceIds),
            performedWorkOrders,
            performedServiceLines);

        var rows = await FlightExportProjection.LoadAsync(
            db,
            DashboardFlightQuery.ApplySort(flights, request.Sort),
            cancellationToken);
        return Result.Success(rows);
    }
}

internal sealed record DashboardFilter(
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    IReadOnlyList<Guid> StationIds,
    IReadOnlyList<Guid> CustomerIds,
    IReadOnlyList<Guid> ServiceIds)
{
    public static DashboardFilter Create(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        IReadOnlyList<Guid>? stationIds,
        IReadOnlyList<Guid>? customerIds,
        IReadOnlyList<Guid>? serviceIds) =>
        new(
            fromUtc?.ToUniversalTime(),
            toUtc?.ToUniversalTime(),
            Normalize(stationIds),
            Normalize(customerIds),
            Normalize(serviceIds));

    private static IReadOnlyList<Guid> Normalize(IReadOnlyList<Guid>? values) =>
        values is { Count: > 0 }
            ? values.Where(value => value != Guid.Empty).Distinct().ToArray()
            : [];
}

internal static class DashboardQueryValidation
{
    public static bool TryResolveTimeZone(string? timeZoneId, out TimeZoneInfo timeZone)
    {
        var candidate = timeZoneId?.Trim();
        if (candidate?.Length > 128)
        {
            timeZone = TimeZoneInfo.Utc;
            return false;
        }

        const string browserPrefix = "Browser UTC";
        if (candidate?.StartsWith(browserPrefix, StringComparison.OrdinalIgnoreCase) == true)
        {
            var suffix = candidate.AsSpan(browserPrefix.Length);
            if (suffix.Length != 6 || suffix[0] is not ('+' or '-')
                || !TimeSpan.TryParseExact(suffix[1..], "hh\\:mm", CultureInfo.InvariantCulture, out var offset)
                || offset > TimeSpan.FromHours(14))
            {
                timeZone = TimeZoneInfo.Utc;
                return false;
            }
            candidate = browserPrefix + suffix.ToString();
        }

        if (ScheduleFlightsCommandHandler.TryResolveTimeZone(candidate, out timeZone))
            return true;
        return candidate is not null
            && TimeZoneInfo.TryConvertWindowsIdToIanaId(candidate, out var ianaId)
            && ScheduleFlightsCommandHandler.TryResolveTimeZone(ianaId, out timeZone);
    }

    public static Error? ValidateDates(DateTimeOffset? fromUtc, DateTimeOffset? toUtc)
    {
        if (fromUtc is null || toUtc is null || fromUtc < toUtc)
            return null;

        return Error.Validation(
            new Dictionary<string, string[]>
            {
                ["toUtc"] = ["To UTC must be later than From UTC; the dashboard range is [fromUtc, toUtc)."]
            },
            code: "Operations.Dashboard.DateRangeInvalid");
    }
}

internal static class DashboardFlightQuery
{
    public static IQueryable<WorkOrder> PerformedWorkOrders(IOperationsDbContext db) =>
        db.WorkOrders.AsNoTracking().Where(workOrder => workOrder.Status != WorkOrderStatus.Merged);

    public static IQueryable<WorkOrderServiceLine> PerformedServiceLines(IOperationsDbContext db) =>
        db.WorkOrderServiceLines.AsNoTracking();

    public static IQueryable<Flight> ApplyScope(
        IQueryable<Flight> flights,
        OperationsScopeContext scope,
        IQueryable<WorkOrder> performedWorkOrders) =>
        FlightListQuery.ApplyScopeAndFilters(
            flights,
            scope,
            new FlightListFilter(
                Search: null,
                StationId: null,
                CustomerId: null,
                OperationTypeId: null,
                Statuses: null,
                FromUtc: null,
                ToUtc: null,
                ServiceCategories: null),
            performedWorkOrders);

    public static IQueryable<Flight> ApplyFilters(
        IQueryable<Flight> flights,
        DashboardFilter filter,
        IQueryable<WorkOrder> performedWorkOrders,
        IQueryable<WorkOrderServiceLine> performedServiceLines)
    {
        if (filter.FromUtc is { } fromUtc)
            flights = flights.Where(flight => flight.Schedule.Sta >= fromUtc);
        if (filter.ToUtc is { } toUtc)
            flights = flights.Where(flight => flight.Schedule.Sta < toUtc);
        if (filter.StationIds.Count > 0)
            flights = flights.Where(flight => filter.StationIds.Contains(flight.Station.StationId));
        if (filter.CustomerIds.Count > 0)
            flights = flights.Where(flight => filter.CustomerIds.Contains(flight.Customer.CustomerId));
        if (filter.ServiceIds.Count > 0)
        {
            flights = flights.Where(flight => performedWorkOrders.Any(workOrder =>
                workOrder.FlightId == flight.Id &&
                performedServiceLines.Any(line =>
                    line.WorkOrderId == workOrder.Id &&
                    filter.ServiceIds.Contains(line.Service.ServiceId))));
        }

        return flights;
    }

    public static async Task<IReadOnlyList<DashboardFlightRowDto>> LoadRowsAsync(
        IQueryable<Flight> flights,
        IQueryable<WorkOrder> performedWorkOrders,
        IQueryable<WorkOrderServiceLine> performedServiceLines,
        string? sort,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        IQueryable<Flight> selectedFlights = ApplySort(flights, sort);
        if (skip is { } skipValue)
            selectedFlights = selectedFlights.Skip(skipValue);
        if (take is { } takeValue)
            selectedFlights = selectedFlights.Take(takeValue);

        var baseRows = await selectedFlights
            .Select(flight => new DashboardFlightBaseRow(
                flight.Id,
                flight.FlightNumber.Value,
                flight.Customer.IataCode,
                flight.Customer.Name,
                flight.Station.StationId,
                flight.Station.IataCode,
                flight.Station.Name,
                flight.OperationType.Name,
                flight.Schedule.Sta,
                flight.Schedule.Std,
                flight.Status.ToString()))
            .ToListAsync(cancellationToken);

        var selectedFlightIds = selectedFlights.Select(flight => flight.Id);
        var serviceRows = await (
                from workOrder in performedWorkOrders
                join line in performedServiceLines
                    on workOrder.Id equals line.WorkOrderId
                where selectedFlightIds.Contains(workOrder.FlightId)
                select new DashboardFlightServiceRow(
                    workOrder.FlightId,
                    line.Service.ServiceId,
                    line.Service.Name))
            .ToListAsync(cancellationToken);
        var serviceNames = serviceRows
            .GroupBy(row => row.FlightId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .GroupBy(row => row.ServiceId)
                    .Select(service => service.Select(row => row.ServiceName)
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(name => name, StringComparer.Ordinal)
                        .First())
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(name => name, StringComparer.Ordinal)
                    .ToList());

        return baseRows.Select(row => new DashboardFlightRowDto(
            row.Id,
            row.FlightNumber,
            row.CustomerIataCode,
            row.CustomerName,
            row.StationId,
            row.StationIata,
            row.StationName,
            row.OperationTypeName,
            row.ScheduledArrivalUtc,
            row.ScheduledDepartureUtc,
            row.Status,
            serviceNames.GetValueOrDefault(row.Id, []))).ToList();
    }

    public static IOrderedQueryable<Flight> ApplySort(IQueryable<Flight> query, string? sort)
    {
        if (SortSpec.Parse(sort) is not { } spec)
            return DefaultSort(query);

        return spec.Field switch
        {
            "flightnumber" => spec.Descending
                ? query.OrderByDescending(flight => flight.FlightNumber.Value).ThenByDescending(flight => flight.Id)
                : query.OrderBy(flight => flight.FlightNumber.Value).ThenBy(flight => flight.Id),
            "customer" or "customername" => spec.Descending
                ? query.OrderByDescending(flight => flight.Customer.Name).ThenByDescending(flight => flight.Id)
                : query.OrderBy(flight => flight.Customer.Name).ThenBy(flight => flight.Id),
            "station" or "stationiata" => spec.Descending
                ? query.OrderByDescending(flight => flight.Station.IataCode).ThenByDescending(flight => flight.Id)
                : query.OrderBy(flight => flight.Station.IataCode).ThenBy(flight => flight.Id),
            "operation" or "operationtypename" => spec.Descending
                ? query.OrderByDescending(flight => flight.OperationType.Name).ThenByDescending(flight => flight.Id)
                : query.OrderBy(flight => flight.OperationType.Name).ThenBy(flight => flight.Id),
            "sta" or "scheduledarrivalutc" => spec.Descending
                ? query.OrderByDescending(flight => flight.Schedule.Sta).ThenByDescending(flight => flight.Id)
                : query.OrderBy(flight => flight.Schedule.Sta).ThenBy(flight => flight.Id),
            "std" or "scheduleddepartureutc" => spec.Descending
                ? query.OrderByDescending(flight => flight.Schedule.Std).ThenByDescending(flight => flight.Id)
                : query.OrderBy(flight => flight.Schedule.Std).ThenBy(flight => flight.Id),
            "status" => spec.Descending
                ? query.OrderByDescending(flight => flight.Status).ThenByDescending(flight => flight.Id)
                : query.OrderBy(flight => flight.Status).ThenBy(flight => flight.Id),
            _ => DefaultSort(query)
        };
    }

    private static IOrderedQueryable<Flight> DefaultSort(IQueryable<Flight> query) =>
        query.OrderByDescending(flight => flight.Schedule.Sta).ThenBy(flight => flight.Id);
}

internal static class DashboardProjection
{
    private static readonly FlightStatus[] OperationalStatuses =
    [
        FlightStatus.Scheduled,
        FlightStatus.InProgress,
        FlightStatus.Completed,
        FlightStatus.Canceled
    ];

    public static IReadOnlyList<DashboardStatusItemDto> BuildStatuses(
        IReadOnlyList<DashboardStatusCount> counts,
        long totalFlights)
    {
        var lookup = counts.ToDictionary(item => item.Status, item => item.Count);
        return OperationalStatuses
            .Select(status =>
            {
                var count = lookup.GetValueOrDefault(status);
                return new DashboardStatusItemDto(status.ToString(), count, Percentage(count, totalFlights));
            })
            .ToList();
    }

    public static IReadOnlyList<DashboardFilterOptionDto> SortOptions(
        IReadOnlyList<DashboardFilterOptionDto> options) =>
        options
            .OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.Label, StringComparer.Ordinal)
            .ThenBy(option => option.Id)
            .ToList();

    public static IReadOnlyList<DashboardBreakdownItemDto> BuildBreakdown(
        IReadOnlyList<DashboardGroupRow> groups,
        int topCount,
        long denominator)
    {
        var ordered = groups
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Label, StringComparer.Ordinal)
            .ThenBy(group => group.Id)
            .ToList();
        var items = ordered
            .Take(topCount)
            .Select(group => new DashboardBreakdownItemDto(
                group.Id,
                group.Label,
                group.Code,
                group.Count,
                Percentage(group.Count, denominator),
                IsOther: false,
                GroupedItemCount: 1))
            .ToList();

        var remaining = ordered.Skip(topCount).ToList();
        if (remaining.Count > 0)
        {
            var otherCount = remaining.Sum(group => group.Count);
            items.Add(new DashboardBreakdownItemDto(
                Id: null,
                Label: "Other",
                Code: null,
                otherCount,
                Percentage(otherCount, denominator),
                IsOther: true,
                GroupedItemCount: remaining.Count));
        }

        return items;
    }

    public static IReadOnlyList<DashboardBreakdownItemDto> BuildCompleteBreakdown(
        IReadOnlyList<DashboardGroupRow> groups,
        long denominator,
        Guid? firstId = null) =>
        groups
            .OrderBy(group => firstId.HasValue && group.Id == firstId.Value ? 0 : 1)
            .ThenByDescending(group => group.Count)
            .ThenBy(group => group.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Label, StringComparer.Ordinal)
            .ThenBy(group => group.Id)
            .Select(group => new DashboardBreakdownItemDto(
                group.Id,
                group.Label,
                group.Code,
                group.Count,
                Percentage(group.Count, denominator),
                IsOther: false,
                GroupedItemCount: 1))
            .ToList();

    public static IReadOnlyList<DashboardBreakdownItemDto> BuildServiceCategories(
        long perLandingFlightCount,
        long onCallFlightCount)
    {
        var perLandingOnlyFlightCount = perLandingFlightCount - onCallFlightCount;
        return
        [
            new DashboardBreakdownItemDto(
                Id: null,
                Label: "Per Landing",
                Code: null,
                perLandingOnlyFlightCount,
                Percentage(perLandingOnlyFlightCount, perLandingFlightCount),
                IsOther: false,
                GroupedItemCount: 1),
            new DashboardBreakdownItemDto(
                Id: null,
                Label: "On Call",
                Code: null,
                onCallFlightCount,
                Percentage(onCallFlightCount, perLandingFlightCount),
                IsOther: false,
                GroupedItemCount: 1)
        ];
    }

    public static IReadOnlyList<DashboardTrendPointDto> BuildHourly(IReadOnlyList<DashboardTrendCount> counts)
    {
        var lookup = counts.ToDictionary(item => item.Key, item => item.Count);
        return Enumerable.Range(0, 24)
            .Select(hour => new DashboardTrendPointDto(
                hour.ToString("00", CultureInfo.InvariantCulture),
                $"{hour:00}:00",
                hour,
                lookup.GetValueOrDefault(hour)))
            .ToList();
    }

    public static IReadOnlyList<DashboardTrendPointDto> BuildMonthly(IReadOnlyList<DashboardTrendCount> counts)
    {
        var lookup = counts.ToDictionary(item => item.Key, item => item.Count);
        return Enumerable.Range(1, 12)
            .Select(month => new DashboardTrendPointDto(
                month.ToString("00", CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(month),
                month,
                lookup.GetValueOrDefault(month)))
            .ToList();
    }

    public static IReadOnlyList<DashboardTrendPointDto> BuildYearly(
        IReadOnlyList<DashboardTrendCount> counts,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc)
    {
        IEnumerable<int> years;
        if (fromUtc is { } from && toUtc is { } to)
        {
            var firstYear = from.Year;
            var lastYear = to.AddTicks(-1).Year;
            years = Enumerable.Range(firstYear, lastYear - firstYear + 1);
        }
        else
        {
            years = counts.Select(item => item.Key).Distinct().Order();
        }

        var lookup = counts.ToDictionary(item => item.Key, item => item.Count);
        return years.Select(year => new DashboardTrendPointDto(
            year.ToString(CultureInfo.InvariantCulture),
            year.ToString(CultureInfo.InvariantCulture),
            year,
            lookup.GetValueOrDefault(year))).ToList();
    }

    private static double Percentage(long count, long denominator) =>
        denominator <= 0
            ? 0
            : Math.Round(count * 100d / denominator, 2, MidpointRounding.AwayFromZero);
}

internal enum DashboardTimelineGranularity
{
    Hour,
    Day,
    Month
}

internal static class DashboardTimelineProjection
{
    public static DashboardTimelineGranularity SelectGranularity(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        TimeZoneInfo? timeZone = null)
    {
        if (fromUtc is null || toUtc is null)
            return DashboardTimelineGranularity.Month;

        var duration = timeZone is null
            ? toUtc.Value - fromUtc.Value
            : TimeZoneInfo.ConvertTime(toUtc.Value, timeZone).DateTime
                - TimeZoneInfo.ConvertTime(fromUtc.Value, timeZone).DateTime;
        if (duration <= TimeSpan.FromDays(2))
            return DashboardTimelineGranularity.Hour;
        if (duration <= TimeSpan.FromDays(120))
            return DashboardTimelineGranularity.Day;

        return DashboardTimelineGranularity.Month;
    }

    public static DashboardTimelineGranularity SelectMaxGranularity(
        DateTimeOffset firstFlightUtc,
        DateTimeOffset lastFlightUtc,
        TimeZoneInfo? timeZone = null)
    {
        var first = TimeZoneInfo.ConvertTime(firstFlightUtc, timeZone ?? TimeZoneInfo.Utc);
        var last = TimeZoneInfo.ConvertTime(lastFlightUtc, timeZone ?? TimeZoneInfo.Utc);
        if (first.Year == last.Year && first.DayOfYear == last.DayOfYear)
            return DashboardTimelineGranularity.Hour;
        if (first.Year == last.Year && first.Month == last.Month)
            return DashboardTimelineGranularity.Day;

        return DashboardTimelineGranularity.Month;
    }

    public static async Task<IReadOnlyList<DashboardTimelineCount>> LoadCountsAsync(
        IQueryable<Flight> flights,
        DashboardTimelineGranularity granularity,
        CancellationToken cancellationToken) =>
        granularity switch
        {
            DashboardTimelineGranularity.Hour => await flights
                .GroupBy(flight => new
                {
                    flight.Schedule.Sta.Year,
                    flight.Schedule.Sta.Month,
                    flight.Schedule.Sta.Day,
                    flight.Schedule.Sta.Hour
                })
                .Select(group => new DashboardTimelineCount(
                    group.Key.Year,
                    group.Key.Month,
                    group.Key.Day,
                    group.Key.Hour,
                    group.LongCount()))
                .ToListAsync(cancellationToken),
            DashboardTimelineGranularity.Day => await flights
                .GroupBy(flight => new
                {
                    flight.Schedule.Sta.Year,
                    flight.Schedule.Sta.Month,
                    flight.Schedule.Sta.Day
                })
                .Select(group => new DashboardTimelineCount(
                    group.Key.Year,
                    group.Key.Month,
                    group.Key.Day,
                    Hour: 0,
                    group.LongCount()))
                .ToListAsync(cancellationToken),
            _ => await flights
                .GroupBy(flight => new
                {
                    flight.Schedule.Sta.Year,
                    flight.Schedule.Sta.Month
                })
                .Select(group => new DashboardTimelineCount(
                    group.Key.Year,
                    group.Key.Month,
                    Day: 1,
                    Hour: 0,
                    group.LongCount()))
                .ToListAsync(cancellationToken)
        };

    public static IReadOnlyList<DashboardTimelinePointDto> Build(
        IReadOnlyList<DashboardTimelineCount> counts,
        DashboardTimelineGranularity granularity,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc)
    {
        var lookup = counts.ToDictionary(ToBucketUtc, count => count.Count);
        if (!TryResolveBounds(counts, granularity, fromUtc, toUtc, out var firstBucket, out var endExclusive))
            return [];

        var points = new List<DashboardTimelinePointDto>();
        for (var bucket = firstBucket; bucket < endExclusive; bucket = AddBucket(bucket, granularity))
        {
            points.Add(new DashboardTimelinePointDto(
                bucket,
                lookup.GetValueOrDefault(bucket)));
        }

        return points;
    }

    public static IReadOnlyList<DashboardTimelinePointDto> BuildLocal(
        IReadOnlyList<DateTimeOffset> arrivalInstants,
        DashboardTimelineGranularity granularity,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        TimeZoneInfo timeZone)
    {
        var lookup = arrivalInstants.GroupBy(instant => FloorToLocalBucket(instant, granularity, timeZone))
            .ToDictionary(group => group.Key, group => group.LongCount());
        if (lookup.Count == 0 && (fromUtc is null || toUtc is null))
            return [];

        var firstBucket = fromUtc is { } from
            ? FloorToLocalBucket(from, granularity, timeZone)
            : lookup.Keys.Min();
        var endExclusive = toUtc ?? AddLocalBucket(lookup.Keys.Max(), granularity, timeZone);
        var points = new List<DashboardTimelinePointDto>();
        for (var bucket = firstBucket; bucket < endExclusive; bucket = AddLocalBucket(bucket, granularity, timeZone))
            points.Add(new DashboardTimelinePointDto(bucket, lookup.GetValueOrDefault(bucket)));
        return points;
    }

    private static DateTimeOffset FloorToLocalBucket(
        DateTimeOffset instant,
        DashboardTimelineGranularity granularity,
        TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(instant, timeZone);
        if (granularity == DashboardTimelineGranularity.Hour)
        {
            // Retain the instant's offset: repeated fall-back hours are distinct UTC buckets.
            var floor = new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, 0, 0, local.Offset)
                .ToUniversalTime();
            // A partial-hour DST transition may make that nominal hour start invalid for this
            // offset. Its bucket starts at the actual transition instead (e.g. Lord Howe).
            return timeZone.GetUtcOffset(floor) == local.Offset
                ? floor
                : FirstOffsetChange(floor, instant.ToUniversalTime(), timeZone) ?? floor;
        }

        var date = granularity == DashboardTimelineGranularity.Day
            ? local.Date
            : new DateTime(local.Year, local.Month, 1);
        return ResolveLocalPeriodStart(date, timeZone);
    }

    private static DateTimeOffset AddLocalBucket(
        DateTimeOffset bucketUtc,
        DashboardTimelineGranularity granularity,
        TimeZoneInfo timeZone)
    {
        if (granularity == DashboardTimelineGranularity.Hour)
        {
            var localHour = TimeZoneInfo.ConvertTime(bucketUtc, timeZone).DateTime;
            var nextLocalHour = new DateTime(localHour.Year, localHour.Month, localHour.Day, localHour.Hour, 0, 0)
                .AddHours(1);
            var next = ResolveLocalPeriodStart(nextLocalHour, timeZone, bucketUtc);
            while (next <= bucketUtc)
            {
                nextLocalHour = nextLocalHour.AddHours(1);
                next = ResolveLocalPeriodStart(nextLocalHour, timeZone, bucketUtc);
            }
            // Offset changes split a repeated hour or partial-hour transition into real, distinct
            // intervals; nonexistent spring hours are skipped by resolving the next valid boundary.
            return FirstOffsetChange(bucketUtc, next, timeZone) ?? next;
        }
        var local = TimeZoneInfo.ConvertTime(bucketUtc, timeZone).Date;
        return ResolveLocalPeriodStart(
            granularity == DashboardTimelineGranularity.Day ? local.AddDays(1) : local.AddMonths(1),
            timeZone);
    }

    private static DateTimeOffset? FirstOffsetChange(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        TimeZoneInfo timeZone)
    {
        var offset = timeZone.GetUtcOffset(fromUtc);
        for (var cursor = fromUtc; cursor < toUtc;)
        {
            var probe = toUtc - cursor <= TimeSpan.FromMinutes(15) ? toUtc : cursor.AddMinutes(15);
            if (timeZone.GetUtcOffset(probe) != offset)
            {
                var low = cursor.UtcTicks;
                var high = probe.UtcTicks;
                while (high - low > 1)
                {
                    var middle = low + (high - low) / 2;
                    if (timeZone.GetUtcOffset(new DateTimeOffset(middle, TimeSpan.Zero)) == offset)
                        low = middle;
                    else
                        high = middle;
                }
                return new DateTimeOffset(high, TimeSpan.Zero);
            }
            cursor = probe;
        }
        return null;
    }

    private static DateTimeOffset ResolveLocalPeriodStart(
        DateTime local,
        TimeZoneInfo timeZone,
        DateTimeOffset? afterUtc = null)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        // Some zones move their clocks at midnight. Start that calendar bucket at its first valid
        // instant, using the earlier occurrence when midnight is repeated.
        while (timeZone.IsInvalidTime(local))
            local = local.AddMinutes(1);
        var offsets = timeZone.IsAmbiguousTime(local)
            ? timeZone.GetAmbiguousTimeOffsets(local)
            : [timeZone.GetUtcOffset(local)];
        var candidates = offsets.Select(offset => new DateTimeOffset(local, offset).ToUniversalTime())
            .Order().ToArray();
        // When advancing hourly buckets, a repeated boundary's first occurrence may already have
        // passed. Choose its next occurrence before advancing to another wall-clock hour.
        return candidates.FirstOrDefault(candidate => afterUtc is null || candidate > afterUtc.Value, candidates[0]);
    }

    private static bool TryResolveBounds(
        IReadOnlyList<DashboardTimelineCount> counts,
        DashboardTimelineGranularity granularity,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        out DateTimeOffset firstBucket,
        out DateTimeOffset endExclusive)
    {
        if (fromUtc is { } from && toUtc is { } to)
        {
            firstBucket = FloorToBucket(from, granularity);
            endExclusive = to;
            return true;
        }

        if (counts.Count == 0)
        {
            firstBucket = default;
            endExclusive = default;
            return false;
        }

        var populatedBuckets = counts.Select(ToBucketUtc).ToList();
        firstBucket = fromUtc is { } lowerBound
            ? FloorToBucket(lowerBound, granularity)
            : populatedBuckets.Min();
        endExclusive = toUtc ?? AddBucket(populatedBuckets.Max(), granularity);
        return firstBucket < endExclusive;
    }

    private static DateTimeOffset ToBucketUtc(DashboardTimelineCount count) =>
        new(count.Year, count.Month, count.Day, count.Hour, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset FloorToBucket(
        DateTimeOffset value,
        DashboardTimelineGranularity granularity)
    {
        var utc = value.ToUniversalTime();
        return granularity switch
        {
            DashboardTimelineGranularity.Hour => new DateTimeOffset(
                utc.Year,
                utc.Month,
                utc.Day,
                utc.Hour,
                0,
                0,
                TimeSpan.Zero),
            DashboardTimelineGranularity.Day => new DateTimeOffset(
                utc.Year,
                utc.Month,
                utc.Day,
                0,
                0,
                0,
                TimeSpan.Zero),
            _ => new DateTimeOffset(
                utc.Year,
                utc.Month,
                1,
                0,
                0,
                0,
                TimeSpan.Zero)
        };
    }

    private static DateTimeOffset AddBucket(
        DateTimeOffset bucket,
        DashboardTimelineGranularity granularity) =>
        granularity switch
        {
            DashboardTimelineGranularity.Hour => bucket.AddHours(1),
            DashboardTimelineGranularity.Day => bucket.AddDays(1),
            _ => bucket.AddMonths(1)
        };
}

internal sealed record DashboardStatusCount(FlightStatus Status, long Count);
internal sealed record DashboardGroupRow(Guid Id, string Label, string? Code, long Count);
internal sealed record DashboardTrendCount(int Key, long Count);
internal sealed record DashboardTimelineBounds(DateTimeOffset FirstFlightUtc, DateTimeOffset LastFlightUtc);
internal sealed record DashboardTimelineCount(int Year, int Month, int Day, int Hour, long Count);
internal sealed record DashboardFlightServiceRow(Guid FlightId, Guid ServiceId, string ServiceName);
internal sealed record DashboardFlightBaseRow(
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
    string Status);

// --- Duplicate candidates lookup (called by the ad-hoc UI before creating) ---

public sealed record FindDuplicateCandidatesQuery(
    Guid CustomerId,
    Guid? StationId,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    Guid? ExcludeFlightId = null) : IQuery<IReadOnlyList<DuplicateCandidateDto>>;

public sealed class FindDuplicateCandidatesQueryHandler(IOperationsScope scope, FlightDuplicateDetector detector)
    : IQueryHandler<FindDuplicateCandidatesQuery, IReadOnlyList<DuplicateCandidateDto>>
{
    public async Task<Result<IReadOnlyList<DuplicateCandidateDto>>> Handle(FindDuplicateCandidatesQuery request, CancellationToken cancellationToken)
    {
        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        var context = scopeResult.Value;
        Guid stationId;
        if (context.HasGlobalReadAccess)
        {
            if (request.StationId is not { } requestedStationId || requestedStationId == Guid.Empty)
                return Error.Validation("Station is required to check for duplicates.", "Operations.Flight.DuplicateCheckStationRequired");

            stationId = requestedStationId;
        }
        else if (context.StationId is { } scopedStationId)
        {
            if (request.StationId is { } requestedStationId && requestedStationId != Guid.Empty && requestedStationId != scopedStationId)
                return Error.Forbidden("This duplicate check is outside your station scope.", "Operations.Scope.Forbidden");

            stationId = scopedStationId;
        }
        else
        {
            return Error.Forbidden("You do not have access to duplicate checks.", "Operations.Flight.DuplicateCheckNotAllowed");
        }

        var candidates = await detector.FindAsync(
            request.CustomerId,
            stationId,
            request.ScheduledArrivalUtc,
            request.ScheduledDepartureUtc,
            request.ExcludeFlightId,
            cancellationToken);
        return Result.Success(candidates);
    }
}
