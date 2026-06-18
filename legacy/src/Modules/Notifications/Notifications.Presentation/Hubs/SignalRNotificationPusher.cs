using Microsoft.AspNetCore.SignalR;
using Notifications.Application.Abstractions;
using Notifications.Contracts.Notifications;

namespace Notifications.Presentation.Hubs;

/// <summary>
/// Application-layer <see cref="INotificationPusher"/> implemented over the SignalR hub.
/// Every notification produced by the application layer (e.g. by the
/// <c>FlightEmployeeInvitedIntegrationEventHandler</c>) is broadcast to the recipient's
/// per-user group, so connected portal bells and mobile inboxes refresh immediately.
/// Also fans out to <see cref="INotificationEventBus"/> so in-process Blazor Server
/// components can update without going over the wire.
/// </summary>
public sealed class SignalRNotificationPusher(
    IHubContext<NotificationsHub> hub,
    INotificationEventBus eventBus) : IInnerNotificationPusher
{
    public const string ClientMethodName = "notification";

    public async Task PushAsync(Guid recipientUserId, NotificationDto notification, CancellationToken cancellationToken = default)
    {
        await hub.Clients
            .Group(NotificationsHub.GroupName(recipientUserId))
            .SendAsync(ClientMethodName, notification, cancellationToken);
        eventBus.Publish(recipientUserId, notification);
    }
}
