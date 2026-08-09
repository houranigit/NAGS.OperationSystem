using OperationsSystem.Blazor.Client.Api;

namespace OperationsSystem.Blazor.Client.Features.Operations.Components;

/// <summary>
/// Keeps compatibility-flattened return-to-ramp children out of the ordinary merge selections and
/// emits each canonical occurrence exactly once. A null result deliberately asks the server-side
/// merge cloner to recover canonical grouping for an older response that omitted ReturnToRamps.
/// </summary>
internal static class WorkOrderMergeMapping
{
    internal static bool IsStandardServiceLine(WorkOrderServiceLineModel line) =>
        !line.IsReturnToRamp;

    internal static bool IsStandardTask(WorkOrderTaskModel task) =>
        !task.IsReturnToRamp;

    internal static WorkOrderServiceLineRequestModel ToRequest(
        WorkOrderServiceLineModel line) =>
        new(
            line.ServiceId,
            line.PerformedBy.Select(performer => performer.StaffMemberId).ToList(),
            line.FromUtc,
            line.ToUtc,
            line.Description,
            IsReturnToRamp: false,
            Id: null,
            Attachments: null);

    internal static WorkOrderTaskRequestModel ToRequest(WorkOrderTaskModel task) =>
        new(
            Id: null,
            task.TaskType,
            task.Description,
            task.FromUtc,
            task.ToUtc,
            task.Employees.Select(employee => employee.StaffMemberId).ToList(),
            task.Tools.Select(tool => new WorkOrderTaskToolRequestModel(
                tool.ToolId,
                tool.Quantity,
                tool.FromUtc,
                tool.ToUtc)).ToList(),
            task.Materials.Select(material => new WorkOrderTaskMaterialRequestModel(
                material.MaterialId,
                material.Quantity,
                material.FromUtc,
                material.ToUtc)).ToList(),
            task.GeneralSupports.Select(support => new WorkOrderTaskGeneralSupportRequestModel(
                support.GeneralSupportId,
                support.Quantity,
                support.FromUtc,
                support.ToUtc)).ToList(),
            Attachments: null,
            IsReturnToRamp: false);

    internal static IReadOnlyList<WorkOrderReturnToRampRequestModel>? BuildCanonicalReturnToRamps(
        IReadOnlyList<WorkOrderDetail> sources)
    {
        if (sources.Any(source => source.ReturnToRamps is null))
            return null;

        return sources
            .SelectMany(source => source.ReturnToRamps!)
            .OrderBy(item => item.FromUtc)
            .ThenBy(item => item.CreatedAtUtc)
            .ThenBy(item => item.Id)
            .Select(item => new WorkOrderReturnToRampRequestModel(
                Id: null,
                item.FromUtc,
                item.ToUtc,
                item.Description,
                item.ServiceLines.Select(ToRequest).ToList(),
                item.Tasks.Select(ToRequest).ToList()))
            .ToList();
    }
}
