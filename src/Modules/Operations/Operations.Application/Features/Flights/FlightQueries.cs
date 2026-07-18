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
using Operations.Application.Features.WorkOrders;
using Operations.Domain.Authorization;
using Operations.Domain.Enumerations;
using Operations.Domain.Flights;
using Operations.Domain.WorkOrders;

namespace Operations.Application.Features.Flights;

// --- Paged flights list -----------------------------------------------------

public sealed record GetFlightsQuery(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    Guid? StationId = null,
    Guid? CustomerId = null,
    Guid? OperationTypeId = null,
    IReadOnlyList<FlightStatus>? Statuses = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    IReadOnlyList<FlightServiceCategory>? ServiceCategories = null,
    string? Sort = null) : IQuery<PagedResult<FlightListItemDto>>;

/// <summary>
/// Returns every flight visible to the caller that matches the list filters. Pagination is
/// deliberately absent: callers use this query only to build complete export files.
/// </summary>
public sealed record GetFlightsExportQuery(
    string? Search = null,
    Guid? StationId = null,
    Guid? CustomerId = null,
    Guid? OperationTypeId = null,
    IReadOnlyList<FlightStatus>? Statuses = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    IReadOnlyList<FlightServiceCategory>? ServiceCategories = null,
    string? Sort = null) : IQuery<IReadOnlyList<FlightExportRowDto>>;

public sealed record GetPerLandingExtractionQuery(
    string? Search = null,
    Guid? StationId = null,
    Guid? CustomerId = null,
    Guid? OperationTypeId = null,
    IReadOnlyList<FlightStatus>? Statuses = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    IReadOnlyList<FlightServiceCategory>? ServiceCategories = null,
    string? Sort = null) : IQuery<IReadOnlyList<PerLandingExtractionItemDto>>;

public sealed class GetFlightsQueryHandler(IOperationsDbContext db, IOperationsScope scope)
    : IQueryHandler<GetFlightsQuery, PagedResult<FlightListItemDto>>
{
    public async Task<Result<PagedResult<FlightListItemDto>>> Handle(GetFlightsQuery request, CancellationToken cancellationToken)
    {
        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        var paging = PageRequest.From(request.Page, request.PageSize);
        var qualifyingWorkOrders = db.WorkOrders.AsNoTracking().QualifyingForOnCall();
        var query = FlightListQuery.ApplyScopeAndFilters(
            db.Flights.AsNoTracking(),
            scopeResult.Value,
            new FlightListFilter(
                request.Search,
                request.StationId,
                request.CustomerId,
                request.OperationTypeId,
                request.Statuses,
                request.FromUtc,
                request.ToUtc,
                request.ServiceCategories),
            qualifyingWorkOrders);

        var total = await query.LongCountAsync(cancellationToken);
        if (paging.IsOutOfRange(total))
            return paging.Empty<FlightListItemDto>(total);

        var items = await FlightListQuery.ApplySort(query, request.Sort)
            .Skip(paging.Skip).Take(paging.PageSize)
            .Select(f => new FlightListItemDto(
                f.Id,
                f.FlightNumber.Value,
                f.OriginalFlightNumber,
                f.Customer.IataCode,
                f.Customer.Name,
                f.Station.IataCode,
                f.OperationType.Name,
                f.Schedule.Sta,
                f.Schedule.Std,
                f.Status.ToString(),
                f.PlannedServices.Any(p => p.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService),
                f.PlannedServices.Any(p => p.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService) &&
                    qualifyingWorkOrders.Any(w => w.FlightId == f.Id)))
            .ToListAsync(cancellationToken);

        return paging.ToResult(items, total);
    }
}

public sealed class GetFlightsExportQueryHandler(IOperationsDbContext db, IOperationsScope scope)
    : IQueryHandler<GetFlightsExportQuery, IReadOnlyList<FlightExportRowDto>>
{
    public async Task<Result<IReadOnlyList<FlightExportRowDto>>> Handle(
        GetFlightsExportQuery request,
        CancellationToken cancellationToken)
    {
        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        var qualifyingWorkOrders = db.WorkOrders.AsNoTracking().QualifyingForOnCall();
        var query = FlightListQuery.ApplyScopeAndFilters(
            db.Flights.AsNoTracking(),
            scopeResult.Value,
            new FlightListFilter(
                request.Search,
                request.StationId,
                request.CustomerId,
                request.OperationTypeId,
                request.Statuses,
                request.FromUtc,
                request.ToUtc,
                request.ServiceCategories),
            qualifyingWorkOrders);

        var flightRows = await FlightListQuery.ApplySort(query, request.Sort)
            .Select(f => new
            {
                f.Id,
                FlightNumber = f.FlightNumber.Value,
                f.OriginalFlightNumber,
                CustomerIataCode = f.Customer.IataCode,
                CustomerName = f.Customer.Name,
                StationIata = f.Station.IataCode,
                StationName = f.Station.Name,
                OperationTypeName = f.OperationType.Name,
                ScheduledArrivalUtc = f.Schedule.Sta,
                ScheduledDepartureUtc = f.Schedule.Std,
                Status = f.Status.ToString(),
                IsPerLanding = f.PlannedServices.Any(p => p.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService),
                PlannedServiceNames = f.PlannedServices.Select(p => p.Service.Name).ToList(),
                AssignedEmployeeNames = f.AssignedEmployees.Select(e => e.Employee.FullName).ToList()
            })
            .ToListAsync(cancellationToken);

        var flightIds = flightRows.Select(f => f.Id).ToList();
        var approvedWorkOrders = await db.WorkOrders.AsNoTracking()
            .Where(w => flightIds.Contains(w.FlightId) && w.Status == WorkOrderStatus.Approved)
            .Select(w => new
            {
                w.FlightId,
                WorkOrder = new ApprovedWorkOrderExportDto(
                    w.ApprovalNumber,
                    w.ActualFlightNumber.Value,
                    w.Actuals == null ? null : w.Actuals.Ata,
                    w.Actuals == null ? null : w.Actuals.Atd,
                    w.AircraftType == null ? null : w.AircraftType.Manufacturer,
                    w.AircraftType == null ? null : w.AircraftType.Model,
                    w.AircraftTailNumber,
                    w.ServiceLines.Select(line => line.Service.Name).ToList(),
                    w.Remarks)
            })
            .ToDictionaryAsync(w => w.FlightId, w => w.WorkOrder, cancellationToken);

        IReadOnlyList<FlightExportRowDto> items = flightRows.Select(f => new FlightExportRowDto(
            f.Id,
            f.FlightNumber,
            f.OriginalFlightNumber,
            f.CustomerIataCode,
            f.CustomerName,
            f.StationIata,
            f.StationName,
            f.OperationTypeName,
            f.ScheduledArrivalUtc,
            f.ScheduledDepartureUtc,
            f.Status,
            f.IsPerLanding,
            f.PlannedServiceNames,
            f.AssignedEmployeeNames,
            approvedWorkOrders.GetValueOrDefault(f.Id))).ToList();

        return Result.Success(items);
    }
}

public sealed class GetPerLandingExtractionQueryHandler(IOperationsDbContext db, IOperationsScope scope)
    : IQueryHandler<GetPerLandingExtractionQuery, IReadOnlyList<PerLandingExtractionItemDto>>
{
    public async Task<Result<IReadOnlyList<PerLandingExtractionItemDto>>> Handle(
        GetPerLandingExtractionQuery request,
        CancellationToken cancellationToken)
    {
        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        var workOrders = db.WorkOrders.AsNoTracking();
        var qualifyingWorkOrders = workOrders.QualifyingForOnCall();
        var query = FlightListQuery.ApplyScopeAndFilters(
                db.Flights.AsNoTracking(),
                scopeResult.Value,
                new FlightListFilter(
                    request.Search,
                    request.StationId,
                    request.CustomerId,
                    request.OperationTypeId,
                    request.Statuses,
                    request.FromUtc,
                    request.ToUtc,
                    request.ServiceCategories),
                qualifyingWorkOrders)
            .Where(f => f.Status == FlightStatus.InProgress)
            .Where(f => f.PlannedServices.Any(p =>
                p.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService))
            .Where(f => !qualifyingWorkOrders.Any(w => w.FlightId == f.Id))
            .Where(f => workOrders.Any(w => w.FlightId == f.Id &&
                w.Type == WorkOrderType.Completion &&
                (w.Status == WorkOrderStatus.Submitted || w.Status == WorkOrderStatus.Returned)));

        var flights = await FlightListQuery.ApplySort(query, request.Sort)
            .Select(f => new
            {
                Flight = f,
                WorkOrder = workOrders
                    .Where(w => w.FlightId == f.Id &&
                        w.Type == WorkOrderType.Completion &&
                        (w.Status == WorkOrderStatus.Submitted || w.Status == WorkOrderStatus.Returned))
                    .OrderBy(w => w.OwnerUserId == Guid.Empty ? 0 : 1)
                    .ThenBy(w => w.CreatedAtUtc)
                    .Select(w => new { w.Id, w.RowVersion })
                    .First()
            })
            .Select(x => new PerLandingExtractionItemDto(
                x.Flight.Id,
                x.WorkOrder.Id,
                Convert.ToBase64String(x.WorkOrder.RowVersion),
                x.Flight.FlightNumber.Value,
                x.Flight.Customer.IataCode,
                x.Flight.Customer.Name,
                x.Flight.Station.IataCode,
                x.Flight.OperationType.Name,
                x.Flight.Schedule.Sta,
                x.Flight.Schedule.Std))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<PerLandingExtractionItemDto>>(flights);
    }
}

internal sealed record FlightListFilter(
    string? Search,
    Guid? StationId,
    Guid? CustomerId,
    Guid? OperationTypeId,
    IReadOnlyList<FlightStatus>? Statuses,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    IReadOnlyList<FlightServiceCategory>? ServiceCategories);

internal static class FlightListQuery
{
    public static IQueryable<Flight> ApplyScopeAndFilters(
        IQueryable<Flight> query,
        OperationsScopeContext scope,
        FlightListFilter filter,
        IQueryable<WorkOrder> qualifyingWorkOrders)
    {
        query = query.Where(f => f.Status != FlightStatus.Merged);

        // Station staff see their station's Per-Landing flights (station-wide) plus the flights they
        // are assigned to; holders of view-station (station dispatchers) see every flight at their
        // station; admins see everything (optionally filtered).
        if (!scope.IsAdministrator && scope.StationId is { } stationId)
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
        if (filter.Statuses is { Count: > 0 } statuses)
            query = query.Where(f => statuses.Contains(f.Status));
        if (filter.FromUtc is { } from)
            query = query.Where(f => f.Schedule.Sta >= from);
        if (filter.ToUtc is { } to)
            query = query.Where(f => f.Schedule.Sta <= to);
        if (filter.ServiceCategories is { Count: > 0 } categories)
        {
            var includePerLanding = categories.Contains(FlightServiceCategory.PerLanding);
            var includeOnCall = categories.Contains(FlightServiceCategory.OnCall);
            var includeOther = categories.Contains(FlightServiceCategory.Other);

            query = query.Where(f =>
                (includePerLanding && f.PlannedServices.Any(p =>
                    p.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService) &&
                    !qualifyingWorkOrders.Any(w => w.FlightId == f.Id)) ||
                (includeOnCall && f.PlannedServices.Any(p =>
                    p.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService) &&
                    qualifyingWorkOrders.Any(w => w.FlightId == f.Id)) ||
                (includeOther && !f.PlannedServices.Any(p =>
                    p.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService)));
        }
        if (SearchFilter.Term(filter.Search) is { } term)
        {
            var compactFlightTerm = CompactFlightSearchTerm(term);
            var flightIdTerm = ParseFlightIdSearchTerm(term);
            var hasFlightIdTerm = flightIdTerm.HasValue;
            var flightId = flightIdTerm.GetValueOrDefault();
            query = query.Where(f =>
                (hasFlightIdTerm && f.Id == flightId) ||
                f.FlightNumber.Value.ToLower().Contains(term) ||
                f.OriginalFlightNumber.ToLower().Contains(term) ||
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
            "originalflightnumber" => spec.Descending
                ? query.OrderByDescending(f => f.OriginalFlightNumber).ThenByDescending(f => f.Id)
                : query.OrderBy(f => f.OriginalFlightNumber).ThenBy(f => f.Id),
            "customername" => spec.Descending
                ? query.OrderByDescending(f => f.Customer.Name).ThenByDescending(f => f.Id)
                : query.OrderBy(f => f.Customer.Name).ThenBy(f => f.Id),
            "stationiata" => spec.Descending
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
            "status" => spec.Descending
                ? query.OrderByDescending(f => f.Status).ThenByDescending(f => f.Id)
                : query.OrderBy(f => f.Status).ThenBy(f => f.Id),
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

// --- Scheduler calendar -----------------------------------------------------

public sealed record GetSchedulerCalendarQuery(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    Guid? StationId = null,
    Guid? CustomerId = null,
    FlightStatus? Status = null)
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
        {
            query = query.Where(f => f.Station.StationId == stationId);

            if (!scopeResult.Value.CanViewStationWide)
            {
                var staffId = scopeResult.Value.StaffMemberId;
                query = query.Where(f => f.PlannedServices.Any(p => p.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService)
                                         || f.AssignedEmployees.Any(e => e.Employee.StaffMemberId == staffId));
            }
        }
        else if (request.StationId is { } filterStation)
        {
            query = query.Where(f => f.Station.StationId == filterStation);
        }

        if (request.CustomerId is { } customerId)
            query = query.Where(f => f.Customer.CustomerId == customerId);
        if (request.Status is { } status)
            query = query.Where(f => f.Status == status);

        var qualifyingWorkOrders = db.WorkOrders.AsNoTracking().QualifyingForOnCall();
        IReadOnlyList<CalendarFlightDto> items = await query
            .OrderBy(f => f.Schedule.Sta)
            .Select(f => new CalendarFlightDto(
                f.Id,
                f.FlightNumber.Value,
                f.Customer.IataCode,
                f.Customer.Name,
                f.Station.IataCode,
                f.Station.Name,
                f.Status.ToString(),
                f.PlannedServices.Any(p => p.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService),
                f.PlannedServices.Any(p => p.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService) &&
                    qualifyingWorkOrders.Any(w => w.FlightId == f.Id),
                f.Schedule.Sta,
                f.Schedule.Std))
            .ToListAsync(cancellationToken);

        return Result.Success(items);
    }
}

// --- Flight by id -----------------------------------------------------------

public sealed record GetFlightByIdQuery(Guid Id) : IQuery<FlightDetailDto>;

public sealed class GetFlightByIdQueryHandler(IOperationsDbContext db, IOperationsScope scope, IUserContext user)
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
        var accessCheck = scopeResult.Value.EnsureFlightAccess(flight);
        if (accessCheck.IsFailure)
            return accessCheck.Error;

        IReadOnlyList<WorkOrderSummaryDto> workOrders = user.HasPermission(OperationsPermissions.WorkOrders.View)
            ? await WorkOrderQueryVisibility.ApplyVisibility(
                    db.WorkOrders.AsNoTracking().Where(w => w.FlightId == flight.Id),
                    scopeResult.Value,
                    user)
                .OrderByDescending(w => w.CreatedAtUtc)
                .Select(w => new WorkOrderSummaryDto(
                    w.Id,
                    w.FlightId,
                    w.Type.ToString(),
                    w.Status.ToString(),
                    w.ApprovalNumber,
                    w.OwnerUserId,
                    w.Owner == null ? null : w.Owner.FullName,
                    Convert.ToBase64String(w.RowVersion)))
                .ToListAsync(cancellationToken)
            : [];

        var dto = new FlightDetailDto(
            flight.Id,
            flight.FlightNumber.Value,
            flight.OriginalFlightNumber,
            flight.Customer.CustomerId,
            flight.Customer.IataCode,
            flight.Customer.Name,
            flight.Station.StationId,
            flight.Station.IataCode,
            flight.Station.Name,
            flight.OperationType.OperationTypeId,
            flight.OperationType.Name,
            flight.AircraftType == null ? null : flight.AircraftType.AircraftTypeId,
            flight.AircraftType == null ? null : flight.AircraftType.Model,
            flight.Schedule.Sta,
            flight.Schedule.Std,
            flight.Status.ToString(),
            flight.IsPerLanding,
            flight.IsPerLanding && await db.WorkOrders.AsNoTracking()
                .QualifyingForOnCall()
                .AnyAsync(w => w.FlightId == flight.Id, cancellationToken),
            flight.ContractId,
            flight.ContractNumber,
            flight.MergedIntoFlightId,
            flight.PlannedServices.Select(p => new PlannedServiceDto(p.Service.ServiceId, p.Service.Name, p.IsAircraftPerLanding)).ToList(),
            flight.AssignedEmployees.Select(e => new AssignedEmployeeDto(e.Employee.StaffMemberId, e.Employee.FullName, e.Employee.EmployeeId)).ToList(),
            workOrders,
            flight.CreatedAtUtc,
            flight.UpdatedAtUtc,
            Convert.ToBase64String(flight.RowVersion));

        return dto;
    }
}

// --- Invite employee options ----------------------------------------------

public sealed record GetFlightInviteOptionsQuery(Guid FlightId) : IQuery<IReadOnlyList<AssignedEmployeeDto>>;

public sealed class GetFlightInviteOptionsQueryHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    IMasterDataReader masterData)
    : IQueryHandler<GetFlightInviteOptionsQuery, IReadOnlyList<AssignedEmployeeDto>>
{
    public async Task<Result<IReadOnlyList<AssignedEmployeeDto>>> Handle(GetFlightInviteOptionsQuery request, CancellationToken cancellationToken)
    {
        var flight = await db.Flights.AsNoTracking()
            .Include(f => f.PlannedServices)
            .Include(f => f.AssignedEmployees)
            .FirstOrDefaultAsync(f => f.Id == request.FlightId, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var accessCheck = scopeResult.Value.EnsureFlightAccess(flight);
        if (accessCheck.IsFailure)
            return accessCheck.Error;

        var editCheck = flight.EnsureScheduledDetailsEditable();
        if (editCheck.IsFailure)
            return editCheck.Error;

        if (flight.IsPerLanding)
            return PerLandingAssignmentGuard.Error();

        var assigned = flight.AssignedEmployees.Select(e => e.Employee.StaffMemberId).ToHashSet();
        var staff = await masterData.GetActiveStaffMembersForStationAsync(flight.Station.StationId, cancellationToken);
        var options = staff
            .Where(member => !assigned.Contains(member.Id))
            .Select(member => new AssignedEmployeeDto(member.Id, member.FullName, member.EmployeeId))
            .ToList();

        return options;
    }
}

// --- Flight timeline / history ------------------------------------------------

public sealed record GetFlightTimelineQuery(Guid FlightId) : IQuery<IReadOnlyList<FlightTimelineEntryDto>>;

public sealed class GetFlightTimelineQueryHandler(IOperationsDbContext db, IOperationsScope scope)
    : IQueryHandler<GetFlightTimelineQuery, IReadOnlyList<FlightTimelineEntryDto>>
{
    public async Task<Result<IReadOnlyList<FlightTimelineEntryDto>>> Handle(GetFlightTimelineQuery request, CancellationToken cancellationToken)
    {
        var flight = await db.Flights.AsNoTracking()
            .Include(f => f.PlannedServices)
            .Include(f => f.AssignedEmployees)
            .FirstOrDefaultAsync(f => f.Id == request.FlightId, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var accessCheck = scopeResult.Value.EnsureFlightAccess(flight);
        if (accessCheck.IsFailure)
            return accessCheck.Error;

        IReadOnlyList<FlightTimelineEntryDto> items = await db.FlightTimelineEntries.AsNoTracking()
            .Where(e => e.FlightId == flight.Id)
            .OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id)
            .Select(e => new FlightTimelineEntryDto(
                e.Id, e.EventType.ToString(), e.OccurredAtUtc, e.ActorName, e.Details))
            .ToListAsync(cancellationToken);

        return Result.Success(items);
    }
}
