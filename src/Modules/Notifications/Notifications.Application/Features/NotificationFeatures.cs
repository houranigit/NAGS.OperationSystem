using System.Text.Json;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Notifications.Application.Abstractions;
using Notifications.Contracts;

namespace Notifications.Application.Features;

public sealed record GetMyNotificationsQuery(int Page = 1, int PageSize = 20, bool UnreadOnly = false)
    : IQuery<PagedResult<NotificationDto>>;

public sealed class GetMyNotificationsQueryHandler(INotificationsDbContext db, IUserContext user)
    : IQueryHandler<GetMyNotificationsQuery, PagedResult<NotificationDto>>
{
    public async Task<Result<PagedResult<NotificationDto>>> Handle(GetMyNotificationsQuery request, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
            return Error.Unauthorized("Authentication is required.", "Notifications.Unauthenticated");

        var paging = PageRequest.From(request.Page, request.PageSize);
        var query = db.Notifications.AsNoTracking()
            .Where(n => n.RecipientUserId == userId && n.ArchivedAtUtc == null);
        if (request.UnreadOnly)
            query = query.Where(n => n.ReadAtUtc == null);

        var total = await query.LongCountAsync(cancellationToken);
        if (paging.IsOutOfRange(total))
            return paging.Empty<NotificationDto>(total);
        var rows = await query
            .OrderByDescending(n => n.CreatedAtUtc)
            .ThenByDescending(n => n.Id)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .ToListAsync(cancellationToken);

        return paging.ToResult(rows.Select(NotificationMapper.ToDto).ToList(), total);
    }
}

public sealed record GetMyUnreadNotificationCountQuery : IQuery<UnreadNotificationCountDto>;

public sealed class GetMyUnreadNotificationCountQueryHandler(INotificationsDbContext db, IUserContext user)
    : IQueryHandler<GetMyUnreadNotificationCountQuery, UnreadNotificationCountDto>
{
    public async Task<Result<UnreadNotificationCountDto>> Handle(GetMyUnreadNotificationCountQuery request, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
            return Error.Unauthorized("Authentication is required.", "Notifications.Unauthenticated");

        var count = await db.Notifications.CountAsync(
            n => n.RecipientUserId == userId && n.ReadAtUtc == null && n.ArchivedAtUtc == null,
            cancellationToken);
        return new UnreadNotificationCountDto(count);
    }
}

public sealed record MarkNotificationReadCommand(Guid NotificationId) : ICommand;

public sealed class MarkNotificationReadCommandHandler(INotificationsDbContext db, IUserContext user, TimeProvider timeProvider)
    : ICommandHandler<MarkNotificationReadCommand>
{
    public async Task<Result> Handle(MarkNotificationReadCommand request, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
            return Error.Unauthorized("Authentication is required.", "Notifications.Unauthenticated");

        var notification = await db.Notifications.FirstOrDefaultAsync(
            n => n.Id == request.NotificationId && n.RecipientUserId == userId && n.ArchivedAtUtc == null,
            cancellationToken);
        if (notification is null)
            return Error.NotFound("Notification not found.", "Notifications.Notification.NotFound");

        notification.MarkAsRead(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record MarkAllNotificationsReadCommand : ICommand;

public sealed class MarkAllNotificationsReadCommandHandler(INotificationsDbContext db, IUserContext user, TimeProvider timeProvider)
    : ICommandHandler<MarkAllNotificationsReadCommand>
{
    public async Task<Result> Handle(MarkAllNotificationsReadCommand request, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
            return Error.Unauthorized("Authentication is required.", "Notifications.Unauthenticated");

        var now = timeProvider.GetUtcNow();
        await db.Notifications
            .Where(n => n.RecipientUserId == userId && n.ReadAtUtc == null && n.ArchivedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(n => n.ReadAtUtc, now), cancellationToken);
        return Result.Success();
    }
}

public sealed record ArchiveNotificationCommand(Guid NotificationId) : ICommand;

public sealed class ArchiveNotificationCommandHandler(INotificationsDbContext db, IUserContext user, TimeProvider timeProvider)
    : ICommandHandler<ArchiveNotificationCommand>
{
    public async Task<Result> Handle(ArchiveNotificationCommand request, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
            return Error.Unauthorized("Authentication is required.", "Notifications.Unauthenticated");

        var notification = await db.Notifications.FirstOrDefaultAsync(
            n => n.Id == request.NotificationId && n.RecipientUserId == userId && n.ArchivedAtUtc == null,
            cancellationToken);
        if (notification is null)
            return Error.NotFound("Notification not found.", "Notifications.Notification.NotFound");

        notification.Archive(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record ArchiveAllNotificationsCommand : ICommand;

public sealed class ArchiveAllNotificationsCommandHandler(INotificationsDbContext db, IUserContext user, TimeProvider timeProvider)
    : ICommandHandler<ArchiveAllNotificationsCommand>
{
    public async Task<Result> Handle(ArchiveAllNotificationsCommand request, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
            return Error.Unauthorized("Authentication is required.", "Notifications.Unauthenticated");

        var now = timeProvider.GetUtcNow();
        await db.Notifications
            .Where(n => n.RecipientUserId == userId && n.ArchivedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(n => n.ArchivedAtUtc, now), cancellationToken);
        return Result.Success();
    }
}

public static class NotificationMapper
{
    public static NotificationDto ToDto(Domain.Notifications.Notification notification) => new(
        notification.Id,
        notification.Kind,
        notification.TitleEn,
        notification.BodyEn,
        notification.TitleAr,
        notification.BodyAr,
        DeserializePayload(notification.PayloadJson),
        notification.IsRead,
        notification.CreatedAtUtc,
        notification.ReadAtUtc);

    private static IReadOnlyDictionary<string, string> DeserializePayload(string payloadJson)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(payloadJson)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }
}
