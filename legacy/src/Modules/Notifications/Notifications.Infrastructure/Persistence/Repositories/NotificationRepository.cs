using Microsoft.EntityFrameworkCore;
using Notifications.Domain.Aggregates.Notification;

namespace Notifications.Infrastructure.Persistence.Repositories;

internal sealed class NotificationRepository(NotificationsDbContext db) : INotificationRepository
{
    public async Task<Notification?> GetByIdAsync(NotificationId id, CancellationToken cancellationToken = default) =>
        await db.NotificationItems.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

    public void Add(Notification notification) => db.NotificationItems.Add(notification);

    public void Update(Notification notification) => db.NotificationItems.Update(notification);
}
