using System.Text.Json;
using BuildingBlocks.Contracts.IntegrationEvents;
using Core.Contracts.Readers;
using Notifications.Application.Abstractions;
using Notifications.Contracts.Notifications;
using Notifications.Domain.Aggregates.Notification;
using Operations.Contracts.IntegrationEvents;

namespace Notifications.Application.IntegrationEvents.Handlers;

/// <summary>
/// Cross-module consumer for <see cref="FlightEmployeeInvitedIntegrationEvent"/>. Resolves
/// the invitee's <c>LinkedUserId</c> through Core, writes a notification row, then pushes
/// the live update via <see cref="INotificationPusher"/> so the recipient's portal bell
/// and mobile inbox refresh without polling.
/// </summary>
/// <remarks>
/// Standard inbox dedupe — checks <see cref="InboxMessage"/> by <c>EventId</c> first to
/// avoid double-processing when the outbox processor retries; otherwise writes the
/// inbox row and the notification in the same transaction.
/// </remarks>
public sealed class FlightEmployeeInvitedIntegrationEventHandler(
    INotificationsDbContext db,
    INotificationRepository notifications,
    IEmployeeReader employeeReader,
    INotificationPusher pusher)
    : IIntegrationEventHandler<FlightEmployeeInvitedIntegrationEvent>
{
    public async Task Handle(FlightEmployeeInvitedIntegrationEvent notification, CancellationToken cancellationToken)
    {
        if (await db.IsAlreadyProcessedAsync(notification.EventId, cancellationToken))
            return;

        var recipientUserId = await employeeReader.GetLinkedUserIdByEmployeeIdAsync(
            notification.InviteeEmployeeId, cancellationToken);
        if (recipientUserId is null || recipientUserId == Guid.Empty) return;

        // System-issued invites (AOG self-claim, contract-driven flight creation, the
        // batch-schedule outbox writer, etc.) emit FlightEmployeeInvited with
        // InviterEmployeeId == Guid.Empty. Skip the inviter lookup in that case so the
        // body falls back to the generic "A teammate" copy and we never call GetByIdAsync
        // with an empty id (which used to surface as the EF funcletizer crash on
        // EmployeeId.From). The reader still guards Guid.Empty internally as a defence
        // in depth.
        var inviterName = "A teammate";
        if (notification.InviterEmployeeId != Guid.Empty)
        {
            var inviter = await employeeReader.GetByIdAsync(notification.InviterEmployeeId, cancellationToken);
            if (inviter is not null) inviterName = inviter.FullName;
        }

        var payload = JsonSerializer.Serialize(new
        {
            flightId = notification.FlightId,
            inviterEmployeeId = notification.InviterEmployeeId,
            inviteeEmployeeId = notification.InviteeEmployeeId,
        });

        var build = Notification.Create(
            recipientUserId.Value,
            NotificationKind.EmployeeInvitedToFlight,
            title: "You were invited to a flight",
            body: $"{inviterName} added you to a flight.",
            payloadJson: payload,
            utcNow: DateTime.UtcNow);
        if (build.IsFailure) return;

        notifications.Add(build.Value);
        db.MarkProcessed(notification.EventId, nameof(FlightEmployeeInvitedIntegrationEvent));
        await db.SaveChangesAsync(cancellationToken);

        await pusher.PushAsync(
            recipientUserId.Value,
            new NotificationDto(
                build.Value.Id.Value,
                build.Value.Kind,
                build.Value.Title,
                build.Value.Body,
                build.Value.PayloadJson,
                build.Value.IsRead,
                build.Value.CreatedAt,
                build.Value.ReadAt),
            cancellationToken);
    }
}
