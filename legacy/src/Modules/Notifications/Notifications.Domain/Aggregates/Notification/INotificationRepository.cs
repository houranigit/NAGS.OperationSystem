namespace Notifications.Domain.Aggregates.Notification;

public interface INotificationRepository
{
    Task<Notification?> GetByIdAsync(NotificationId id, CancellationToken cancellationToken = default);
    void Add(Notification notification);
    void Update(Notification notification);
}
