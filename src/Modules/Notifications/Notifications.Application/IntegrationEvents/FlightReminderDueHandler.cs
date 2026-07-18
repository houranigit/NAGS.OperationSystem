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
using Operations.Contracts.Readers;

namespace Notifications.Application.IntegrationEvents;

/// <summary>Turns one durable flight reminder milestone into an inbox entry and push notification.</summary>
public sealed class FlightReminderDueHandler(
    INotificationsDbContext db,
    IStaffNotificationReader staffReader,
    IFlightReminderEligibilityReader eligibilityReader,
    INotificationPusher pusher,
    TimeProvider timeProvider) : IIntegrationEventHandler<FlightReminderDue>
{
    private const string Consumer = "notifications.flight-reminder-due";

    public async Task HandleAsync(FlightReminderDue integrationEvent, CancellationToken cancellationToken = default)
    {
        var alreadyProcessed = await db.HasProcessedAsync(integrationEvent.EventId, Consumer, cancellationToken);
        if (!await IsEligibleAsync(integrationEvent, cancellationToken))
        {
            await CompleteWithoutDeliveryAsync(integrationEvent.EventId, alreadyProcessed, cancellationToken);
            return;
        }

        if (alreadyProcessed)
        {
            var persisted = await db.Notifications
                .FirstOrDefaultAsync(notification => notification.Id == integrationEvent.EventId, cancellationToken);
            if (persisted is not null && persisted.DeliveredAtUtc is null)
            {
                if (!await IsEligibleAsync(integrationEvent, cancellationToken))
                {
                    db.Notifications.Remove(persisted);
                    await db.SaveChangesAsync(cancellationToken);
                    return;
                }

                await pusher.PushAsync(persisted.RecipientUserId, NotificationMapper.ToDto(persisted), cancellationToken);
                persisted.MarkDelivered(timeProvider.GetUtcNow());
                await db.SaveChangesAsync(cancellationToken);
            }
            return;
        }

        var recipient = await staffReader.GetStaffRecipientAsync(integrationEvent.StaffMemberId, cancellationToken);
        if (recipient?.LinkedUserId is not { } recipientUserId)
        {
            db.MarkProcessed(integrationEvent.EventId, Consumer, timeProvider);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var payload = JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["flightId"] = integrationEvent.FlightId.ToString(),
            ["flightNumber"] = integrationEvent.FlightNumber,
            ["scheduledArrivalUtc"] = integrationEvent.ScheduledArrivalUtc.ToString("O", CultureInfo.InvariantCulture),
            ["leadTimeMinutes"] = integrationEvent.LeadTimeMinutes.ToString(CultureInfo.InvariantCulture)
        });
        var (leadTimeEn, leadTimeAr) = ReminderLeadTime(integrationEvent.LeadTimeMinutes);

        var created = Notification.Create(
            integrationEvent.EventId,
            recipientUserId,
            NotificationKind.FlightReminder,
            "Upcoming flight reminder",
            $"Flight {integrationEvent.FlightNumber} is due within {leadTimeEn}.",
            "تذكير برحلة قادمة",
            $"موعد وصول الرحلة {integrationEvent.FlightNumber} خلال {leadTimeAr}.",
            payload,
            integrationEvent.OccurredOnUtc);
        if (created.IsFailure)
            throw new InvalidOperationException($"Invalid flight-reminder notification: {created.Error.Code} {created.Error.Description}");

        // Recipient resolution can involve another module and the flight can change while it is in
        // progress. Revalidate immediately before creating durable inbox state.
        if (!await IsEligibleAsync(integrationEvent, cancellationToken))
        {
            await CompleteWithoutDeliveryAsync(integrationEvent.EventId, alreadyProcessed: false, cancellationToken);
            return;
        }

        db.Notifications.Add(created.Value);
        db.MarkProcessed(integrationEvent.EventId, Consumer, timeProvider);
        await db.SaveChangesAsync(cancellationToken);

        // A cancellation, unassignment, reschedule, or STA expiry after persistence must still
        // suppress every delivery transport and remove the undelivered row from the visible inbox.
        if (!await IsEligibleAsync(integrationEvent, cancellationToken))
        {
            db.Notifications.Remove(created.Value);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        await pusher.PushAsync(recipientUserId, NotificationMapper.ToDto(created.Value), cancellationToken);
        created.Value.MarkDelivered(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> IsEligibleAsync(
        FlightReminderDue integrationEvent,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (now >= integrationEvent.ScheduledArrivalUtc)
            return false;

        return await eligibilityReader.IsEligibleAsync(
            integrationEvent.FlightId,
            integrationEvent.StaffMemberId,
            integrationEvent.ScheduledArrivalUtc,
            now,
            cancellationToken);
    }

    private async Task CompleteWithoutDeliveryAsync(
        Guid eventId,
        bool alreadyProcessed,
        CancellationToken cancellationToken)
    {
        if (!alreadyProcessed)
        {
            db.MarkProcessed(eventId, Consumer, timeProvider);
        }
        else
        {
            var persisted = await db.Notifications
                .FirstOrDefaultAsync(notification => notification.Id == eventId, cancellationToken);
            if (persisted is not null && persisted.DeliveredAtUtc is null)
                db.Notifications.Remove(persisted);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static (string English, string Arabic) ReminderLeadTime(int leadTimeMinutes) => leadTimeMinutes switch
    {
        720 => ("12 hours", "12 ساعة"),
        120 => ("2 hours", "ساعتين"),
        30 => ("30 minutes", "30 دقيقة"),
        _ => ($"{leadTimeMinutes.ToString(CultureInfo.InvariantCulture)} minutes",
            $"{leadTimeMinutes.ToString(CultureInfo.InvariantCulture)} دقيقة")
    };
}
