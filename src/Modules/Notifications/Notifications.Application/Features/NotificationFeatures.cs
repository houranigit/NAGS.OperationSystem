using System.Text.Json;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Notifications.Application.Abstractions;
using Notifications.Contracts;
using Notifications.Domain.Notifications;

namespace Notifications.Application.Features;

public sealed record GetMyNotificationsQuery(int Page = 1, int PageSize = 20, bool UnreadOnly = false, string? Category = null)
    : IQuery<PagedResult<NotificationDto>>;

public sealed class GetMyNotificationsQueryHandler(INotificationsDbContext db, IUserContext user)
    : IQueryHandler<GetMyNotificationsQuery, PagedResult<NotificationDto>>
{
    public async Task<Result<PagedResult<NotificationDto>>> Handle(GetMyNotificationsQuery request, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
            return Error.Unauthorized("Authentication is required.", "Notifications.Unauthenticated");

        if (request.Category is not null && !NotificationCategoryFilter.IsSupported(request.Category))
            return Error.Validation("Category must be Invitations, Reminders, or FlightUpdates.", "Notifications.Category.Invalid");

        var paging = PageRequest.From(request.Page, request.PageSize);
        var query = db.Notifications.AsNoTracking()
            .Where(n => n.RecipientUserId == userId && n.ArchivedAtUtc == null);
        if (request.UnreadOnly)
            query = query.Where(n => n.ReadAtUtc == null);
        query = NotificationCategoryFilter.Apply(query, request.Category);

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

        return await NotificationUnreadCounts.ReadAsync(db, userId, cancellationToken);
    }
}

public sealed record MarkNotificationCategoryReadCommand(string? Category, DateTimeOffset? ThroughCreatedAtUtc)
    : ICommand<NotificationCategoryReadDto>;

public sealed class MarkNotificationCategoryReadCommandHandler(INotificationsDbContext db, IUserContext user, TimeProvider timeProvider)
    : ICommandHandler<MarkNotificationCategoryReadCommand, NotificationCategoryReadDto>
{
    public async Task<Result<NotificationCategoryReadDto>> Handle(MarkNotificationCategoryReadCommand request, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
            return Error.Unauthorized("Authentication is required.", "Notifications.Unauthenticated");
        if (!NotificationCategoryFilter.IsSupported(request.Category))
            return Error.Validation("Category must be Invitations, Reminders, or FlightUpdates.", "Notifications.Category.Invalid");
        return await NotificationReadThrough.ApplyAsync(db, userId, timeProvider, request.ThroughCreatedAtUtc, request.Category, cancellationToken);
    }
}

public sealed record MarkNotificationInboxReadCommand(DateTimeOffset? ThroughCreatedAtUtc) : ICommand<NotificationCategoryReadDto>;

public sealed class MarkNotificationInboxReadCommandHandler(INotificationsDbContext db, IUserContext user, TimeProvider timeProvider)
    : ICommandHandler<MarkNotificationInboxReadCommand, NotificationCategoryReadDto>
{
    public async Task<Result<NotificationCategoryReadDto>> Handle(MarkNotificationInboxReadCommand request, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
            return Error.Unauthorized("Authentication is required.", "Notifications.Unauthenticated");
        return await NotificationReadThrough.ApplyAsync(db, userId, timeProvider, request.ThroughCreatedAtUtc, null, cancellationToken);
    }
}

internal static class NotificationReadThrough
{
    public static async Task<Result<NotificationCategoryReadDto>> ApplyAsync(INotificationsDbContext db, Guid userId,
        TimeProvider timeProvider, DateTimeOffset? throughCreatedAtUtc, string? category, CancellationToken cancellationToken)
    {
        if (throughCreatedAtUtc is not { } cutoff || cutoff == default)
            return Error.Validation("A notification creation-time cutoff is required.", "Notifications.CategoryRead.CutoffRequired");

        var now = timeProvider.GetUtcNow().ToUniversalTime();
        cutoff = cutoff.ToUniversalTime();
        if (cutoff > now)
            return Error.Validation("The notification creation-time cutoff cannot be in the future.", "Notifications.CategoryRead.CutoffInFuture");

        var query = db.Notifications.Where(n => n.RecipientUserId == userId && n.ReadAtUtc == null && n.ArchivedAtUtc == null);
        await NotificationCategoryFilter.Apply(query, category)
            .Where(n => n.CreatedAtUtc <= cutoff)
            .ExecuteUpdateAsync(setters => setters.SetProperty(n => n.ReadAtUtc, now), cancellationToken);
        var unread = await NotificationUnreadCounts.ReadAsync(db, userId, cancellationToken);
        return new NotificationCategoryReadDto(now, unread.Count, unread.Invitations, unread.Reminders, unread.FlightUpdates);
    }
}

internal static class NotificationUnreadCounts
{
    public static async Task<UnreadNotificationCountDto> ReadAsync(
        INotificationsDbContext db, Guid userId, CancellationToken cancellationToken)
    {
        var groups = await db.Notifications
            .Where(n => n.RecipientUserId == userId && n.ReadAtUtc == null && n.ArchivedAtUtc == null)
            .GroupBy(n => n.Kind == NotificationKind.EmployeeInvitedToFlight ? NotificationCategories.Invitations
                : n.Kind == NotificationKind.FlightReminder ? NotificationCategories.Reminders
                : NotificationCategories.FlightUpdates)
            .Select(group => new { Category = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.Category, group => group.Count, cancellationToken);

        var invitations = groups.GetValueOrDefault(NotificationCategories.Invitations);
        var reminders = groups.GetValueOrDefault(NotificationCategories.Reminders);
        var flightUpdates = groups.GetValueOrDefault(NotificationCategories.FlightUpdates);
        return new(invitations + reminders + flightUpdates, invitations, reminders, flightUpdates);
    }
}

internal static class NotificationCategoryFilter
{
    public static bool IsSupported(string? category) =>
        category is NotificationCategories.Invitations or NotificationCategories.Reminders or NotificationCategories.FlightUpdates;

    public static IQueryable<Notification> Apply(IQueryable<Notification> query, string? category) => category switch
    {
        NotificationCategories.Invitations => query.Where(n => n.Kind == NotificationKind.EmployeeInvitedToFlight),
        NotificationCategories.Reminders => query.Where(n => n.Kind == NotificationKind.FlightReminder),
        NotificationCategories.FlightUpdates => query.Where(n => n.Kind != NotificationKind.EmployeeInvitedToFlight
            && n.Kind != NotificationKind.FlightReminder),
        _ => query
    };
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
