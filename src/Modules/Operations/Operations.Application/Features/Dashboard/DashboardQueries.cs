using System.Globalization;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
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
    bool IncludeOptions = true) : IQuery<OperationsDashboardDto>;

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
    string? Sort = null) : IQuery<IReadOnlyList<DashboardFlightRowDto>>;

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
                Hourly: [],
                Monthly: [],
                Yearly: [],
                StationOptions: [],
                CustomerOptions: [],
                ServiceOptions: []);
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

        var hourlyCounts = await flights
            .GroupBy(flight => flight.Schedule.Sta.Hour)
            .Select(group => new DashboardTrendCount(group.Key, group.LongCount()))
            .ToListAsync(cancellationToken);
        var monthlyCounts = await flights
            .GroupBy(flight => flight.Schedule.Sta.Month)
            .Select(group => new DashboardTrendCount(group.Key, group.LongCount()))
            .ToListAsync(cancellationToken);
        var yearlyCounts = await flights
            .GroupBy(flight => flight.Schedule.Sta.Year)
            .Select(group => new DashboardTrendCount(group.Key, group.LongCount()))
            .ToListAsync(cancellationToken);

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
            DashboardProjection.BuildBreakdown(stationGroups, request.TopCount, totalFlights),
            DashboardProjection.BuildBreakdown(customerGroups, request.TopCount, totalFlights),
            DashboardProjection.BuildBreakdown(serviceGroups, request.TopCount, totalServiceFlightPairs),
            DashboardProjection.BuildHourly(hourlyCounts),
            DashboardProjection.BuildMonthly(monthlyCounts),
            DashboardProjection.BuildYearly(yearlyCounts, filter.FromUtc, filter.ToUtc),
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
    : IQueryHandler<GetDashboardFlightsExportQuery, IReadOnlyList<DashboardFlightRowDto>>
{
    public async Task<Result<IReadOnlyList<DashboardFlightRowDto>>> Handle(
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

        var rows = await DashboardFlightQuery.LoadRowsAsync(
            flights,
            performedWorkOrders,
            performedServiceLines,
            request.Sort,
            skip: null,
            take: null,
            cancellationToken);
        return Result.Success<IReadOnlyList<DashboardFlightRowDto>>(rows);
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

    private static IOrderedQueryable<Flight> ApplySort(IQueryable<Flight> query, string? sort)
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

internal sealed record DashboardStatusCount(FlightStatus Status, long Count);
internal sealed record DashboardGroupRow(Guid Id, string Label, string? Code, long Count);
internal sealed record DashboardTrendCount(int Key, long Count);
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
        if (context.IsAdministrator)
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
