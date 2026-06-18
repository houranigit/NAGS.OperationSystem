using Operations.Contracts.Mobile;
using Operations.Contracts.WorkOrder;
using WorkOrderEntity = Operations.Domain.Aggregates.WorkOrder.WorkOrder;

namespace Operations.Application.Features.Mobile.Mapping;

/// <summary>
/// Maps a loaded <see cref="WorkOrderEntity"/> aggregate (with the standard mobile graph:
/// service lines, tasks, attachments) to the compact wire DTO embedded on flight summaries
/// and returned from the flight-context query.
/// </summary>
internal static class MobileMyWorkOrderDtoMapper
{
    public static MobileMyWorkOrderDto? ToDto(WorkOrderEntity? workOrder)
    {
        if (workOrder is null)
            return null;

        var snapshot = new WorkOrderSnapshot(workOrder.Id.Value, workOrder.WorkOrderNo?.Value);
        return new MobileMyWorkOrderDto(
            workOrder.Id.Value,
            workOrder.Status,
            workOrder.AircraftType?.AircraftTypeId,
            workOrder.AircraftTailNumber,
            workOrder.TimesActual?.Ata,
            workOrder.TimesActual?.Atd,
            workOrder.IsCanceled,
            workOrder.CanceledAt,
            workOrder.Remarks,
            workOrder.ServiceLines
                .Select(s => new WorkOrderServiceLineDto(
                    s.Id,
                    s.Service,
                    s.Employee,
                    snapshot,
                    s.From,
                    s.To,
                    s.Description,
                    s.ReturnToRamp))
                .ToList(),
            workOrder.Tasks
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
                .ToList(),
            CustomerSignature: workOrder.CustomerSignature);
    }
}
