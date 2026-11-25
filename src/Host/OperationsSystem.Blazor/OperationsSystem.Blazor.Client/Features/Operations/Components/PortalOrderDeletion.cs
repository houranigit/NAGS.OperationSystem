using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;
using Radzen;

namespace OperationsSystem.Blazor.Client.Features.Operations.Components;

internal static class PortalOrderDeletion
{
    public static bool CanDelete(AuthSession auth, PortalFlightOperations? flight, PortalFlightOrderKind kind) =>
        auth.IsSystemAdministrator && auth.HasPermission(OperationsPermissions.FlightsView) &&
        auth.HasPermission(OperationsPermissions.FlightsOperate) && flight is { IsApprovalLocked: false } &&
        (kind == PortalFlightOrderKind.WorkOrder ? flight.HasWorkOrderRecords : flight.CancellationSubmissions.Count > 0);

    public static async Task<bool> ConfirmAsync(DialogService dialogs, PortalFlightOrderKind kind, string flightNumber)
    {
        var title = kind == PortalFlightOrderKind.WorkOrder ? "Delete work order" : "Delete cancellation order";
        var message = kind == PortalFlightOrderKind.WorkOrder
            ? $"Delete all saved arrivals, departures, services, tasks, returns to ramp, and their files for flight {flightNumber}? The schedule and any cancellation reports will stay on file."
            : $"Delete all cancellation reports for flight {flightNumber}? The schedule and any saved work will stay on file.";
        return await dialogs.Confirm(message, title, new ConfirmOptions { OkButtonText = "Delete", CancelButtonText = "Cancel" }) == true;
    }
}
