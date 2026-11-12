using System.Text.Json;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Contracts.Messaging;
using Microsoft.EntityFrameworkCore;
using Notifications.Application.Abstractions;
using Notifications.Application.Features;
using Notifications.Domain.Notifications;
using Operations.Contracts;

namespace Notifications.Application.IntegrationEvents;

/// <summary>Routes a committed colleague correction to the original submitting user.</summary>
public sealed class FlightSubmissionUpdatedHandler(
    INotificationsDbContext db,
    INotificationPusher pusher,
    TimeProvider timeProvider) : IIntegrationEventHandler<FlightSubmissionUpdated>
{
    private const string Consumer = "notifications.flight-submission-updated";

    public async Task HandleAsync(FlightSubmissionUpdated integrationEvent, CancellationToken cancellationToken = default)
    {
        if (await db.HasProcessedAsync(integrationEvent.EventId, Consumer, cancellationToken))
        {
            var persisted = await db.Notifications.SingleOrDefaultAsync(n => n.Id == integrationEvent.EventId, cancellationToken);
            if (persisted is not null && persisted.DeliveredAtUtc is null)
            {
                await pusher.PushAsync(persisted.RecipientUserId, NotificationMapper.ToDto(persisted), cancellationToken);
                persisted.MarkDelivered(timeProvider.GetUtcNow());
                await db.SaveChangesAsync(cancellationToken);
            }
            return;
        }

        // CreatedByUserId is the submitting account captured by Operations. A later staff-account
        // relink must not route the original user's submission notification to a different user.
        var recipientUserId = integrationEvent.SubmittedByUserId;
        if (recipientUserId == Guid.Empty || recipientUserId == integrationEvent.UpdatedByUserId)
        {
            db.MarkProcessed(integrationEvent.EventId, Consumer, timeProvider);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var (kind, entryEn, entryAr) = integrationEvent.SubmissionKind switch
        {
            FlightSubmissionKind.Arrival => ("arrival", "arrival submission", "بيانات الوصول التي أرسلتها"),
            FlightSubmissionKind.Departure => ("departure", "departure submission", "بيانات المغادرة التي أرسلتها"),
            FlightSubmissionKind.Service => ("service", "service entry", "سجل الخدمة الذي أنشأته"),
            FlightSubmissionKind.Task => ("task", "task entry", "سجل المهمة الذي أنشأته"),
            _ => throw new InvalidOperationException("Unsupported flight submission kind.")
        };
        if (!Enum.IsDefined(integrationEvent.Action) || integrationEvent.Action == FlightSubmissionAction.Deleted &&
            integrationEvent.SubmissionKind is not (FlightSubmissionKind.Service or FlightSubmissionKind.Task))
            throw new InvalidOperationException("Unsupported flight submission action.");
        var deleted = integrationEvent.Action == FlightSubmissionAction.Deleted;
        var updaterEn = string.IsNullOrWhiteSpace(integrationEvent.UpdatedByDisplayName) ? "A teammate" : integrationEvent.UpdatedByDisplayName;
        var updaterAr = string.IsNullOrWhiteSpace(integrationEvent.UpdatedByDisplayName) ? "أحد زملائك" : integrationEvent.UpdatedByDisplayName;
        var payloadValues = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["flightId"] = integrationEvent.FlightId.ToString(),
            ["flightNumber"] = integrationEvent.FlightNumber,
            ["submissionId"] = integrationEvent.SubmissionId.ToString(),
            ["submissionKind"] = kind
        };
        if (deleted) payloadValues["action"] = "deleted";
        var payload = JsonSerializer.Serialize(payloadValues);
        var created = Notification.Create(integrationEvent.EventId, recipientUserId, NotificationKind.FlightSubmissionUpdated,
            $"Your {entryEn} was {(deleted ? "deleted" : "updated")}",
            $"{updaterEn} {(deleted ? "deleted" : "updated")} your {entryEn} for flight {integrationEvent.FlightNumber}.",
            $"تم {(deleted ? "حذف" : "تحديث")} {entryAr}",
            $"{(deleted ? "حذف" : "حدّث")} {updaterAr} {entryAr} للرحلة {integrationEvent.FlightNumber}.",
            payload, integrationEvent.OccurredOnUtc);
        if (created.IsFailure)
            throw new InvalidOperationException($"Invalid flight-submission notification: {created.Error.Code} {created.Error.Description}");

        db.Notifications.Add(created.Value);
        db.MarkProcessed(integrationEvent.EventId, Consumer, timeProvider);
        await db.SaveChangesAsync(cancellationToken);
        await pusher.PushAsync(recipientUserId, NotificationMapper.ToDto(created.Value), cancellationToken);
        created.Value.MarkDelivered(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }
}
