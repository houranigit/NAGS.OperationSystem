using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Seeding;
using MasterData.Contracts.Readers;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Contracts;
using Operations.Domain.Flights;

namespace Operations.Application.Features.Flights;

public sealed record GetFlightsQuery(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    Guid? StationId = null,
    Guid? CustomerId = null,
    Guid? OperationTypeId = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string? Sort = null) : IQuery<PagedResult<FlightListItemDto>>;

public sealed class GetFlightsQueryHandler(IOperationsDbContext db, IOperationsScope scope, ICustomerLogoReader logos, IMasterDataReader masterData)
    : IQueryHandler<GetFlightsQuery, PagedResult<FlightListItemDto>>
{
    public async Task<Result<PagedResult<FlightListItemDto>>> Handle(GetFlightsQuery request, CancellationToken cancellationToken)
    {
        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        var paging = PageRequest.From(request.Page, request.PageSize);
        var query = FlightListQuery.ApplyScopeAndFilters(db.Flights.AsNoTracking(), scopeResult.Value,
            new FlightListFilter(request.Search, request.StationId, request.CustomerId,
                request.OperationTypeId, request.FromUtc, request.ToUtc));
        var total = await query.LongCountAsync(cancellationToken);
        if (paging.IsOutOfRange(total))
            return paging.Empty<FlightListItemDto>(total);

        var items = await FlightListQuery.ApplySort(query, request.Sort)
            .Skip(paging.Skip).Take(paging.PageSize)
            .Select(f => new
            {
                f.Id,
                FlightNumber = f.FlightNumber.Value,
                CustomerIataCode = f.Customer.IataCode,
                CustomerName = f.Customer.Name,
                f.Customer.CustomerId,
                StationIataCode = f.Station.IataCode,
                f.Station.StationId,
                OperationTypeName = f.OperationType.Name,
                ScheduledArrivalUtc = f.Schedule.Sta,
                ScheduledDepartureUtc = f.Schedule.Std,
                IsPerLanding = f.PlannedServices.Any(service => service.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService),
                AircraftTypeModel = f.AircraftType == null ? null : f.AircraftType.Model
            })
            .ToListAsync(cancellationToken);
        var customerIds = items.Select(item => item.CustomerId).Distinct().ToArray();
        var versions = await logos.GetLogoVersionsAsync(customerIds, cancellationToken);
        var stations = await masterData.GetStationsAsync(items.Select(item => item.StationId).Distinct().ToArray(), cancellationToken);
        var stationTimeZones = stations.ToDictionary(station => station.Id, station => station.TimeZoneId);
        if (items.Any(item => !stationTimeZones.TryGetValue(item.StationId, out var zone) || string.IsNullOrWhiteSpace(zone)))
            return Error.Failure("Flight data could not be loaded. Please try again.", "Operations.Flight.StationDataUnavailable");

        var versionedItems = items.Select(item => new FlightListItemDto(
            item.Id, item.FlightNumber, item.CustomerIataCode, item.CustomerName, item.StationIataCode,
            item.OperationTypeName, item.ScheduledArrivalUtc, item.ScheduledDepartureUtc, item.IsPerLanding,
            stationTimeZones[item.StationId], item.AircraftTypeModel, item.CustomerId,
            versions.TryGetValue(item.CustomerId, out var version) ? version : CustomerLogoVersions.None,
            item.StationId)).ToList();
        return paging.ToResult(versionedItems, total);
    }
}

public sealed record GetFlightByIdQuery(Guid Id) : IQuery<FlightDetailDto>;

public sealed class GetFlightByIdQueryHandler(IOperationsDbContext db, IOperationsScope scope, IMasterDataReader masterData)
    : IQueryHandler<GetFlightByIdQuery, FlightDetailDto>
{
    public async Task<Result<FlightDetailDto>> Handle(GetFlightByIdQuery request, CancellationToken cancellationToken)
    {
        var flight = await db.Flights.AsNoTracking().AsSplitQuery()
            .Include(f => f.PlannedServices)
            .Include(f => f.AssignedEmployees)
            .Include(f => f.ArrivalSubmissions)
            .Include(f => f.DepartureSubmissions)
            .Include(f => f.CancellationSubmissions)
            .FirstOrDefaultAsync(f => f.Id == request.Id, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var access = scopeResult.Value.EnsureFlightAccess(flight);
        if (access.IsFailure)
            return access.Error;

        var station = await masterData.GetStationAsync(flight.Station.StationId, cancellationToken);
        if (station is null || string.IsNullOrWhiteSpace(station.TimeZoneId))
            return Error.Failure("Flight data could not be loaded. Please try again.", "Operations.Flight.StationDataUnavailable");
        return FlightDetails.Map(flight, station.TimeZoneId);
    }
}

internal static class FlightDetails
{
    public static FlightDetailDto Map(Flight flight, string stationTimeZoneId, Guid? currentStaffMemberId = null, bool canSaveArrival = false, bool canSaveDeparture = false) =>
        new(flight.Id, flight.FlightNumber.Value,
            flight.Customer.CustomerId, flight.Customer.IataCode, flight.Customer.Name,
            flight.Station.StationId, flight.Station.IataCode, flight.Station.Name,
            flight.OperationType.OperationTypeId, flight.OperationType.Name,
            flight.AircraftType?.AircraftTypeId, flight.AircraftType?.Model,
            flight.Schedule.Sta, flight.Schedule.Std, flight.IsPerLanding,
            flight.PlannedServices.Select(service => new PlannedServiceDto(service.Service.ServiceId,
                service.Service.Name, service.IsAircraftPerLanding)).ToList(),
            flight.AssignedEmployees.Select(employee => new AssignedEmployeeDto(employee.Employee.StaffMemberId,
                employee.Employee.FullName, employee.Employee.EmployeeId)).ToList(),
            flight.CreatedAtUtc, flight.UpdatedAtUtc, Convert.ToBase64String(flight.RowVersion), stationTimeZoneId,
            flight.ArrivalSubmissions.OrderBy(a => a.CreatedAtUtc).ThenBy(a => a.Id).Select(FlightArrivalSubmissionDto.Map).ToList(),
            currentStaffMemberId, canSaveArrival, flight.CancellationSubmissions.Count > 0,
            flight.DepartureSubmissions.OrderBy(d => d.CreatedAtUtc).ThenBy(d => d.Id).Select(FlightDepartureSubmissionDto.Map).ToList(), canSaveDeparture);
}

internal sealed record FlightListFilter(
    string? Search,
    Guid? StationId,
    Guid? CustomerId,
    Guid? OperationTypeId,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc);

internal static class FlightListQuery
{
    public static IQueryable<Flight> ApplyScopeAndFilters(
        IQueryable<Flight> query,
        OperationsScopeContext scope,
        FlightListFilter filter)
    {
        // Station staff see their station's Per-Landing flights (station-wide) plus the flights they
        // are assigned to; holders of view-station (station dispatchers) see every flight at their
        // station; global readers see everything (optionally filtered).
        if (!scope.HasGlobalReadAccess && scope.StationId is { } stationId)
        {
            query = query.Where(f => f.Station.StationId == stationId);

            if (!scope.CanViewStationWide)
            {
                var staffId = scope.StaffMemberId;
                query = query.Where(f =>
                    f.PlannedServices.Any(p => p.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService) ||
                    f.AssignedEmployees.Any(e => e.Employee.StaffMemberId == staffId));
            }
        }
        else if (filter.StationId is { } filterStation)
        {
            query = query.Where(f => f.Station.StationId == filterStation);
        }

        if (filter.CustomerId is { } customer)
            query = query.Where(f => f.Customer.CustomerId == customer);
        if (filter.OperationTypeId is { } operationType)
            query = query.Where(f => f.OperationType.OperationTypeId == operationType);
        if (filter.FromUtc is { } from)
            query = query.Where(f => f.Schedule.Sta >= from);
        if (filter.ToUtc is { } to)
            query = query.Where(f => f.Schedule.Sta <= to);
        return ApplySearch(query, filter.Search);
    }

    public static IQueryable<Flight> ApplySearch(IQueryable<Flight> query, string? search)
    {
        if (SearchFilter.Term(search) is { } term)
        {
            var compactFlightTerm = CompactFlightSearchTerm(term);
            var flightIdTerm = ParseFlightIdSearchTerm(term);
            var hasFlightIdTerm = flightIdTerm.HasValue;
            var flightId = flightIdTerm.GetValueOrDefault();
            query = query.Where(f =>
                (hasFlightIdTerm && f.Id == flightId) ||
                f.FlightNumber.Value.ToLower().Contains(term) ||
                f.Customer.Name.ToLower().Contains(term) ||
                (f.Customer.IataCode != null &&
                 ((f.Customer.IataCode.ToLower() + "-" + f.FlightNumber.Value.ToLower()).Contains(term) ||
                  (f.Customer.IataCode.ToLower() + f.FlightNumber.Value.ToLower()).Contains(compactFlightTerm))));
        }

        return query;
    }

    public static IOrderedQueryable<Flight> ApplySort(IQueryable<Flight> query, string? sort)
    {
        if (SortSpec.Parse(sort) is not { } spec)
            return DefaultSort(query);

        return spec.Field switch
        {
            "flightnumber" => spec.Descending
                ? query.OrderByDescending(f => f.FlightNumber.Value).ThenByDescending(f => f.Id)
                : query.OrderBy(f => f.FlightNumber.Value).ThenBy(f => f.Id),
            "customername" => spec.Descending
                ? query.OrderByDescending(f => f.Customer.Name).ThenByDescending(f => f.Id)
                : query.OrderBy(f => f.Customer.Name).ThenBy(f => f.Id),
            "stationiata" or "stationiatacode" => spec.Descending
                ? query.OrderByDescending(f => f.Station.IataCode).ThenByDescending(f => f.Id)
                : query.OrderBy(f => f.Station.IataCode).ThenBy(f => f.Id),
            "operationtypename" => spec.Descending
                ? query.OrderByDescending(f => f.OperationType.Name).ThenByDescending(f => f.Id)
                : query.OrderBy(f => f.OperationType.Name).ThenBy(f => f.Id),
            "scheduledarrivalutc" => spec.Descending
                ? query.OrderByDescending(f => f.Schedule.Sta).ThenByDescending(f => f.Id)
                : query.OrderBy(f => f.Schedule.Sta).ThenBy(f => f.Id),
            "scheduleddepartureutc" => spec.Descending
                ? query.OrderByDescending(f => f.Schedule.Std).ThenByDescending(f => f.Id)
                : query.OrderBy(f => f.Schedule.Std).ThenBy(f => f.Id),
            _ => DefaultSort(query)
        };
    }

    private static IOrderedQueryable<Flight> DefaultSort(IQueryable<Flight> query) =>
        query.OrderByDescending(f => f.Schedule.Sta).ThenBy(f => f.Id);

    private static string CompactFlightSearchTerm(string term) =>
        term.Replace("-", string.Empty).Replace(" ", string.Empty);

    private static Guid? ParseFlightIdSearchTerm(string term)
    {
        if (Guid.TryParse(term, out var flightId))
            return flightId;

        var compactTerm = CompactFlightSearchTerm(term);
        return compactTerm.Length == 32 && Guid.TryParseExact(compactTerm, "N", out flightId)
            ? flightId
            : null;
    }
}
