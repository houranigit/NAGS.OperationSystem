using Microsoft.Extensions.Logging;
using Notifications.Application.Abstractions;
using Notifications.Contracts;

namespace Notifications.Infrastructure.Push;

/// <summary>Runs every delivery transport independently and preserves the persisted inbox on failures.</summary>
public sealed class CompositeNotificationPusher(
    IEnumerable<INotificationTransport> transports,
    ILogger<CompositeNotificationPusher> logger) : INotificationPusher
{
    public async Task PushAsync(Guid recipientUserId, NotificationDto notification, CancellationToken cancellationToken = default)
    {
        var attempts = await Task.WhenAll(
            transports.Select(transport => PushSafelyAsync(transport, recipientUserId, notification, cancellationToken)));
        var failures = attempts.OfType<Exception>().ToList();
        if (failures.Count > 0)
        {
            // Every transport has already had an independent attempt. Propagating only after fan-out
            // lets the source outbox retry durable delivery without one transport blocking its peers.
            throw new AggregateException("One or more notification transports failed.", failures);
        }
    }

    private async Task<Exception?> PushSafelyAsync(
        INotificationTransport transport,
        Guid recipientUserId,
        NotificationDto notification,
        CancellationToken cancellationToken)
    {
        try
        {
            await transport.PushAsync(recipientUserId, notification, cancellationToken);
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception,
                "Notification transport {Transport} failed for notification {NotificationId} and user {UserId}",
                transport.GetType().Name,
                notification.Id,
                recipientUserId);
            return exception;
        }
    }
}
