using BuildingBlocks.Application.Mobile;
using Microsoft.Extensions.DependencyInjection;

namespace Operations.Api.Mobile;

public static class MobileSyncExtensions
{
    /// <summary>
    /// Registers SignalR and the scoped mobile-sync broadcaster. The host must also add
    /// <see cref="MobileSyncBroadcastBehavior{TRequest,TResponse}"/> as the outermost
    /// MediatR behavior and map <see cref="MobileSyncHub"/> at <see cref="MobileSyncHub.Path"/>.
    /// </summary>
    public static IServiceCollection AddMobileSync(this IServiceCollection services)
    {
        services.AddSignalR();
        services.AddScoped<IMobileSyncBroadcaster, SignalRMobileSyncBroadcaster>();
        return services;
    }
}
