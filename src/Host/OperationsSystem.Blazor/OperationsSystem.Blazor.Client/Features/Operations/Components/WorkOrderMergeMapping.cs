using OperationsSystem.Blazor.Client.Api;

namespace OperationsSystem.Blazor.Client.Features.Operations.Components;

/// <summary>
/// Keeps compatibility-flattened return-to-ramp children out of the ordinary merge selections and
/// emits each canonical occurrence exactly once. A null result deliberately asks the server-side
/// merge cloner to recover canonical grouping for an older response that omitted ReturnToRamps.
/// </summary>
internal static class WorkOrderMergeMapping
{
    internal static string? ResolveRemarks(
        Guid? customerId,
        bool isCompletion,
        string? sourceRemarks,
        string? customerRemarksOverride) =>
        WorkOrderEntryRules.RequiresCustomerDescription(customerId)
            ? (customerRemarksOverride ?? sourceRemarks)?.Trim()
            : isCompletion ? sourceRemarks : null;

    internal static string? ValidateRemarks(Guid? customerId, string? remarks)
    {
        if (WorkOrderEntryRules.RequiresCustomerDescription(customerId) && string.IsNullOrWhiteSpace(remarks))
            return "Describe the Unknown customer before merging the work orders.";
        if (remarks?.Trim().Length > 2000)
            return "Remarks must be at most 2000 characters.";
        return null;
    }

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
            Attachments: null,
            EmployeeAssignments: line.PerformedBy.Select(item => new WorkOrderEmployeeAssignmentRequestModel(
                item.StaffMemberId, item.FromUtc ?? line.FromUtc, item.ToUtc ?? line.ToUtc)).ToList());

    internal static WorkOrderTaskRequestModel ToRequest(WorkOrderTaskModel task) =>
        new(
            // Source identity lets the server retain inactive ATA snapshots before allocating the merged task.
            Id: task.Id,
            task.TaskType,
            task.Description,
            task.FromUtc,
            task.ToUtc,
            task.Employees.Select(employee => employee.StaffMemberId).ToList(),
            task.Tools.Select(tool => new WorkOrderTaskToolRequestModel(
                tool.ToolId,
                tool.Quantity,
                tool.FromUtc,
                tool.ToUtc,
                tool.Description)).ToList(),
            task.Materials.Select(material => new WorkOrderTaskMaterialRequestModel(
                material.MaterialId,
                material.Quantity,
                material.FromUtc,
                material.ToUtc,
                material.Description)).ToList(),
            task.GeneralSupports.Select(support => new WorkOrderTaskGeneralSupportRequestModel(
                support.GeneralSupportId,
                support.Quantity,
                support.FromUtc,
                support.ToUtc,
                support.Description)).ToList(),
            Attachments: null,
            IsReturnToRamp: false,
            EmployeeAssignments: task.Employees.Select(item => new WorkOrderEmployeeAssignmentRequestModel(
                item.StaffMemberId, item.FromUtc ?? task.FromUtc, item.ToUtc ?? task.ToUtc)).ToList(),
            AtaChapterId: task.AtaChapterId);

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
