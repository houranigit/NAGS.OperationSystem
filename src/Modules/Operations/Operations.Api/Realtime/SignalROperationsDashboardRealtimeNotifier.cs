using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Operations.Application.Abstractions;

namespace Operations.Api.Realtime;

public sealed class SignalROperationsDashboardRealtimeNotifier(
    IHubContext<OperationsDashboardHub> hub) : IOperationsDashboardRealtimeNotifier
{
    public Task NotifyChangedAsync(CancellationToken cancellationToken = default) =>
        hub.Clients.All.SendCoreAsync(
            OperationsDashboardHub.DashboardChangedClientMethod,
            [],
            cancellationToken);
}

public static class OperationsDashboardRealtimeExtensions
{
    public static IServiceCollection AddOperationsDashboardRealtime(this IServiceCollection services)
    {
        services.AddSignalR();
        services.AddScoped<IOperationsDashboardRealtimeNotifier, SignalROperationsDashboardRealtimeNotifier>();
        return services;
    }
}
