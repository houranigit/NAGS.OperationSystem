using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Application.Abstractions;
using Notifications.Presentation.Endpoints;
using Notifications.Presentation.Hubs;

namespace Notifications.Presentation;

public static class NotificationsPresentationExtensions
{
    /// <summary>
    /// Registers SignalR + the SignalR-backed transport that participates in the composite
    /// <see cref="INotificationPusher"/>. The FCM transport and the composite itself are
    /// wired by <c>NotificationsModuleExtensions</c>; that ordering makes
    /// <c>AddNotificationsModule</c> the single owner of the multi-transport push fan-out.
    /// Call before <c>app.UseAuthentication()</c>.
    /// </summary>
    public static IServiceCollection AddNotificationsPresentation(this IServiceCollection services)
    {
        services.AddSignalR();
        services.AddScoped<SignalRNotificationPusher>();
        services.AddScoped<IInnerNotificationPusher>(sp => sp.GetRequiredService<SignalRNotificationPusher>());
        return services;
    }

    public static IEndpointRouteBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapNotificationEndpoints();
        app.MapHub<NotificationsHub>(NotificationsHub.Path);
        return app;
    }
}
