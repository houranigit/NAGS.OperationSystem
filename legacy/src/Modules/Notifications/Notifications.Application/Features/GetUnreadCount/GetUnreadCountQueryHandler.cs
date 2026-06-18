using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Notifications.Application.Abstractions;

namespace Notifications.Application.Features.GetUnreadCount;

public sealed class GetUnreadCountQueryHandler(INotificationsDbContext db)
    : IQueryHandler<GetUnreadCountQuery, int>
{
    public async Task<Result<int>> Handle(GetUnreadCountQuery request, CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty)
            return Error.Validation("User id is required.");

        // Mirror GetMyInbox: archived rows are no longer visible to the user, so they
        // shouldn't bump the bell badge either.
        return await db.Notifications.CountAsync(
            n => n.RecipientUserId == request.UserId && !n.IsRead && !n.IsArchived,
            cancellationToken);
    }
}
