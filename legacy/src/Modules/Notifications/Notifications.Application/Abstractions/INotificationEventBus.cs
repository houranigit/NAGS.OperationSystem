using Notifications.Contracts.Notifications;

namespace Notifications.Application.Abstractions;

/// <summary>
/// In-process event aggregator used by the Blazor Server portal so the notification bell
/// can react to new notifications without round-tripping through SignalR. Mobile and any
/// other out-of-process clients still receive updates over the SignalR hub.
/// </summary>
public interface INotificationEventBus
{
    event Action<Guid, NotificationDto>? NotificationCreated;
    void Publish(Guid recipientUserId, NotificationDto notification);
}
