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

/// <summary>Delivers share information without changing assignment or operational access.</summary>
public sealed class FlightSharedHandler(
    INotificationsDbContext db,
    IStaffNotificationReader staffReader,
    INotificationPusher pusher,
    TimeProvider timeProvider) : IIntegrationEventHandler<FlightShared>
{
    private const string Consumer = "notifications.flight-shared";

    public async Task HandleAsync(FlightShared integrationEvent, CancellationToken cancellationToken = default)
    {
        if (await db.HasProcessedAsync(integrationEvent.EventId, Consumer, cancellationToken))
        {
            var persisted = await db.Notifications.SingleOrDefaultAsync(
                item => item.Id == integrationEvent.EventId, cancellationToken);
            if (persisted is not null && persisted.DeliveredAtUtc is null &&
                await NotificationPreferenceFilter.IsEnabledAsync(db, persisted.RecipientUserId, persisted.Kind, cancellationToken))
            {
                await pusher.PushAsync(persisted.RecipientUserId, NotificationMapper.ToDto(persisted), cancellationToken);
                persisted.MarkDelivered(timeProvider.GetUtcNow());
                await db.SaveChangesAsync(cancellationToken);
            }
            return;
        }

        var recipient = await staffReader.GetStaffRecipientAsync(integrationEvent.StaffMemberId, cancellationToken);
        var kind = integrationEvent.IsStationWide
            ? NotificationKind.FlightSharedWithStation : NotificationKind.FlightSharedWithEmployee;
        if (recipient?.LinkedUserId is not { } recipientUserId || recipientUserId == integrationEvent.SharedByUserId ||
            !await NotificationPreferenceFilter.IsEnabledAsync(db, recipientUserId, kind, cancellationToken))
        {
            db.MarkProcessed(integrationEvent.EventId, Consumer, timeProvider);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var authorEn = string.IsNullOrWhiteSpace(integrationEvent.SharedByDisplayName) ? "A teammate" : integrationEvent.SharedByDisplayName;
        var authorAr = string.IsNullOrWhiteSpace(integrationEvent.SharedByDisplayName) ? "أحد زملائك" : integrationEvent.SharedByDisplayName;
        var payload = JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["flightId"] = integrationEvent.FlightId.ToString(),
            ["flightNumber"] = integrationEvent.FlightNumber,
            ["shareId"] = integrationEvent.ShareId.ToString(),
            ["stationId"] = integrationEvent.StationId.ToString()
        });
        var created = Notification.Create(integrationEvent.EventId, recipientUserId, kind,
            integrationEvent.IsStationWide ? "A flight was shared with your station" : "A flight was shared with you",
            $"{authorEn} shared flight {integrationEvent.FlightNumber} {(integrationEvent.IsStationWide ? "with your station" : "with you")}.",
            integrationEvent.IsStationWide ? "تمت مشاركة رحلة مع محطتك" : "تمت مشاركة رحلة معك",
            $"شارك {authorAr} الرحلة {integrationEvent.FlightNumber} {(integrationEvent.IsStationWide ? "مع محطتك" : "معك")}.",
            payload, integrationEvent.OccurredOnUtc);
        if (created.IsFailure)
            throw new InvalidOperationException($"Invalid flight-share notification: {created.Error.Code} {created.Error.Description}");

        db.Notifications.Add(created.Value);
        db.MarkProcessed(integrationEvent.EventId, Consumer, timeProvider);
        await db.SaveChangesAsync(cancellationToken);

        await pusher.PushAsync(recipientUserId, NotificationMapper.ToDto(created.Value), cancellationToken);
        created.Value.MarkDelivered(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }
}
