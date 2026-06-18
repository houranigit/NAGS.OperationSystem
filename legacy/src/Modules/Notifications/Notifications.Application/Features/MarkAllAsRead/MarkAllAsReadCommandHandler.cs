using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Notifications.Application.Abstractions;
using Notifications.Domain.Aggregates.Notification;

namespace Notifications.Application.Features.MarkAllAsRead;

public sealed class MarkAllAsReadCommandHandler(
    INotificationsDbContext db,
    INotificationRepository notifications)
    : ICommandHandler<MarkAllAsReadCommand>
{
    public async Task<Result> Handle(MarkAllAsReadCommand request, CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty)
            return Error.Validation("User id is required.");

        // Skip archived rows — they're already invisible to the user, no need to write
        // them through the read-bookkeeping.
        var unread = await db.Notifications
            .Where(n => n.RecipientUserId == request.UserId && !n.IsRead && !n.IsArchived)
            .Select(n => n.Id)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var id in unread)
        {
            var notification = await notifications.GetByIdAsync(id, cancellationToken);
            if (notification is null) continue;
            notification.MarkAsRead(now);
            notifications.Update(notification);
        }

        return Result.Success();
    }
}
