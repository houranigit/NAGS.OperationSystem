using System.Globalization;
using FirebaseAdmin.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notifications.Application.Abstractions;
using Notifications.Contracts;
using Notifications.Domain.Notifications;
using Notifications.Infrastructure.Persistence;

namespace Notifications.Infrastructure.Push;

/// <summary>Sends high-priority, data-only FCM messages so Android validates account ownership before display.</summary>
public sealed class FcmNotificationPusher(
    FirebaseAppFactory firebaseApps,
    NotificationsDbContext db,
    TimeProvider timeProvider,
    ILogger<FcmNotificationPusher> logger) : INotificationTransport
{
    private const int FcmBatchSize = 500;
    private static readonly TimeSpan DefaultTimeToLive = TimeSpan.FromDays(1);

    public async Task PushAsync(Guid recipientUserId, NotificationDto notification, CancellationToken cancellationToken = default)
    {
        if (ResolveTimeToLive(notification, timeProvider.GetUtcNow()) is null)
            return;

        var app = firebaseApps.GetApp();
        if (app is null)
            return;

        var tokens = await db.DeviceTokens
            .Where(token => token.UserId == recipientUserId && token.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        if (tokens.Count == 0)
            return;

        var messaging = FirebaseMessaging.GetMessaging(app);
        var transientFailures = new List<Exception>();
        foreach (var batch in tokens.Chunk(FcmBatchSize))
        {
            var timeToLive = ResolveTimeToLive(notification, timeProvider.GetUtcNow());
            if (timeToLive is null)
                break;

            var messages = batch
                .Select(token => BuildMessage(token.Token, recipientUserId, notification, timeToLive.Value))
                .ToList();
            var response = await messaging.SendEachAsync(messages, cancellationToken);
            for (var index = 0; index < response.Responses.Count; index++)
            {
                var send = response.Responses[index];
                if (send.IsSuccess)
                    continue;

                var token = batch[index];
                var code = send.Exception?.MessagingErrorCode;
                if (code is MessagingErrorCode.Unregistered)
                {
                    token.Revoke(timeProvider.GetUtcNow());
                }
                else
                {
                    logger.LogWarning(send.Exception,
                        "FCM delivery failed for token hash {TokenHashPrefix}, user {UserId}, code {Code}",
                        token.TokenHash[..12],
                        recipientUserId,
                        code);
                    transientFailures.Add((Exception?)send.Exception ?? new InvalidOperationException(
                        $"FCM rejected notification {notification.Id} with code {code}."));
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        if (transientFailures.Count > 0)
            throw new AggregateException("One or more FCM destinations were not accepted.", transientFailures);
    }

    internal static TimeSpan? ResolveTimeToLive(NotificationDto notification, DateTimeOffset now)
    {
        if (!string.Equals(notification.Kind, NotificationKind.FlightReminder, StringComparison.Ordinal))
            return DefaultTimeToLive;

        if (!notification.Payload.TryGetValue("scheduledArrivalUtc", out var scheduledArrivalText) ||
            !DateTimeOffset.TryParseExact(
                scheduledArrivalText,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var scheduledArrivalUtc))
        {
            // Reminder expiry is safety-sensitive. A malformed snapshot must fail closed instead of
            // falling back to the normal one-day FCM retention period.
            return null;
        }

        var remaining = scheduledArrivalUtc - now;
        if (remaining <= TimeSpan.Zero)
            return null;

        return remaining < DefaultTimeToLive ? remaining : DefaultTimeToLive;
    }

    private static Message BuildMessage(
        string firebaseInstallationId,
        Guid recipientUserId,
        NotificationDto notification,
        TimeSpan timeToLive)
    {
        var data = new Dictionary<string, string>(notification.Payload, StringComparer.Ordinal)
        {
            ["notificationId"] = notification.Id.ToString(),
            ["kind"] = notification.Kind,
            ["recipientUserId"] = recipientUserId.ToString(),
            ["createdAtUtc"] = notification.CreatedAtUtc.ToString("O"),
            ["titleEn"] = notification.TitleEn,
            ["bodyEn"] = notification.BodyEn,
            ["titleAr"] = notification.TitleAr,
            ["bodyAr"] = notification.BodyAr
        };

        return new Message
        {
            Fid = firebaseInstallationId,
            Data = data,
            Android = new AndroidConfig
            {
                Priority = Priority.High,
                TimeToLive = timeToLive
            }
        };
    }
}
