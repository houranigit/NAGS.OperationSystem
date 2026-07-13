using Microsoft.AspNetCore.SignalR;
using Notifications.Application.Abstractions;
using Notifications.Contracts;

namespace Notifications.Api.Realtime;

public sealed class SignalRNotificationPusher(IHubContext<NotificationsHub> hub) : INotificationTransport
{
    public Task PushAsync(Guid recipientUserId, NotificationDto notification, CancellationToken cancellationToken = default) =>
        hub.Clients.Group(NotificationsHub.GroupName(recipientUserId))
            .SendAsync(NotificationsHub.ClientMethod, notification, cancellationToken);
}
