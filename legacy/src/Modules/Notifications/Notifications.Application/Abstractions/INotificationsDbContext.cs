using BuildingBlocks.Application.Abstractions;
using DeviceTokenRoot = Notifications.Domain.Aggregates.DeviceToken.DeviceToken;
using NotificationRoot = Notifications.Domain.Aggregates.Notification.Notification;

namespace Notifications.Application.Abstractions;

public interface INotificationsDbContext : IUnitOfWork
{
    IQueryable<NotificationRoot> Notifications { get; }

    /// <summary>
    /// Read-only projection of the device-token table. Application handlers query this
    /// (e.g. FcmNotificationPusher fetching all active tokens for a user) but mutate via
    /// <see cref="Domain.Aggregates.DeviceToken.IDeviceTokenRepository"/>.
    /// </summary>
    IQueryable<DeviceTokenRoot> DeviceTokens { get; }

    /// <summary>
    /// Returns <c>true</c> if an integration-event with the given <paramref name="eventId"/>
    /// has already been processed (recorded in the inbox table). Used by the
    /// Notifications integration-event handlers to dedupe re-deliveries from the outbox
    /// processor.
    /// </summary>
    Task<bool> IsAlreadyProcessedAsync(Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that an integration-event has been processed, so subsequent deliveries
    /// of the same <paramref name="eventId"/> are skipped via <see cref="IsAlreadyProcessedAsync"/>.
    /// Persisted on the next <see cref="IUnitOfWork.SaveChangesAsync"/>.
    /// </summary>
    void MarkProcessed(Guid eventId, string eventType);
}
