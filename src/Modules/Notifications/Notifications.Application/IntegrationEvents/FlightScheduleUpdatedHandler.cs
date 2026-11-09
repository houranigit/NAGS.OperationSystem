using System.Globalization;
using System.Text.Json;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Contracts.Messaging;
using MasterData.Contracts.Readers;
using Microsoft.EntityFrameworkCore;
using Notifications.Application.Abstractions;
using Notifications.Application.Features;
using Notifications.Domain.Notifications;
using Operations.Contracts;

namespace Notifications.Application.IntegrationEvents;

/// <summary>Creates one generic schedule notification for one recipient of a bulk scheduling action.</summary>
public sealed class FlightScheduleUpdatedHandler(
    INotificationsDbContext db,
    IStaffNotificationReader staffReader,
    INotificationPusher pusher,
    TimeProvider timeProvider) : IIntegrationEventHandler<FlightScheduleUpdated>
{
    private const string Consumer = "notifications.flight-schedule-updated";

    public async Task HandleAsync(FlightScheduleUpdated integrationEvent, CancellationToken cancellationToken = default)
    {
        if (await db.HasProcessedAsync(integrationEvent.EventId, Consumer, cancellationToken))
        {
            // Persistence is authoritative. If the first delivery committed the inbox row but its
            // transport failed, retry fan-out with the same stable notification id.
            var persisted = await db.Notifications
                .FirstOrDefaultAsync(notification => notification.Id == integrationEvent.EventId, cancellationToken);
            if (persisted is not null && persisted.DeliveredAtUtc is null)
            {
                await pusher.PushAsync(persisted.RecipientUserId, NotificationMapper.ToDto(persisted), cancellationToken);
                persisted.MarkDelivered(timeProvider.GetUtcNow());
                await db.SaveChangesAsync(cancellationToken);
            }
            return;
        }

        var recipient = await staffReader.GetStaffRecipientAsync(integrationEvent.StaffMemberId, cancellationToken);
        if (recipient?.LinkedUserId is not { } recipientUserId ||
            recipientUserId == integrationEvent.UpdatedByUserId)
        {
            db.MarkProcessed(integrationEvent.EventId, Consumer, timeProvider);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var payload = JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["flightCount"] = integrationEvent.FlightCount.ToString(CultureInfo.InvariantCulture)
        });

        var created = Notification.Create(
            integrationEvent.EventId,
            recipientUserId,
            NotificationKind.FlightScheduleUpdated,
            "Your schedule was updated",
            "New flights were added to your schedule.",
            "تم تحديث جدولك",
            "تمت إضافة رحلات جديدة إلى جدولك.",
            payload,
            integrationEvent.OccurredOnUtc);
        if (created.IsFailure)
            throw new InvalidOperationException($"Invalid flight-schedule notification: {created.Error.Code} {created.Error.Description}");

        db.Notifications.Add(created.Value);
        db.MarkProcessed(integrationEvent.EventId, Consumer, timeProvider);
        await db.SaveChangesAsync(cancellationToken);

        await pusher.PushAsync(recipientUserId, NotificationMapper.ToDto(created.Value), cancellationToken);
        created.Value.MarkDelivered(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }
}
