using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Notifications.Contracts.Notifications;

namespace Notifications.Application.Features.GetMyInbox;

public sealed record GetMyInboxQuery(
    Guid UserId,
    int Page = 1,
    int PageSize = 20,
    bool UnreadOnly = false) : IQuery<PaginatedResult<NotificationDto>>;
