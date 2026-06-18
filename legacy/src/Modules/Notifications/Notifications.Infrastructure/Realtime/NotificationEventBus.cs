using Notifications.Application.Abstractions;
using Notifications.Contracts.Notifications;

namespace Notifications.Infrastructure.Realtime;

/// <summary>
/// Default singleton implementation of <see cref="INotificationEventBus"/>. Subscribers
/// are registered via the standard <see cref="Action{T1, T2}"/> event so Blazor
/// components can dispose their handlers cleanly with <c>-=</c>.
/// </summary>
public sealed class NotificationEventBus : INotificationEventBus
{
    public event Action<Guid, NotificationDto>? NotificationCreated;

    public void Publish(Guid recipientUserId, NotificationDto notification) =>
        NotificationCreated?.Invoke(recipientUserId, notification);
}
