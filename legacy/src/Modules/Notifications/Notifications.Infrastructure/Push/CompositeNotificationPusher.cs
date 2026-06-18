using Notifications.Application.Abstractions;
using Notifications.Contracts.Notifications;

namespace Notifications.Infrastructure.Push;

/// <summary>
/// Fans a single notification out to every registered <see cref="IInnerNotificationPusher"/>
/// implementation in parallel. Used to combine the SignalR (foreground/live) and FCM
/// (closed/background) transports so the same handler call reaches both audiences.
/// Pushers run independently — a failure in one does not abort the others.
/// </summary>
public sealed class CompositeNotificationPusher(IEnumerable<IInnerNotificationPusher> pushers)
    : INotificationPusher
{
    public Task PushAsync(Guid recipientUserId, NotificationDto notification, CancellationToken cancellationToken = default)
    {
        var tasks = pushers.Select(p => SafeAsync(p, recipientUserId, notification, cancellationToken));
        return Task.WhenAll(tasks);
    }

    private static async Task SafeAsync(
        IInnerNotificationPusher inner,
        Guid recipientUserId,
        NotificationDto notification,
        CancellationToken cancellationToken)
    {
        try
        {
            await inner.PushAsync(recipientUserId, notification, cancellationToken);
        }
        catch
        {
            // Inner pushers already log their own failures; we suppress here so a misbehaving
            // transport doesn't stop the others from delivering.
        }
    }
}
