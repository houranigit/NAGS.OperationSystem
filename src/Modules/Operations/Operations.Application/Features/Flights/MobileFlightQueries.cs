using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Readers;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Contracts;
using Operations.Domain.Authorization;
using Operations.Domain.Flights;

namespace Operations.Application.Features.Flights;

public enum MobileFlightCategory
{
    MyFlights,
    PerLanding,
    AdHoc
}

public sealed record GetMobileFlightsQuery(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    string? Sort = null,
    string? Category = null) : IQuery<MobileFlightPageDto>;

public sealed class GetMobileFlightsQueryHandler(
    IOperationsDbContext db,
    IMobileFlightScope scope,
    ICustomerLogoReader logos,
    IMasterDataReader masterData) : IQueryHandler<GetMobileFlightsQuery, MobileFlightPageDto>
{
    public async Task<Result<MobileFlightPageDto>> Handle(GetMobileFlightsQuery request, CancellationToken cancellationToken)
    {
        var resolved = await scope.ResolveAsync(cancellationToken);
        if (resolved.IsFailure)
            return resolved.Error;

        MobileFlightCategory? category = null;
        if (request.Category is not null)
        {
            if (!Enum.TryParse<MobileFlightCategory>(request.Category, true, out var parsed) ||
                !Enum.IsDefined(parsed) || int.TryParse(request.Category, out _))
                return Error.Validation("Choose MyFlights, PerLanding, or AdHoc.", "Operations.Mobile.InvalidCategory");
            category = parsed;
        }

        var context = resolved.Value;
        var paging = PageRequest.From(request.Page, request.PageSize);
        var query = FlightListQuery.ApplySearch(
            MobileFlightQuery.ApplyScope(db.Flights.AsNoTracking(), context, category), request.Search);
        var total = await query.LongCountAsync(cancellationToken);
        List<MobileFlightListItemDto> items = [];
        if (!paging.IsOutOfRange(total))
        {
            var station = await masterData.GetStationAsync(context.StationId, cancellationToken);
            if (station is null || string.IsNullOrWhiteSpace(station.TimeZoneId))
                return Error.Failure("Flight data could not be loaded. Please try again.", "Operations.Flight.StationDataUnavailable");
            var stationTimeZoneId = station.TimeZoneId;
            items = await FlightListQuery.ApplySort(query, request.Sort)
                .Skip(paging.Skip).Take(paging.PageSize)
                .Select(f => new MobileFlightListItemDto(
                    f.Id, f.FlightNumber.Value, f.Customer.IataCode, f.Customer.Name, f.Station.IataCode,
                    f.OperationType.Name, f.Schedule.Sta, f.Schedule.Std,
                    f.PlannedServices.Any(p => p.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService),
                    f.AircraftType == null ? null : f.AircraftType.Model, f.Customer.CustomerId, null,
                    f.Station.StationId,
                    f.OperationType.OperationTypeId != WellKnownMasterDataIds.AdHocOperationType &&
                    !f.PlannedServices.Any(p => p.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService) &&
                    f.AssignedEmployees.Any(e => e.Employee.StaffMemberId == context.StaffMemberId),
                    f.OperationType.OperationTypeId == WellKnownMasterDataIds.AdHocOperationType &&
                    f.CreatedByUserId == context.UserId, stationTimeZoneId))
                .ToListAsync(cancellationToken);

            var customerIds = items.Select(item => item.CustomerId).Distinct().ToArray();
            var versions = await logos.GetLogoVersionsAsync(customerIds, cancellationToken);
            items = items.Select(item => item with
            {
                CustomerLogoVersion = versions.TryGetValue(item.CustomerId, out var version)
                    ? version : CustomerLogoVersions.None
            }).ToList();
        }

        return new MobileFlightPageDto(items, paging.Page, paging.PageSize, total,
            context.StationId, context.AsOfUtc, context.WindowStartUtc, context.WindowEndUtc);
    }
}

public sealed record GetMobileFlightByIdQuery(Guid Id) : IQuery<FlightDetailDto>;

public sealed class GetMobileFlightByIdQueryHandler(IOperationsDbContext db, IMobileFlightScope scope, IMasterDataReader masterData, IUserContext? user = null)
    : IQueryHandler<GetMobileFlightByIdQuery, FlightDetailDto>
{
    public async Task<Result<FlightDetailDto>> Handle(GetMobileFlightByIdQuery request, CancellationToken cancellationToken)
    {
        var resolved = await scope.ResolveAsync(cancellationToken);
        if (resolved.IsFailure)
            return resolved.Error;

        var flight = await MobileFlightQuery.ApplyScope(db.Flights.AsNoTracking(), resolved.Value)
            .AsSplitQuery().Include(f => f.PlannedServices).Include(f => f.AssignedEmployees)
            .Include(f => f.ArrivalSubmissions).Include(f => f.DepartureSubmissions).Include(f => f.CancellationSubmissions)
            .FirstOrDefaultAsync(f => f.Id == request.Id, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");
        var station = await masterData.GetStationAsync(flight.Station.StationId, cancellationToken);
        if (station is null || string.IsNullOrWhiteSpace(station.TimeZoneId))
            return Error.Failure("Flight data could not be loaded. Please try again.", "Operations.Flight.StationDataUnavailable");
        var staffId = resolved.Value.StaffMemberId;
        var canOperate = user?.HasPermission(OperationsPermissions.Flights.Operate) == true;
        var ownCancellation = flight.CancellationSubmissions.Any(c => c.SubmittedByEmployee.StaffMemberId == staffId);
        var ownWork = flight.ArrivalSubmissions.Any(a => a.SubmittedByEmployee.StaffMemberId == staffId) ||
            flight.DepartureSubmissions.Any(d => d.SubmittedByEmployee.StaffMemberId == staffId);
        return FlightDetails.Map(flight, station.TimeZoneId, staffId,
            canOperate && !ownCancellation, canOperate && !ownCancellation, canOperate && !ownWork);
    }
}

public sealed record GetMobileFlightCustomerLogoQuery(Guid CustomerId, string? ExpectedVersion = null)
    : IQuery<CustomerLogoReadContent>;

public sealed class GetMobileFlightCustomerLogoQueryHandler(
    IOperationsDbContext db,
    IMobileFlightScope scope,
    ICustomerLogoReader logos) : IQueryHandler<GetMobileFlightCustomerLogoQuery, CustomerLogoReadContent>
{
    public async Task<Result<CustomerLogoReadContent>> Handle(
        GetMobileFlightCustomerLogoQuery request, CancellationToken cancellationToken)
    {
        var resolved = await scope.ResolveAsync(cancellationToken);
        if (resolved.IsFailure)
            return resolved.Error;

        if (!await MobileFlightQuery.ApplyScope(db.Flights.AsNoTracking(), resolved.Value)
                .AnyAsync(f => f.Customer.CustomerId == request.CustomerId, cancellationToken))
            return LogoNotFound();

        var logo = await logos.GetLogoAsync(request.CustomerId, cancellationToken);
        if (logo is null)
            return LogoNotFound();
        if (request.ExpectedVersion is { } expectedVersion && expectedVersion != logo.Version)
            return Error.PreconditionFailed("The customer logo changed. Refresh its metadata and retry.",
                "Operations.Flight.CustomerLogoChanged");
        return logo;
    }

    private static Error LogoNotFound() =>
        Error.NotFound("Customer logo not found.", "Operations.Flight.CustomerLogoNotFound");
}

internal static class MobileFlightQuery
{
    public static IQueryable<Flight> ApplyScope(
        IQueryable<Flight> query,
        MobileFlightScopeContext scope,
        MobileFlightCategory? category = null)
    {
        var stationId = scope.StationId;
        var staffId = scope.StaffMemberId;
        var userId = scope.UserId;
        var fromUtc = scope.WindowStartUtc;
        var toUtc = scope.WindowEndUtc;
        query = query.Where(f => f.Station.StationId == stationId &&
            f.Schedule.Sta <= toUtc && f.Schedule.Std >= fromUtc);

        return category switch
        {
            MobileFlightCategory.MyFlights => query.Where(f =>
                f.OperationType.OperationTypeId != WellKnownMasterDataIds.AdHocOperationType &&
                !f.PlannedServices.Any(p => p.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService) &&
                f.AssignedEmployees.Any(e => e.Employee.StaffMemberId == staffId)),
            MobileFlightCategory.PerLanding => query.Where(f =>
                f.OperationType.OperationTypeId != WellKnownMasterDataIds.AdHocOperationType &&
                f.PlannedServices.Any(p => p.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService)),
            MobileFlightCategory.AdHoc => query.Where(f =>
                f.OperationType.OperationTypeId == WellKnownMasterDataIds.AdHocOperationType && f.CreatedByUserId == userId),
            _ => query.Where(f =>
                (f.OperationType.OperationTypeId == WellKnownMasterDataIds.AdHocOperationType && f.CreatedByUserId == userId) ||
                (f.OperationType.OperationTypeId != WellKnownMasterDataIds.AdHocOperationType &&
                 (f.PlannedServices.Any(p => p.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService) ||
                  f.AssignedEmployees.Any(e => e.Employee.StaffMemberId == staffId))))
        };
    }
}
