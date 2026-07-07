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

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        var viewCheck = WorkOrderOwnership.EnsureCanView(scopeResult.Value, workOrder);
        if (viewCheck.IsFailure)
            return viewCheck.Error;

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

// --- Work order timeline / history -----------------------------------------

public sealed record GetWorkOrderTimelineQuery(Guid WorkOrderId) : IQuery<IReadOnlyList<WorkOrderTimelineEntryDto>>;

public sealed class GetWorkOrderTimelineQueryHandler(IOperationsDbContext db, IOperationsScope scope)
    : IQueryHandler<GetWorkOrderTimelineQuery, IReadOnlyList<WorkOrderTimelineEntryDto>>
{
    public async Task<Result<IReadOnlyList<WorkOrderTimelineEntryDto>>> Handle(GetWorkOrderTimelineQuery request, CancellationToken cancellationToken)
    {
        var workOrder = await db.WorkOrders.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == request.WorkOrderId, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.", "Operations.WorkOrder.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        var viewCheck = WorkOrderOwnership.EnsureCanView(scopeResult.Value, workOrder);
        if (viewCheck.IsFailure)
            return viewCheck.Error;

        IReadOnlyList<WorkOrderTimelineEntryDto> items = await db.WorkOrderTimelineEntries.AsNoTracking()
            .Where(e => e.WorkOrderId == workOrder.Id)
            .OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id)
            .Select(e => new WorkOrderTimelineEntryDto(
                e.Id, e.EventType.ToString(), e.OccurredAtUtc, e.ActorName, e.WorkOrderNumber, e.Details))
            .ToListAsync(cancellationToken);

        return Result.Success(items);
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
        var query = db.WorkOrders.AsNoTracking()
            .Where(w => w.Status == WorkOrderStatus.Submitted)
            .Where(w => w.SupersededByWorkOrderId == null)
            .Where(w => !db.WorkOrderTimelineEntries
                .Where(e => e.WorkOrderId == w.Id)
                .OrderByDescending(e => e.OccurredAtUtc)
                .ThenByDescending(e => e.Id)
                .Select(e => e.EventType)
                .Take(1)
                .Any(eventType => eventType == WorkOrderTimelineEventType.Returned));

        // Station staff only see submitted work orders for flights they can access (their station's
        // Per-Landing flights plus flights they are assigned to); holders of view-station (station
        // dispatchers) see all of their station's submitted work orders; admins/reviewers see everything.
        if (!scopeResult.Value.IsAdministrator && scopeResult.Value.StationId is { } stationId)
        {
            query = query.Where(w => w.Station.StationId == stationId);

            if (!scopeResult.Value.CanViewStationWide)
            {
                var staffId = scopeResult.Value.StaffMemberId;
                query = query.Where(w => w.OwnerStaffMemberId == staffId);
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
