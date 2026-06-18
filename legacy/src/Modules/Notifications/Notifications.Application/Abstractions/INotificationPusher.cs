using Notifications.Contracts.Notifications;

namespace Notifications.Application.Abstractions;

/// <summary>
/// Live-update sink. Application-layer command handlers call <see cref="PushAsync"/> after
/// writing a new notification so connected clients (portal bell, mobile inbox) receive an
/// immediate update without polling. Implemented by the SignalR hub adapter in the
/// Notifications.Infrastructure project.
/// </summary>
public interface INotificationPusher
{
    Task PushAsync(Guid recipientUserId, NotificationDto notification, CancellationToken cancellationToken = default);
}
