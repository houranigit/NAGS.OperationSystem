using Microsoft.Extensions.DependencyInjection;
using Notifications.Api.Realtime;
using Notifications.Application.Abstractions;

namespace Notifications.Api;

public static class NotificationsApiExtensions
{
    public static IServiceCollection AddNotificationsApi(this IServiceCollection services)
    {
        services.AddSignalR();
        services.AddScoped<INotificationTransport, SignalRNotificationPusher>();
        return services;
    }
}
