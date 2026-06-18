using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Domain.Results;
using Operations.Contracts.WorkOrder;
using Operations.Domain.Aggregates.WorkOrder;
using DomainWorkOrderSnapshot = Operations.Contracts.WorkOrder.WorkOrderSnapshot;

namespace Operations.Application.Features.WorkOrder.Queries.GetWorkOrderById;

public sealed class GetWorkOrderByIdQueryHandler(IWorkOrderRepository workOrders)
    : IQueryHandler<GetWorkOrderByIdQuery, WorkOrderDetailDto?>
{
    public async Task<Result<WorkOrderDetailDto?>> Handle(GetWorkOrderByIdQuery request, CancellationToken cancellationToken)
    {
        if (request.Id == Guid.Empty)
            return Result<WorkOrderDetailDto?>.Success(null);

        var id = WorkOrderId.From(request.Id);
        var wo = await workOrders.GetByIdAsync(id, cancellationToken);
        if (wo is null)
            return Result<WorkOrderDetailDto?>.Success(null);

        var workOrderSnapshot = new DomainWorkOrderSnapshot(wo.Id.Value, wo.WorkOrderNo?.Value);

        var serviceLines = wo.ServiceLines
            .Select(s => new WorkOrderServiceLineDto(
                s.Id,
                s.Service,
                s.Employee,
                workOrderSnapshot,
                s.From,
                s.To,
                s.Description,
                s.ReturnToRamp))
            .ToList();

        var tasks = wo.Tasks
            .Select(t => new WorkOrderTaskDto(
                t.Id,
                t.TaskType,
                t.Description,
                t.From,
                t.To,
                t.ReturnToRamp,
                t.Employees.Select(e => e.Employee).ToList(),
                t.Tools.Select(x => x.Tool).ToList(),
                t.Materials.Select(x => x.Material).ToList(),
                t.GeneralSupports.Select(x => x.GeneralSupport).ToList(),
                t.Attachments.Select(a => new WorkOrderTaskAttachmentDto(
                    a.Id, a.Kind, a.ContentType, a.FileName, a.SizeBytes, a.CapturedAt, a.Bytes)).ToList()))
            .ToList();

        var dto = new WorkOrderDetailDto(
            wo.Id.Value,
            wo.WorkOrderNo?.Value,
            wo.FlightId is null ? null : new Operations.Contracts.Flight.FlightSnapshot(wo.FlightId.Value, wo.FlightNumber.Value),
            wo.Customer,
            wo.Station,
            wo.OperationType,
            wo.AircraftType,
            wo.AircraftTailNumber,
            wo.FlightNumber.Value,
            wo.Schedule.Sta,
            wo.Schedule.Std,
            wo.TimesActual?.Ata,
            wo.TimesActual?.Atd,
            wo.IsCanceled,
            wo.CanceledAt,
            wo.Status,
            wo.MarkedForDeletionAt,
            wo.Remarks,
            wo.CreatedByEmployeeId,
            serviceLines,
            tasks,
            wo.CreatedAt,
            wo.UpdatedAt,
            wo.CustomerSignature);

        return Result<WorkOrderDetailDto?>.Success(dto);
    }
}
