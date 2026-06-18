using BuildingBlocks.Application.Abstractions.Queries;

namespace Notifications.Application.Features.GetUnreadCount;

public sealed record GetUnreadCountQuery(Guid UserId) : IQuery<int>;
