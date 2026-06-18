using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using BuildingBlocks.Application.Abstractions.Mobile.Sync;
using Operations.Presentation.Mobile;
using Operations.Presentation.Mobile.Sync;

namespace Operations.Presentation;

public static class OperationsPresentationExtensions
{
    /// <summary>
    /// DI surface for the mobile Operations presentation layer. Registers the
    /// HttpContext-backed <see cref="IMobileEmployeeContext"/> implementation so the
    /// minimal API endpoints can resolve the current employee per request, plus the
    /// SignalR-backed <see cref="IMobileSyncBroadcaster"/> used by command handlers
    /// to push real-time updates to connected mobile clients.
    /// </summary>
    public static IServiceCollection AddOperationsMobilePresentation(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IMobileEmployeeContext, HttpContextMobileEmployeeContext>();

        // SignalR core registration. AddSignalR() is idempotent so calling it from
        // both Notifications.Presentation and here is safe and keeps each module's
        // wiring self-contained.
        services.AddSignalR();
        services.AddScoped<IMobileSyncBroadcaster, SignalRMobileSyncBroadcaster>();
        return services;
    }

    /// <summary>
    /// Mounts mobile API groups: the v2 minimal-API surface, the v2 sync REST companion
    /// to the <see cref="MobileSyncHub"/>, and the legacy v1 group (currently empty).
    /// The SignalR hub itself is mapped from <c>Host.Web/Program.cs</c> alongside the
    /// notifications hub so all hub mounts live in one place.
    /// </summary>
    public static IEndpointRouteBuilder MapOperationsMobileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMobileEndpoints();
        app.MapMobileV2Endpoints();
        app.MapMobileSyncEndpoints();
        return app;
    }
}
