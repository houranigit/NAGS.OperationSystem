using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Contracts;
using Operations.Domain.Enumerations;
using Operations.Domain.Flights;

namespace Operations.Application.Features.Flights;

// --- Paged flights list -----------------------------------------------------

public sealed record GetFlightsQuery(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    Guid? StationId = null,
    Guid? CustomerId = null,
    FlightStatus? Status = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string? Sort = null) : IQuery<PagedResult<FlightListItemDto>>;

public sealed class GetFlightsQueryHandler(IOperationsDbContext db, IOperationsScope scope)
    : IQueryHandler<GetFlightsQuery, PagedResult<FlightListItemDto>>
{
    public async Task<Result<PagedResult<FlightListItemDto>>> Handle(GetFlightsQuery request, CancellationToken cancellationToken)
    {
        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        var paging = PageRequest.From(request.Page, request.PageSize);
        var query = db.Flights.AsNoTracking().Where(f => f.Status != FlightStatus.Merged);

        if (!scopeResult.Value.IsAdministrator && scopeResult.Value.StationId is { } stationId)
            query = query.Where(f => f.Station.StationId == stationId);
        else if (request.StationId is { } filterStation)
            query = query.Where(f => f.Station.StationId == filterStation);

        if (request.CustomerId is { } customer)
            query = query.Where(f => f.Customer.CustomerId == customer);
        if (request.Status is { } status)
            query = query.Where(f => f.Status == status);
        if (request.FromUtc is { } from)
            query = query.Where(f => f.Schedule.Sta >= from);
        if (request.ToUtc is { } to)
            query = query.Where(f => f.Schedule.Sta <= to);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToUpperInvariant();
            query = query.Where(f => f.FlightNumber.Value.Contains(term) || f.OriginalFlightNumber.Contains(term) || f.Customer.Name.Contains(term));
        }

        var total = await query.LongCountAsync(cancellationToken);
        if (paging.IsOutOfRange(total))
            return paging.Empty<FlightListItemDto>(total);

        var items = await query
            .OrderByDescending(f => f.Schedule.Sta).ThenBy(f => f.Id)
            .Skip(paging.Skip).Take(paging.PageSize)
            .Select(f => new FlightListItemDto(
                f.Id,
                f.FlightNumber.Value,
                f.OriginalFlightNumber,
                f.Customer.Name,
                f.Station.IataCode,
                f.OperationType.Name,
                f.Schedule.Sta,
                f.Schedule.Std,
                f.Status.ToString(),
                f.PlannedServices.Any(p => p.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService)))
            .ToListAsync(cancellationToken);

        return paging.ToResult(items, total);
    }
}

// --- Scheduler calendar -----------------------------------------------------

public sealed record GetSchedulerCalendarQuery(DateTimeOffset FromUtc, DateTimeOffset ToUtc, Guid? StationId = null)
    : IQuery<IReadOnlyList<CalendarFlightDto>>;

public sealed class GetSchedulerCalendarQueryHandler(IOperationsDbContext db, IOperationsScope scope)
    : IQueryHandler<GetSchedulerCalendarQuery, IReadOnlyList<CalendarFlightDto>>
{
    public async Task<Result<IReadOnlyList<CalendarFlightDto>>> Handle(GetSchedulerCalendarQuery request, CancellationToken cancellationToken)
    {
        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        var query = db.Flights.AsNoTracking()
            .Where(f => f.Status != FlightStatus.Merged)
            .Where(f => f.Schedule.Sta >= request.FromUtc && f.Schedule.Sta <= request.ToUtc);

        if (!scopeResult.Value.IsAdministrator && scopeResult.Value.StationId is { } stationId)
            query = query.Where(f => f.Station.StationId == stationId);
        else if (request.StationId is { } filterStation)
            query = query.Where(f => f.Station.StationId == filterStation);

        IReadOnlyList<CalendarFlightDto> items = await query
            .OrderBy(f => f.Schedule.Sta)
            .Select(f => new CalendarFlightDto(
                f.Id,
                f.FlightNumber.Value,
                f.Customer.Name,
                f.Status.ToString(),
                f.PlannedServices.Any(p => p.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService),
                f.Schedule.Sta,
                f.Schedule.Std))
            .ToListAsync(cancellationToken);

        return Result.Success(items);
    }
}

// --- Flight by id -----------------------------------------------------------

public sealed record GetFlightByIdQuery(Guid Id) : IQuery<FlightDetailDto>;

public sealed class GetFlightByIdQueryHandler(IOperationsDbContext db, IOperationsScope scope)
    : IQueryHandler<GetFlightByIdQuery, FlightDetailDto>
{
    public async Task<Result<FlightDetailDto>> Handle(GetFlightByIdQuery request, CancellationToken cancellationToken)
    {
        var flight = await db.Flights.AsNoTracking()
            .Include(f => f.PlannedServices)
            .Include(f => f.AssignedEmployees)
            .FirstOrDefaultAsync(f => f.Id == request.Id, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var stationCheck = scopeResult.Value.EnsureStation(flight.Station.StationId);
        if (stationCheck.IsFailure)
            return stationCheck.Error;

        var workOrders = await db.WorkOrders.AsNoTracking()
            .Where(w => w.FlightId == flight.Id)
            .OrderBy(w => w.CreatedAtUtc)
            .Select(w => new WorkOrderSummaryDto(w.Id, w.Type.ToString(), w.Status.ToString(), w.Number == null ? null : w.Number.Value))
            .ToListAsync(cancellationToken);

        var dto = new FlightDetailDto(
            flight.Id,
            flight.FlightNumber.Value,
            flight.OriginalFlightNumber,
            flight.Customer.CustomerId,
            flight.Customer.Name,
            flight.Station.StationId,
            flight.Station.IataCode,
            flight.OperationType.OperationTypeId,
            flight.OperationType.Name,
            flight.AircraftType == null ? null : flight.AircraftType.AircraftTypeId,
            flight.AircraftType == null ? null : flight.AircraftType.Model,
            flight.Schedule.Sta,
            flight.Schedule.Std,
            flight.Status.ToString(),
            flight.IsPerLanding,
            flight.ContractId,
            flight.ContractNumber,
            flight.MergedIntoFlightId,
            flight.PotentialDuplicateOfFlightId,
            flight.PlannedServices.Select(p => new PlannedServiceDto(p.Service.ServiceId, p.Service.Name, p.IsAircraftPerLanding)).ToList(),
            flight.AssignedEmployees.Select(e => new AssignedEmployeeDto(e.Employee.StaffMemberId, e.Employee.FullName, e.Employee.EmployeeId)).ToList(),
            workOrders,
            flight.CreatedAtUtc,
            flight.UpdatedAtUtc,
            Convert.ToBase64String(flight.RowVersion));

        return dto;
    }
}
