using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Contracts;
using Operations.Domain.Enumerations;

namespace Operations.Application.Features.WorkOrders;

// --- Work order detail ------------------------------------------------------

public sealed record GetWorkOrderByIdQuery(Guid Id) : IQuery<WorkOrderDetailDto>;

public sealed class GetWorkOrderByIdQueryHandler(IOperationsDbContext db, IOperationsScope scope)
    : IQueryHandler<GetWorkOrderByIdQuery, WorkOrderDetailDto>
{
    public async Task<Result<WorkOrderDetailDto>> Handle(GetWorkOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var workOrder = await db.WorkOrders.AsNoTracking()
            .Include(w => w.ServiceLines).ThenInclude(l => l.Employees)
            .Include(w => w.Tasks).ThenInclude(t => t.Employees)
            .Include(w => w.Tasks).ThenInclude(t => t.Tools)
            .Include(w => w.Tasks).ThenInclude(t => t.Materials)
            .Include(w => w.Tasks).ThenInclude(t => t.GeneralSupports)
            .FirstOrDefaultAsync(w => w.Id == request.Id, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.", "Operations.WorkOrder.NotFound");

        // Reads enforce the same visibility as writes: station staff may see a work order only when
        // they can access its flight (Per-Landing station-wide, otherwise assigned staff only).
        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        var flight = await db.Flights.AsNoTracking()
            .Include(f => f.PlannedServices)
            .Include(f => f.AssignedEmployees)
            .FirstOrDefaultAsync(f => f.Id == workOrder.FlightId, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        var accessCheck = scopeResult.Value.EnsureFlightAccess(flight);
        if (accessCheck.IsFailure)
            return accessCheck.Error;

        var lines = workOrder.ServiceLines.Select(l => new WorkOrderServiceLineDto(
            l.Id, l.Service.ServiceId, l.Service.Name, l.Origin.ToString(), l.Window.From, l.Window.To,
            l.Description, l.ReturnToRamp,
            l.Employees.Select(e => new AssignedEmployeeDto(e.Employee.StaffMemberId, e.Employee.FullName, e.Employee.EmployeeId)).ToList())).ToList();

        var tasks = workOrder.Tasks.Select(t => new WorkOrderTaskDto(
            t.Id, t.TaskType.ToString(), t.Description, t.Window.From, t.Window.To, t.ReturnToRamp,
            t.Employees.Select(e => new AssignedEmployeeDto(e.Employee.StaffMemberId, e.Employee.FullName, e.Employee.EmployeeId)).ToList(),
            t.Tools.Select(x => new WorkOrderResourceDto(x.Tool.ToolId, x.Tool.Name, x.Quantity.Value)).ToList(),
            t.Materials.Select(x => new WorkOrderResourceDto(x.Material.MaterialId, x.Material.Name, x.Quantity.Value)).ToList(),
            t.GeneralSupports.Select(x => new WorkOrderResourceDto(x.GeneralSupport.GeneralSupportId, x.GeneralSupport.Name, x.Quantity.Value)).ToList())).ToList();

        var dto = new WorkOrderDetailDto(
            workOrder.Id,
            workOrder.FlightId,
            workOrder.Type.ToString(),
            workOrder.Status.ToString(),
            workOrder.Number?.Value,
            workOrder.OwnerStaffMemberId,
            workOrder.Owner?.FullName,
            workOrder.FlightNumber.Value,
            workOrder.Customer.Name,
            workOrder.Station.IataCode,
            workOrder.AircraftType?.AircraftTypeId,
            workOrder.AircraftType?.Model,
            workOrder.AircraftTailNumber,
            workOrder.Schedule.Sta,
            workOrder.Schedule.Std,
            workOrder.Actuals?.Ata,
            workOrder.Actuals?.Atd,
            workOrder.Remarks,
            workOrder.CustomerSignatureReference,
            workOrder.Cancellation?.CanceledAtUtc,
            workOrder.Cancellation?.Reason,
            lines,
            tasks,
            workOrder.CreatedAtUtc,
            workOrder.UpdatedAtUtc,
            Convert.ToBase64String(workOrder.RowVersion));

        return dto;
    }
}

// --- Review queue -----------------------------------------------------------

public sealed record GetReviewQueueQuery(int Page = 1, int PageSize = 20, Guid? StationId = null) : IQuery<PagedResult<ReviewQueueItemDto>>;

public sealed class GetReviewQueueQueryHandler(IOperationsDbContext db, IOperationsScope scope)
    : IQueryHandler<GetReviewQueueQuery, PagedResult<ReviewQueueItemDto>>
{
    public async Task<Result<PagedResult<ReviewQueueItemDto>>> Handle(GetReviewQueueQuery request, CancellationToken cancellationToken)
    {
        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        var paging = PageRequest.From(request.Page, request.PageSize);
        var query = db.WorkOrders.AsNoTracking().Where(w => w.Status == WorkOrderStatus.Submitted);

        // Station staff only see submitted work orders for flights they can access (their station's
        // Per-Landing flights plus flights they are assigned to); holders of view-station (station
        // dispatchers) see all of their station's submitted work orders; admins/reviewers see everything.
        if (!scopeResult.Value.IsAdministrator && scopeResult.Value.StationId is { } stationId)
        {
            query = query.Where(w => w.Station.StationId == stationId);

            if (!scopeResult.Value.CanViewStationWide)
            {
                var staffId = scopeResult.Value.StaffMemberId;
                query = query.Where(w => db.Flights.Any(f => f.Id == w.FlightId &&
                    (f.PlannedServices.Any(p => p.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService)
                     || f.AssignedEmployees.Any(e => e.Employee.StaffMemberId == staffId))));
            }
        }
        else if (request.StationId is { } filterStation)
        {
            query = query.Where(w => w.Station.StationId == filterStation);
        }

        var total = await query.LongCountAsync(cancellationToken);
        if (paging.IsOutOfRange(total))
            return paging.Empty<ReviewQueueItemDto>(total);

        var items = await query
            .OrderBy(w => w.CreatedAtUtc).ThenBy(w => w.Id)
            .Skip(paging.Skip).Take(paging.PageSize)
            .Select(w => new ReviewQueueItemDto(
                w.Id, w.FlightId, w.FlightNumber.Value, w.Station.IataCode, w.Customer.Name,
                w.Type.ToString(), w.Status.ToString(), w.CreatedAtUtc, Convert.ToBase64String(w.RowVersion)))
            .ToListAsync(cancellationToken);

        return paging.ToResult(items, total);
    }
}
