using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Notifications.Application.Abstractions;
using Notifications.Domain.Aggregates.Notification;

namespace Notifications.Application.Features.ArchiveAll;

public sealed class ArchiveAllCommandHandler(
    INotificationsDbContext db,
    INotificationRepository notifications)
    : ICommandHandler<ArchiveAllCommand>
{
    public async Task<Result> Handle(ArchiveAllCommand request, CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty)
            return Error.Validation("User id is required.");

        // Mirror MarkAllAsReadCommand: pre-filter by recipient + already-archived to keep
        // the working set small, then call Archive on each aggregate so domain bookkeeping
        // (ArchivedAt timestamp) lands consistently.
        var visible = await db.Notifications
            .Where(n => n.RecipientUserId == request.UserId && !n.IsArchived)
            .Select(n => n.Id)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var id in visible)
        {
            var notification = await notifications.GetByIdAsync(id, cancellationToken);
            if (notification is null) continue;
            notification.Archive(now);
            notifications.Update(notification);
        }

        return Result.Success();
    }
}
