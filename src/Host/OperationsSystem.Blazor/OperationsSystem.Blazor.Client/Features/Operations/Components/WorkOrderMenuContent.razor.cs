using Microsoft.AspNetCore.Components;
using OperationsSystem.Blazor.Client.Api;
using Radzen;

namespace OperationsSystem.Blazor.Client.Features.Operations.Components;

public partial class WorkOrderMenuContent
{
    [Inject] private OperationsApiClient Operations { get; set; } = default!;
    [Inject] private DialogService DialogService { get; set; } = default!;
    [Inject] private NotificationService Notifications { get; set; } = default!;
    [Inject] private ContextMenuService ContextMenu { get; set; } = default!;

    [Parameter, EditorRequired] public WorkOrderSummaryModel WorkOrder { get; set; } = default!;
    [Parameter] public EventCallback Changed { get; set; }

    private bool busy;
    private enum ActionKind { View, Edit, Approve, Return, Merge, Delete }
    private WorkOrderActionAvailability Availability =>
        WorkOrderActionPolicy.Available(WorkOrder, Auth.User?.Id, Auth.User?.Permissions ?? []);

    private async Task RunAsync(ActionKind action)
    {
        var available = Availability;
        var allowed = action switch
        {
            ActionKind.View => available.View,
            ActionKind.Edit => available.Edit,
            ActionKind.Approve => available.Approve,
            ActionKind.Return => available.Return,
            ActionKind.Merge => available.Merge,
            ActionKind.Delete => available.Delete,
            _ => false
        };
        if (busy || !allowed)
            return;

        busy = true;
        var workOrder = WorkOrder;
        ContextMenu.Close();
        switch (action)
        {
            case ActionKind.View:
            case ActionKind.Edit:
                await OpenAsync(workOrder, action == ActionKind.View);
                break;
            case ActionKind.Approve:
                await ApproveAsync(workOrder);
                break;
            case ActionKind.Return:
                await ReturnAsync(workOrder);
                break;
            case ActionKind.Merge:
                await MergeAsync(workOrder);
                break;
            case ActionKind.Delete:
                await DeleteAsync(workOrder);
                break;
        }
    }

    private async Task OpenAsync(WorkOrderSummaryModel workOrder, bool viewOnly)
    {
        var result = await DialogService.OpenAsync<WorkOrderEditorDialog>(
            viewOnly ? "Work order" : "Edit work order",
            new Dictionary<string, object?>
            {
                [nameof(WorkOrderEditorDialog.WorkOrderId)] = workOrder.Id,
                [nameof(WorkOrderEditorDialog.Type)] = workOrder.Type,
                [nameof(WorkOrderEditorDialog.ViewOnly)] = viewOnly
            },
            new DialogOptions { Width = "760px" });

        if (result is true or "approved" or "returned")
        {
            var title = result switch
            {
                "approved" => "Work order approved",
                "returned" => "Work order returned",
                _ => "Work order updated"
            };
            Notifications.Notify(NotificationSeverity.Success, title);
            await Changed.InvokeAsync();
        }
    }

    private async Task ApproveAsync(WorkOrderSummaryModel workOrder)
    {
        try
        {
            await Operations.ApproveWorkOrderAsync(workOrder.Id, workOrder.RowVersion);
            Notifications.Notify(NotificationSeverity.Success, "Work order approved");
            await Changed.InvokeAsync();
        }
        catch (ApiException ex)
        {
            Notifications.Notify(NotificationSeverity.Error, "Approval failed", ex.ToDisplayMessage("Could not approve the work order."));
        }
    }

    private async Task ReturnAsync(WorkOrderSummaryModel workOrder)
    {
        var reason = await DialogService.OpenAsync<WorkOrderReturnDialog>("Return work order", null,
            new DialogOptions { Width = "520px" });
        if (reason is not string value || string.IsNullOrWhiteSpace(value))
            return;

        try
        {
            await Operations.ReturnWorkOrderAsync(workOrder.Id, new ReturnWorkOrderRequestModel(value), workOrder.RowVersion);
            Notifications.Notify(NotificationSeverity.Success, "Work order returned");
            await Changed.InvokeAsync();
        }
        catch (ApiException ex)
        {
            Notifications.Notify(NotificationSeverity.Error, "Return failed", ex.ToDisplayMessage("Could not return the work order."));
        }
    }

    private async Task MergeAsync(WorkOrderSummaryModel workOrder)
    {
        var result = await DialogService.OpenAsync<WorkOrderMergeDialog>("Merge work orders",
            new Dictionary<string, object?> { [nameof(WorkOrderMergeDialog.FlightId)] = workOrder.FlightId },
            new DialogOptions { Width = "980px" });
        if (result is true)
        {
            Notifications.Notify(NotificationSeverity.Success, "Work orders merged");
            await Changed.InvokeAsync();
        }
    }

    private async Task DeleteAsync(WorkOrderSummaryModel workOrder)
    {
        var confirmed = await DialogService.Confirm("Delete this work order?", "Delete work order",
            new ConfirmOptions { OkButtonText = "Delete", CancelButtonText = "Cancel" });
        if (confirmed is not true)
            return;

        try
        {
            await Operations.DeleteWorkOrderAsync(workOrder.Id, workOrder.RowVersion);
            Notifications.Notify(NotificationSeverity.Success, "Work order deleted");
            await Changed.InvokeAsync();
        }
        catch (ApiException ex)
        {
            Notifications.Notify(NotificationSeverity.Error, "Delete failed", ex.ToDisplayMessage("Could not delete the work order."));
        }
    }
}
