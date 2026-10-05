using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;

namespace OperationsSystem.Blazor.Client.Features.Operations;

public sealed record WorkOrderActionAvailability(
    bool View,
    bool Edit,
    bool Approve,
    bool Return,
    bool Merge,
    bool Delete);

/// <summary>Shared presentation rules for work-order actions. The API remains authoritative.</summary>
public static class WorkOrderActionPolicy
{
    public static WorkOrderActionAvailability Available(
        WorkOrderSummaryModel workOrder,
        Guid? userId,
        IReadOnlyCollection<string> permissions)
    {
        var canView = permissions.Contains(OperationsPermissions.WorkOrdersView);
        var canAuthor = permissions.Contains(OperationsPermissions.WorkOrdersAuthor);
        var canApprove = permissions.Contains(OperationsPermissions.WorkOrdersApprove);
        var mutable = workOrder.Status is "Submitted" or "Returned";
        var owner = userId.HasValue && userId.Value == workOrder.OwnerUserId;

        return new(
            View: canView,
            Edit: canView && canAuthor && mutable
                && (owner || permissions.Contains(OperationsPermissions.WorkOrdersManageOthers)),
            Approve: canView && canApprove && mutable,
            Return: canView && canApprove && workOrder.Status is "Approved",
            Merge: canView && mutable && permissions.Contains(OperationsPermissions.WorkOrdersMerge),
            Delete: canView && canAuthor && mutable
                && (owner || permissions.Contains(OperationsPermissions.WorkOrdersDeleteOthers)));
    }
}
