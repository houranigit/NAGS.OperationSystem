using Notifications.Contracts;

namespace Notifications.Application.Abstractions;

public interface INotificationPusher
{
    public Task PushAsync(Guid recipientUserId, NotificationDto notification, CancellationToken cancellationToken = default);
}

/// <summary>One transport participating in composite notification fan-out.</summary>
public interface INotificationTransport : INotificationPusher;
