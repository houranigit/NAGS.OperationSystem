using FirebaseAdmin.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notifications.Application.Abstractions;
using Notifications.Contracts.Notifications;
using Notifications.Domain.Aggregates.DeviceToken;
using Notifications.Infrastructure.Persistence;

namespace Notifications.Infrastructure.Push;

/// <summary>
/// Sends notifications to every active device token registered for the recipient. The
/// implementation is best-effort — failures don't bubble up to the caller (we log instead)
/// because the SignalR transport runs alongside this one and the in-app inbox is the
/// source of truth. When FCM is disabled the factory returns null and we no-op silently.
/// </summary>
public sealed class FcmNotificationPusher(
    FirebaseAppFactory factory,
    IDeviceTokenRepository tokens,
    NotificationsDbContext db,
    ILogger<FcmNotificationPusher> logger) : IInnerNotificationPusher
{
    public async Task PushAsync(
        Guid recipientUserId,
        NotificationDto notification,
        CancellationToken cancellationToken = default)
    {
        var app = factory.GetApp();
        if (app is null) return;

        var active = await tokens.GetActiveByUserAsync(recipientUserId, cancellationToken);
        if (active.Count == 0) return;

        var messaging = FirebaseMessaging.GetMessaging(app);

        // Build per-token messages — MulticastMessage caps at 500 tokens, but slicing also
        // gives us per-token error attribution from BatchResponse.Responses[i].Exception.
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["id"] = notification.Id.ToString(),
            ["kind"] = notification.Kind,
            ["payloadJson"] = notification.PayloadJson ?? "{}",
            ["createdAt"] = notification.CreatedAt.ToString("o"),
        };

        var tokenList = active.Select(t => t.Token).ToList();
        var multicast = new MulticastMessage
        {
            Tokens = tokenList,
            Notification = new FirebaseAdmin.Messaging.Notification
            {
                Title = notification.Title,
                Body = notification.Body,
            },
            Data = data,
            // Android: high priority so closed-app delivery uses the data path our
            // FcmService picks up; Compose UI shows a heads-up banner via SystemNotifier.
            Android = new AndroidConfig
            {
                Priority = Priority.High,
                Notification = new AndroidNotification
                {
                    Title = notification.Title,
                    Body = notification.Body,
                    DefaultSound = true,
                },
            },
        };

        try
        {
            var response = await messaging.SendEachForMulticastAsync(multicast, cancellationToken);
            if (response.FailureCount == 0) return;

            // Revoke tokens FCM reports as Unregistered or InvalidArgument — they will never
            // succeed again and we should stop wasting fan-out cycles on them.
            for (var i = 0; i < response.Responses.Count; i++)
            {
                var item = response.Responses[i];
                if (item.IsSuccess) continue;

                var token = tokenList[i];
                var error = item.Exception?.MessagingErrorCode;
                if (error is MessagingErrorCode.Unregistered or MessagingErrorCode.InvalidArgument)
                {
                    var existing = await db.DeviceTokenItems
                        .FirstOrDefaultAsync(t => t.UserId == recipientUserId && t.Token == token, cancellationToken);
                    if (existing is not null)
                    {
                        existing.Revoke(DateTime.UtcNow);
                        db.DeviceTokenItems.Update(existing);
                    }
                }
                else
                {
                    logger.LogWarning(item.Exception,
                        "FCM send failed for user {UserId} token {TokenPrefix}: {Error}",
                        recipientUserId,
                        token.Length > 12 ? token[..12] : token,
                        error);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Push is best-effort — the SignalR transport alongside this one and the
            // in-app inbox row already persisted ensure the user never misses a message
            // even if FCM is unavailable.
            logger.LogError(ex, "FCM send failed for user {UserId}", recipientUserId);
        }
    }
}
