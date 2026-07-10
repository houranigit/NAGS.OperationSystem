using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Contracts;
using Operations.Domain.Enumerations;
using Operations.Domain.WorkOrders;

namespace Operations.Application.Features.WorkOrders;

public sealed record GetWorkOrdersQuery(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    Guid? StationId = null,
    WorkOrderStatus? Status = null,
    WorkOrderType? Type = null,
    Guid? FlightId = null,
    Guid? OwnerUserId = null,
    string? Sort = null) : IQuery<PagedResult<WorkOrderListItemDto>>;

public sealed class GetWorkOrdersQueryHandler(IOperationsDbContext db, IOperationsScope scope, IUserContext user)
    : IQueryHandler<GetWorkOrdersQuery, PagedResult<WorkOrderListItemDto>>
{
    public async Task<Result<PagedResult<WorkOrderListItemDto>>> Handle(GetWorkOrdersQuery request, CancellationToken cancellationToken)
    {
        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        var paging = PageRequest.From(request.Page, request.PageSize);
        var query = WorkOrderQueryVisibility.ApplyVisibility(db.WorkOrders.AsNoTracking(), scopeResult.Value, user);

        if (request.StationId is { } stationId)
            query = query.Where(w => w.Station.StationId == stationId);
        if (request.Status is { } status)
            query = query.Where(w => w.Status == status);
        if (request.Type is { } type)
            query = query.Where(w => w.Type == type);
        if (request.FlightId is { } flightId)
            query = query.Where(w => w.FlightId == flightId);
        if (request.OwnerUserId is { } ownerUserId)
            query = query.Where(w => w.OwnerUserId == ownerUserId);
        if (SearchFilter.Term(request.Search) is { } term)
        {
            query = query.Where(w =>
                w.PlannedFlightNumber.Value.ToLower().Contains(term) ||
                w.ActualFlightNumber.Value.ToLower().Contains(term) ||
                w.Customer.Name.ToLower().Contains(term) ||
                w.Station.IataCode.ToLower().Contains(term) ||
                (w.ApprovalNumber != null && w.ApprovalNumber.ToLower().Contains(term)));
        }

        var total = await query.LongCountAsync(cancellationToken);
        if (paging.IsOutOfRange(total))
            return paging.Empty<WorkOrderListItemDto>(total);

        var ordered = request.Sort?.Equals("approval", StringComparison.OrdinalIgnoreCase) == true
            ? query.OrderBy(w => w.Station.IataCode).ThenBy(w => w.ApprovalSequence)
            : query.OrderByDescending(w => w.CreatedAtUtc).ThenByDescending(w => w.Id);

        var items = await ordered
            .Skip(paging.Skip).Take(paging.PageSize)
            .Select(w => new WorkOrderListItemDto(
                w.Id,
                w.FlightId,
                w.PlannedFlightNumber.Value,
                w.ActualFlightNumber.Value,
                w.Customer.IataCode,
                w.Customer.Name,
                w.Station.StationId,
                w.Station.IataCode,
                w.Type.ToString(),
                w.Status.ToString(),
                w.ApprovalNumber,
                w.ApprovalSequence,
                w.OwnerUserId,
                w.Owner == null ? null : w.Owner.FullName,
                w.Schedule.Sta,
                w.CreatedAtUtc,
                w.UpdatedAtUtc,
                Convert.ToBase64String(w.RowVersion)))
            .ToListAsync(cancellationToken);

        return paging.ToResult(items, total);
    }
}

public sealed record GetWorkOrderByIdQuery(Guid Id) : IQuery<WorkOrderDetailDto>;

public sealed class GetWorkOrderByIdQueryHandler(IOperationsDbContext db, IOperationsScope scope)
    : IQueryHandler<GetWorkOrderByIdQuery, WorkOrderDetailDto>
{
    public async Task<Result<WorkOrderDetailDto>> Handle(GetWorkOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var workOrder = await WorkOrderLoader.ForMutation(db.WorkOrders.AsNoTracking())
            .FirstOrDefaultAsync(w => w.Id == request.Id, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.", "Operations.WorkOrder.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var access = scopeResult.Value.EnsureWorkOrderAccess(workOrder);
        if (access.IsFailure)
            return access.Error;

        return WorkOrderDtoMapper.Detail(workOrder);
    }
}

public sealed record GetMyWorkOrderForFlightQuery(Guid FlightId) : IQuery<WorkOrderSummaryDto?>;

public sealed class GetMyWorkOrderForFlightQueryHandler(IOperationsDbContext db, IOperationsScope scope, IUserContext user)
    : IQueryHandler<GetMyWorkOrderForFlightQuery, WorkOrderSummaryDto?>
{
    public async Task<Result<WorkOrderSummaryDto?>> Handle(GetMyWorkOrderForFlightQuery request, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
            return Error.Forbidden("The request is not authenticated.", "Operations.WorkOrder.Unauthenticated");

        var flight = await db.Flights.AsNoTracking()
            .Include(f => f.PlannedServices)
            .Include(f => f.AssignedEmployees)
            .FirstOrDefaultAsync(f => f.Id == request.FlightId, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var access = scopeResult.Value.EnsureFlightAccess(flight);
        if (access.IsFailure)
            return access.Error;

        var workOrder = await db.WorkOrders.AsNoTracking()
            .Where(w => w.FlightId == request.FlightId && w.OwnerUserId == userId)
            .Where(w => w.Status == WorkOrderStatus.Submitted || w.Status == WorkOrderStatus.Returned || w.Status == WorkOrderStatus.Approved)
            .OrderByDescending(w => w.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return workOrder is null ? Result.Success<WorkOrderSummaryDto?>(null) : WorkOrderDtoMapper.Summary(workOrder);
    }
}

public sealed record GetWorkOrderTimelineQuery(Guid WorkOrderId) : IQuery<IReadOnlyList<WorkOrderTimelineEntryDto>>;

public sealed class GetWorkOrderTimelineQueryHandler(IOperationsDbContext db, IOperationsScope scope)
    : IQueryHandler<GetWorkOrderTimelineQuery, IReadOnlyList<WorkOrderTimelineEntryDto>>
{
    public async Task<Result<IReadOnlyList<WorkOrderTimelineEntryDto>>> Handle(GetWorkOrderTimelineQuery request, CancellationToken cancellationToken)
    {
        var workOrder = await db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.Id == request.WorkOrderId, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.", "Operations.WorkOrder.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var access = scopeResult.Value.EnsureWorkOrderAccess(workOrder);
        if (access.IsFailure)
            return access.Error;

        IReadOnlyList<WorkOrderTimelineEntryDto> items = await db.WorkOrderTimelineEntries.AsNoTracking()
            .Where(e => e.WorkOrderId == workOrder.Id)
            .OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id)
            .Select(e => new WorkOrderTimelineEntryDto(
                e.Id, e.EventType.ToString(), e.OccurredAtUtc, e.ActorName, e.Details))
            .ToListAsync(cancellationToken);

        return Result.Success(items);
    }
}

internal static class WorkOrderQueryVisibility
{
    public static IQueryable<WorkOrder> ApplyVisibility(IQueryable<WorkOrder> query, OperationsScopeContext scope, IUserContext user)
    {
        if (scope.IsAdministrator)
            return query;

        if (scope.StationId is not { } stationId)
            return query.Where(_ => false);

        query = query.Where(w => w.Station.StationId == stationId);
        if (!scope.CanViewWorkOrdersStationWide)
        {
            if (user.UserId is not { } userId)
                return query.Where(_ => false);
            query = query.Where(w => w.OwnerUserId == userId);
        }

        return query;
    }
}

internal static class WorkOrderDtoMapper
{
    public static WorkOrderSummaryDto Summary(WorkOrder workOrder) =>
        new(
            workOrder.Id,
            workOrder.FlightId,
            workOrder.Type.ToString(),
            workOrder.Status.ToString(),
            workOrder.ApprovalNumber,
            workOrder.OwnerUserId,
            workOrder.Owner?.FullName,
            Convert.ToBase64String(workOrder.RowVersion));

    public static WorkOrderDetailDto Detail(WorkOrder workOrder) =>
        new(
            workOrder.Id,
            workOrder.FlightId,
            workOrder.Type.ToString(),
            workOrder.Status.ToString(),
            workOrder.IsMergeGenerated,
            workOrder.MergedIntoWorkOrderId,
            workOrder.OwnerUserId,
            workOrder.Owner?.FullName,
            workOrder.Customer.CustomerId,
            workOrder.Customer.IataCode,
            workOrder.Customer.Name,
            workOrder.Station.StationId,
            workOrder.Station.IataCode,
            workOrder.Station.Name,
            workOrder.OperationType.OperationTypeId,
            workOrder.OperationType.Name,
            workOrder.PlannedFlightNumber.Value,
            workOrder.Schedule.Sta,
            workOrder.Schedule.Std,
            workOrder.ActualFlightNumber.Value,
            workOrder.AircraftType?.AircraftTypeId,
            workOrder.AircraftType?.Model,
            workOrder.AircraftTailNumber,
            workOrder.Actuals?.Ata,
            workOrder.Actuals?.Atd,
            workOrder.Cancellation?.CanceledAtUtc,
            workOrder.Cancellation?.Reason,
            workOrder.Remarks,
            string.IsNullOrWhiteSpace(workOrder.CustomerSignatureReference) || workOrder.CustomerSignedAtUtc is null
                ? null
                : new WorkOrderSignatureDto(
                    workOrder.CustomerSignatureFileName ?? "customer-signature.png",
                    workOrder.CustomerSignatureContentType ?? "image/png",
                    workOrder.CustomerSignatureSize ?? 0,
                    workOrder.CustomerSignedAtUtc.Value),
            workOrder.ApprovalSequence,
            workOrder.ApprovalNumber,
            workOrder.ApprovedByUserId,
            workOrder.ApprovedAtUtc,
            workOrder.ServiceLines.Select(line => new WorkOrderServiceLineDto(
                line.Id,
                line.Service.ServiceId,
                line.Service.Name,
                line.PerformedBy.StaffMemberId,
                line.PerformedBy.FullName,
                line.Window.From,
                line.Window.To,
                line.Description)).ToList(),
            workOrder.Tasks.Select(task => new WorkOrderTaskDto(
                task.Id,
                task.TaskType.ToString(),
                task.Description,
                task.Window.From,
                task.Window.To,
                task.Employees.Select(e => new WorkOrderTaskEmployeeDto(e.Employee.StaffMemberId, e.Employee.FullName, e.Employee.EmployeeId)).ToList(),
                task.Tools.Select(t => new WorkOrderTaskToolDto(t.Tool.ToolId, t.Tool.Name, t.Quantity.Value)).ToList(),
                task.Materials.Select(m => new WorkOrderTaskMaterialDto(m.Material.MaterialId, m.Material.Name, m.Quantity.Value)).ToList(),
                task.GeneralSupports.Select(g => new WorkOrderTaskGeneralSupportDto(g.GeneralSupport.GeneralSupportId, g.GeneralSupport.Name, g.Quantity.Value)).ToList(),
                task.Attachments.Select(a => new WorkOrderTaskAttachmentDto(a.Id, a.Kind.ToString(), a.OriginalFileName, a.ContentType, a.Size)).ToList())).ToList(),
            workOrder.CreatedAtUtc,
            workOrder.UpdatedAtUtc,
            Convert.ToBase64String(workOrder.RowVersion));
}
