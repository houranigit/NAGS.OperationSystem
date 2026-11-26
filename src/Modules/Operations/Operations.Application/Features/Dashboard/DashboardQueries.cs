using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Readers;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Contracts;
using Operations.Application.Features.Flights;
using Operations.Domain.Flights;

namespace Operations.Application.Features.Dashboard;

public sealed record GetOperationsDashboardQuery(
    DashboardFlightFilter Filter,
    int TopCount = 5,
    bool IncludeOptions = true,
    TimeZoneInfo? TimeZone = null) : IQuery<OperationsDashboardDto>;

public sealed record GetDashboardFlightsQuery(
    DashboardFlightFilter Filter,
    int Page = 1,
    int PageSize = 20,
    string? Sort = null) : IQuery<PagedResult<DashboardFlightRowDto>>;

public sealed record GetDashboardFlightsExportQuery(
    DashboardFlightFilter Filter,
    string? Sort = null) : IQuery<IReadOnlyList<FlightExportRowDto>>;

public sealed record DashboardFlightFilter(
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    IReadOnlyList<Guid> StationIds,
    IReadOnlyList<Guid> CustomerIds,
    IReadOnlyList<Guid> ServiceIds)
{
    internal Error? Validate() =>
        FromUtc is { } from && ToUtc is { } to && from >= to
            ? Error.Validation("To UTC must be later than From UTC.", "Operations.Dashboard.DateRangeInvalid")
            : null;

    internal IQueryable<Flight> Apply(IQueryable<Flight> flights)
    {
        if (FromUtc is { } from)
            flights = flights.Where(flight => flight.Schedule.Sta >= from);
        if (ToUtc is { } to)
            flights = flights.Where(flight => flight.Schedule.Sta < to);
        if (StationIds.Count > 0)
            flights = flights.Where(flight => StationIds.Contains(flight.Station.StationId));
        if (CustomerIds.Count > 0)
            flights = flights.Where(flight => CustomerIds.Contains(flight.Customer.CustomerId));
        if (ServiceIds.Count > 0)
            flights = flights.Where(flight => flight.ServiceRecords.Any(service => ServiceIds.Contains(service.Service.ServiceId)));

        return flights;
    }
}

public sealed class GetOperationsDashboardQueryHandler(IOperationsDbContext db, IOperationsScope scope, TimeProvider clock)
    : IQueryHandler<GetOperationsDashboardQuery, OperationsDashboardDto>
{
    public async Task<Result<OperationsDashboardDto>> Handle(GetOperationsDashboardQuery request, CancellationToken ct)
    {
        var filter = request.Filter;
        if (filter.Validate() is { } error)
            return error;
        if (request.TopCount < 1)
            return Error.Validation("Top count must be greater than zero.", "Operations.Dashboard.TopCountInvalid");

        var resolved = await scope.ResolveAsync(ct);
        if (resolved.IsFailure)
            return resolved.Error;

        var scopedFlights = DashboardFlightQuery.ApplyScope(db, resolved.Value);
        var flights = filter.Apply(scopedFlights);
        var now = clock.GetUtcNow();
        // The lifecycle CASE expression uses subqueries. Project only the scalars needed by the
        // dashboard instead of grouping that expression in SQL Server or hydrating flight graphs.
        var summaries = await DashboardFlightQuery.Match(flights, now)
            .Select(item => new
            {
                ArrivalUtc = item.Flight.Schedule.Sta,
                item.Status,
                item.IsAdHocFlight,
                item.IsPerLanding,
                item.IsOnCall,
                HasPerformedServices = item.Flight.ServiceRecords.Any()
            })
            .ToListAsync(ct);
        var totalFlights = (long)summaries.Count;
        var statuses = DashboardProjection.BuildStatuses(
            summaries.GroupBy(item => item.Status)
                .Select(group => new DashboardStatusCount(group.Key, group.LongCount())).ToArray(), totalFlights);
        var perLanding = summaries.LongCount(item => item.IsPerLanding && !item.IsAdHocFlight);
        var onCall = summaries.LongCount(item => item.IsOnCall);

        var stationGroups = await flights.GroupBy(flight => flight.Station.StationId)
            .Select(group => new DashboardGroupRow(group.Key, group.Max(flight => flight.Station.Name)!,
                group.Max(flight => flight.Station.IataCode), group.LongCount()))
            .ToListAsync(ct);
        var customerGroups = await flights.GroupBy(flight => flight.Customer.CustomerId)
            .Select(group => new DashboardGroupRow(group.Key, group.Max(flight => flight.Customer.Name)!,
                group.Max(flight => flight.Customer.IataCode), group.LongCount()))
            .ToListAsync(ct);
        var operationTypeGroups = await flights.GroupBy(flight => flight.OperationType.OperationTypeId)
            .Select(group => new DashboardGroupRow(group.Key, group.Max(flight => flight.OperationType.Name)!, null, group.LongCount()))
            .ToListAsync(ct);

        // Keep one use per flight and service, including work recorded on a return to ramp.
        var serviceGroups = await DashboardFlightQuery.PerformedServices(flights)
            .GroupBy(item => item.Service.ServiceId)
            .Select(group => new DashboardGroupRow(group.Key, group.Max(item => item.Service.Name)!, null,
                group.Select(item => item.FlightId).Distinct().LongCount()))
            .ToListAsync(ct);
        var (stations, customers, services) = request.IncludeOptions
            ? await LoadOptionsAsync(scopedFlights, ct)
            : (Array.Empty<DashboardFilterOptionDto>(), Array.Empty<DashboardFilterOptionDto>(), Array.Empty<DashboardFilterOptionDto>());

        var timeZone = request.TimeZone ?? TimeZoneInfo.Utc;
        var arrivalInstants = summaries.Select(item => item.ArrivalUtc).ToArray();
        var localArrivals = arrivalInstants.Select(instant => TimeZoneInfo.ConvertTime(instant, timeZone)).ToArray();
        var granularity = filter.FromUtc is null && filter.ToUtc is null && summaries.Count > 0
            ? DashboardTimelineProjection.SelectMaxGranularity(arrivalInstants.Min(), arrivalInstants.Max(), timeZone)
            : DashboardTimelineProjection.SelectGranularity(filter.FromUtc, filter.ToUtc, timeZone);

        return new OperationsDashboardDto(
            now, filter.FromUtc, filter.ToUtc, totalFlights, summaries.LongCount(item => item.HasPerformedServices), statuses,
            DashboardProjection.BuildCompleteBreakdown(stationGroups, totalFlights),
            DashboardProjection.BuildBreakdown(customerGroups, request.TopCount, totalFlights),
            DashboardProjection.BuildBreakdown(serviceGroups, request.TopCount, serviceGroups.Sum(group => group.Count)),
            DashboardProjection.BuildCompleteBreakdown(operationTypeGroups, totalFlights, WellKnownMasterDataIds.AdHocOperationType),
            DashboardProjection.BuildServiceCategories(perLanding, onCall),
            DashboardTimelineProjection.BuildLocal(arrivalInstants, granularity, filter.FromUtc, filter.ToUtc, timeZone),
            granularity.ToString(),
            DashboardProjection.BuildHourly(CountTrends(localArrivals, instant => instant.Hour)),
            DashboardProjection.BuildMonthly(CountTrends(localArrivals, instant => instant.Month)),
            DashboardProjection.BuildYearly(CountTrends(localArrivals, instant => instant.Year),
                filter.FromUtc is { } from ? TimeZoneInfo.ConvertTime(from, timeZone) : null,
                filter.ToUtc is { } to ? TimeZoneInfo.ConvertTime(to, timeZone) : null),
            stations, customers, services);
    }

    private static DashboardTrendCount[] CountTrends(DateTimeOffset[] instants, Func<DateTimeOffset, int> key) =>
        instants.GroupBy(key).Select(group => new DashboardTrendCount(group.Key, group.LongCount())).ToArray();

    private static async Task<(DashboardFilterOptionDto[] Stations, DashboardFilterOptionDto[] Customers, DashboardFilterOptionDto[] Services)>
        LoadOptionsAsync(IQueryable<Flight> scopedFlights, CancellationToken ct)
    {
        // Options come from the complete visible scope, independently of current selections.
        var stations = await scopedFlights.GroupBy(flight => flight.Station.StationId)
            .Select(group => new DashboardFilterOptionDto(group.Key, group.Max(flight => flight.Station.Name)!,
                group.Max(flight => flight.Station.IataCode)))
            .ToListAsync(ct);
        var customers = await scopedFlights.GroupBy(flight => flight.Customer.CustomerId)
            .Select(group => new DashboardFilterOptionDto(group.Key, group.Max(flight => flight.Customer.Name)!,
                group.Max(flight => flight.Customer.IataCode)))
            .ToListAsync(ct);
        var services = await DashboardFlightQuery.PerformedServices(scopedFlights)
            .GroupBy(item => item.Service.ServiceId)
            .Select(group => new DashboardFilterOptionDto(group.Key, group.Max(item => item.Service.Name)!, null))
            .ToListAsync(ct);

        return (DashboardProjection.SortOptions(stations), DashboardProjection.SortOptions(customers),
            DashboardProjection.SortOptions(services));
    }
}

public sealed class GetDashboardFlightsQueryHandler(IOperationsDbContext db, IOperationsScope scope, TimeProvider clock)
    : IQueryHandler<GetDashboardFlightsQuery, PagedResult<DashboardFlightRowDto>>
{
    public async Task<Result<PagedResult<DashboardFlightRowDto>>> Handle(GetDashboardFlightsQuery request, CancellationToken ct)
    {
        if (request.Filter.Validate() is { } error)
            return error;
        var resolved = await scope.ResolveAsync(ct);
        if (resolved.IsFailure)
            return resolved.Error;

        var flights = request.Filter.Apply(DashboardFlightQuery.ApplyScope(db, resolved.Value));
        var paging = PageRequest.From(request.Page, request.PageSize);
        var count = await flights.LongCountAsync(ct);
        if (paging.IsOutOfRange(count))
            return paging.Empty<DashboardFlightRowDto>(count);

        var matches = DashboardFlightQuery.SortedMatches(flights, request.Sort, clock.GetUtcNow());
        var rows = await matches.Skip(paging.Skip).Take(paging.PageSize)
            .Select(item => new
            {
                item.Flight.Id,
                FlightNumber = item.Flight.FlightNumber.Value,
                CustomerIataCode = item.Flight.Customer.IataCode,
                CustomerName = item.Flight.Customer.Name,
                item.Flight.Station.StationId,
                StationIata = item.Flight.Station.IataCode,
                StationName = item.Flight.Station.Name,
                OperationTypeName = item.Flight.OperationType.Name,
                item.Flight.Schedule.Sta,
                item.Flight.Schedule.Std,
                item.Status,
                item.Flight.ApprovalNumber,
                item.Flight.ApprovalStatus
            })
            .ToListAsync(ct);
        var ids = rows.Select(row => row.Id).ToArray();
        var services = await DashboardFlightQuery.PerformedServices(flights.Where(flight => ids.Contains(flight.Id)))
            .Select(item => new { item.FlightId, ServiceName = item.Service.Name })
            .Distinct()
            .ToListAsync(ct);
        var names = services.GroupBy(item => item.FlightId).ToDictionary(group => group.Key,
            group => group.Select(item => item.ServiceName).Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase).ToArray());
        return paging.ToResult(rows.Select(row => new DashboardFlightRowDto(
            row.Id, row.FlightNumber, row.CustomerIataCode, row.CustomerName, row.StationId, row.StationIata, row.StationName,
            row.OperationTypeName, row.Sta, row.Std, row.Status.ToString(), names.GetValueOrDefault(row.Id, []),
            row.ApprovalNumber, row.ApprovalStatus.ToString())).ToArray(), count);
    }
}

public sealed class GetDashboardFlightsExportQueryHandler(
    IOperationsDbContext db, IOperationsScope scope, IMasterDataReader masterData, TimeProvider clock)
    : IQueryHandler<GetDashboardFlightsExportQuery, IReadOnlyList<FlightExportRowDto>>
{
    public async Task<Result<IReadOnlyList<FlightExportRowDto>>> Handle(GetDashboardFlightsExportQuery request, CancellationToken ct)
    {
        if (request.Filter.Validate() is { } error)
            return error;
        var resolved = await scope.ResolveAsync(ct);
        if (resolved.IsFailure)
            return resolved.Error;

        var flights = request.Filter.Apply(DashboardFlightQuery.ApplyScope(db, resolved.Value));
        var matches = DashboardFlightQuery.SortedMatches(flights, request.Sort, clock.GetUtcNow());
        return await new FlightExportRows(masterData).LoadAsync(flights, matches, includeManpower: false, ct);
    }
}

internal static class DashboardFlightQuery
{
    internal static IQueryable<Flight> ApplyScope(IOperationsDbContext db, OperationsScopeContext scope) =>
        FlightListQuery.ApplyPortalVisibility(db.Flights.AsNoTracking(), scope);

    internal static IQueryable<FlightListQuery.ReportMatch> Match(IQueryable<Flight> flights, DateTimeOffset now) =>
        FlightListQuery.MatchReportFilters(flights, FlightListType.All, null, now, null, null);

    internal static IQueryable<FlightListQuery.ReportMatch> SortedMatches(IQueryable<Flight> flights, string? sort, DateTimeOffset now)
    {
        var spec = SortSpec.Parse(sort);
        if (spec is not { Field: "status" })
            return Match(FlightListQuery.ApplySort(flights, sort), now);

        var matches = Match(flights, now);
        return spec.Value.Descending
            ? matches.OrderByDescending(item => item.Status).ThenByDescending(item => item.Flight.Id)
            : matches.OrderBy(item => item.Status).ThenBy(item => item.Flight.Id);
    }

    internal static IQueryable<FlightService> PerformedServices(IQueryable<Flight> flights) =>
        flights.SelectMany(flight => flight.ServiceRecords);
}
