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

/// <summary>Creates one persisted inbox entry for each durable flight-assignment event.</summary>
public sealed class FlightEmployeeAssignedHandler(
    INotificationsDbContext db,
    IStaffNotificationReader staffReader,
    INotificationPusher pusher,
    TimeProvider timeProvider) : IIntegrationEventHandler<FlightEmployeeAssigned>
{
    private const string Consumer = "notifications.flight-staff-assignment";

    public async Task HandleAsync(FlightEmployeeAssigned integrationEvent, CancellationToken cancellationToken = default)
    {
        if (await db.HasProcessedAsync(integrationEvent.EventId, Consumer, cancellationToken))
        {
            // A prior attempt may have committed the authoritative inbox row and then failed in a
            // transport. Re-delivery from the Operations outbox retries fan-out using the same
            // stable notification id; portal/mobile clients dedupe that id.
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

        // A staff record without a linked portal user has no portal inbox or authenticated mobile
        // destination. Self-assignment is intentionally silent. Both cases are terminal decisions
        // for this event and are recorded in the inbox so at-least-once delivery cannot revisit them.
        if (recipient?.LinkedUserId is not { } recipientUserId ||
            recipientUserId == integrationEvent.AssignedByUserId)
        {
            db.MarkProcessed(integrationEvent.EventId, Consumer, timeProvider);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        const string fallbackNameEn = "A teammate";
        const string fallbackNameAr = "أحد زملائك";
        var inviterName = integrationEvent.AssignedByDisplayName;
        if (string.IsNullOrWhiteSpace(inviterName) &&
            integrationEvent.AssignedByStaffMemberId is { } staffMemberId && staffMemberId != Guid.Empty)
        {
            inviterName = (await staffReader.GetStaffRecipientAsync(staffMemberId, cancellationToken))?.FullName;
        }
        var inviterNameEn = string.IsNullOrWhiteSpace(inviterName) ? fallbackNameEn : inviterName;
        var inviterNameAr = string.IsNullOrWhiteSpace(inviterName) ? fallbackNameAr : inviterName;

        var payload = JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["flightId"] = integrationEvent.FlightId.ToString(),
            ["flightNumber"] = integrationEvent.FlightNumber
        });

        var created = Notification.Create(
            integrationEvent.EventId,
            recipientUserId,
            integrationEvent.Source == FlightAssignmentSource.Invite
                ? NotificationKind.EmployeeInvitedToFlight
                : NotificationKind.StaffAssignedToFlight,
            "You were assigned to a flight",
            $"{inviterNameEn} added you to flight {integrationEvent.FlightNumber}.",
            "تم تعيينك في رحلة",
            $"أضافك {inviterNameAr} إلى الرحلة {integrationEvent.FlightNumber}.",
            payload,
            integrationEvent.OccurredOnUtc);
        if (created.IsFailure)
            throw new InvalidOperationException($"Invalid flight-assignment notification: {created.Error.Code} {created.Error.Description}");

        db.Notifications.Add(created.Value);
        db.MarkProcessed(integrationEvent.EventId, Consumer, timeProvider);
        await db.SaveChangesAsync(cancellationToken);

        // Persistence is authoritative. Transport fan-out is best-effort and isolated by the
        // composite pusher, so a SignalR or FCM outage cannot lose the inbox entry or block peers.
        await pusher.PushAsync(recipientUserId, NotificationMapper.ToDto(created.Value), cancellationToken);
        created.Value.MarkDelivered(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }
}
