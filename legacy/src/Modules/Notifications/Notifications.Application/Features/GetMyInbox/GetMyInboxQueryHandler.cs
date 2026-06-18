using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Notifications.Application.Abstractions;
using Notifications.Contracts.Notifications;

namespace Notifications.Application.Features.GetMyInbox;

public sealed class GetMyInboxQueryHandler(INotificationsDbContext db)
    : IQueryHandler<GetMyInboxQuery, PaginatedResult<NotificationDto>>
{
    public async Task<Result<PaginatedResult<NotificationDto>>> Handle(
        GetMyInboxQuery request,
        CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty)
            return Error.Validation("User id is required.");

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        // Soft-archived rows never surface on the inbox — see Notification.Archive()
        // and the "Clear all" mobile action that wipes the inbox after read-through.
        var query = db.Notifications
            .Where(n => n.RecipientUserId == request.UserId && !n.IsArchived);
        if (request.UnreadOnly)
            query = query.Where(n => !n.IsRead);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new NotificationDto(
                n.Id.Value,
                n.Kind,
                n.Title,
                n.Body,
                n.PayloadJson,
                n.IsRead,
                n.CreatedAt,
                n.ReadAt))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<NotificationDto>(items, total, page, pageSize);
    }
}
