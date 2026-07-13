using FirebaseAdmin.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notifications.Application.Abstractions;
using Notifications.Contracts;
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

    public async Task PushAsync(Guid recipientUserId, NotificationDto notification, CancellationToken cancellationToken = default)
    {
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
            var messages = batch.Select(token => BuildMessage(token.Token, recipientUserId, notification)).ToList();
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

    private static Message BuildMessage(string firebaseInstallationId, Guid recipientUserId, NotificationDto notification)
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
                TimeToLive = TimeSpan.FromDays(1)
            }
        };
    }
}
