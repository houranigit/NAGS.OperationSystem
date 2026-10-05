using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;

namespace OperationsSystem.Blazor.Client.Features.Operations.Components;

public partial class FlightWorkOrderMenu
{
    [Parameter] public Guid FlightId { get; set; }
    [Parameter] public EventCallback Changed { get; set; }

    private readonly CancellationTokenSource cancellation = new();
    private IReadOnlyList<WorkOrderSummaryModel> workOrders = [];
    private bool loading;
    private bool failed;

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        if (loading || !Auth.HasPermission(OperationsPermissions.WorkOrdersView))
            return;

        loading = true;
        failed = false;
        try
        {
            var flight = await Operations.GetFlightAsync(FlightId, cancellation.Token);
            workOrders = flight.WorkOrders;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (ApiException)
        {
            failed = true;
        }
        catch (JSException)
        {
            failed = true;
        }
        finally
        {
            loading = false;
        }
    }

    private static string WorkOrderLabel(WorkOrderSummaryModel workOrder) =>
        $"{(string.IsNullOrWhiteSpace(workOrder.ApprovalNumber) ? workOrder.Id.ToString("N")[..8].ToUpperInvariant() : workOrder.ApprovalNumber)} · {workOrder.Type} · {workOrder.Status}";

    public async ValueTask DisposeAsync()
    {
        await cancellation.CancelAsync();
        cancellation.Dispose();
    }
}
