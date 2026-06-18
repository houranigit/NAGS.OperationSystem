using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Notifications.Domain.Aggregates.Notification;

namespace Notifications.Application.Features.MarkAsRead;

public sealed class MarkAsReadCommandHandler(INotificationRepository notifications)
    : ICommandHandler<MarkAsReadCommand>
{
    public async Task<Result> Handle(MarkAsReadCommand request, CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty)
            return Error.Validation("User id is required.");
        if (request.NotificationId == Guid.Empty)
            return Error.Validation("Notification id is required.");

        var notification = await notifications.GetByIdAsync(NotificationId.From(request.NotificationId), cancellationToken);
        if (notification is null || notification.RecipientUserId != request.UserId)
            return Error.NotFound("Notification not found.");

        var apply = notification.MarkAsRead(DateTime.UtcNow);
        if (apply.IsFailure)
            return apply;

        notifications.Update(notification);
        return Result.Success();
    }
}
