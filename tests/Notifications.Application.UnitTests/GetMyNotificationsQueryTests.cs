using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Notifications.Application.Features;
using Notifications.Contracts;
using Notifications.Domain.Notifications;
using Notifications.Infrastructure.Persistence;
using Shouldly;

namespace Notifications.Application.UnitTests;

public sealed class GetMyNotificationsQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 16, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(NotificationCategories.Invitations, 2, NotificationKind.EmployeeInvitedToFlight, NotificationKind.EmployeeInvitedToFlight)]
    [InlineData(NotificationCategories.Reminders, 2, NotificationKind.FlightReminder, NotificationKind.FlightReminder)]
    [InlineData(NotificationCategories.FlightUpdates, 3, NotificationKind.StaffAssignedToFlight, NotificationKind.FlightScheduleUpdated)]
    public async Task Category_filter_runs_before_counting_and_paging_and_keeps_recipient_archive_scope(
        string category, long expectedTotal, string firstKind, string secondKind)
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        await SeedAsync(db, userId);
        var handler = new GetMyNotificationsQueryHandler(db, new UserContext(userId));

        var first = await handler.Handle(new GetMyNotificationsQuery(PageSize: 1, Category: category), CancellationToken.None);
        var second = await handler.Handle(new GetMyNotificationsQuery(Page: 2, PageSize: 1, Category: category), CancellationToken.None);
        var third = await handler.Handle(new GetMyNotificationsQuery(Page: 3, PageSize: 1, Category: category), CancellationToken.None);

        first.IsSuccess.ShouldBeTrue();
        first.Value.TotalCount.ShouldBe(expectedTotal);
        first.Value.Items.ShouldHaveSingleItem().Kind.ShouldBe(firstKind);
        second.Value.TotalCount.ShouldBe(expectedTotal);
        second.Value.Items.ShouldHaveSingleItem().Kind.ShouldBe(secondKind);
        third.Value.TotalCount.ShouldBe(expectedTotal);
        if (category == NotificationCategories.FlightUpdates)
            third.Value.Items.ShouldHaveSingleItem().Kind.ShouldBe("LegacyFlightMessage");
        else
            third.Value.Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(NotificationCategories.Invitations, 1)]
    [InlineData(NotificationCategories.Reminders, 1)]
    [InlineData(NotificationCategories.FlightUpdates, 3)]
    public async Task Category_filter_combines_with_unread_only(string category, long expectedTotal)
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        await SeedAsync(db, userId);
        var handler = new GetMyNotificationsQueryHandler(db, new UserContext(userId));

        var result = await handler.Handle(new GetMyNotificationsQuery(UnreadOnly: true, Category: category), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalCount.ShouldBe(expectedTotal);
        result.Value.Items.Count.ShouldBe((int)expectedTotal);
        result.Value.Items.ShouldAllBe(item => !item.IsRead);
    }

    [Fact]
    public async Task Omitting_category_keeps_all_active_user_notifications_and_global_unread_count()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        await SeedAsync(db, userId);
        var user = new UserContext(userId);
        var handler = new GetMyNotificationsQueryHandler(db, user);

        var result = await handler.Handle(new GetMyNotificationsQuery(), CancellationToken.None);
        var unread = await new GetMyUnreadNotificationCountQueryHandler(db, user)
            .Handle(new GetMyUnreadNotificationCountQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalCount.ShouldBe(7);
        result.Value.Items.Count.ShouldBe(7);
        result.Value.Items.Select(item => item.Kind).Distinct().Count().ShouldBe(5);
        result.Value.Items.ShouldContain(item => item.Kind == "LegacyFlightMessage");
        unread.Value.Count.ShouldBe(5);
        unread.Value.Invitations.ShouldBe(1);
        unread.Value.Reminders.ShouldBe(1);
        unread.Value.FlightUpdates.ShouldBe(3);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("invitations")]
    [InlineData("All")]
    [InlineData("FlightUpdate")]
    public async Task Invalid_categories_return_validation_errors(string category)
    {
        await using var db = NewDb();
        var handler = new GetMyNotificationsQueryHandler(db, new UserContext(Guid.NewGuid()));

        var result = await handler.Handle(new GetMyNotificationsQuery(Category: category), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe("Notifications.Category.Invalid");
    }

    [Fact]
    public async Task Missing_user_remains_unauthorized_even_with_a_category()
    {
        await using var db = NewDb();
        var handler = new GetMyNotificationsQueryHandler(db, new UserContext(null));

        var result = await handler.Handle(new GetMyNotificationsQuery(Category: NotificationCategories.Invitations), CancellationToken.None);

        result.Error.Type.ShouldBe(ErrorType.Unauthorized);
    }

    private static async Task SeedAsync(NotificationsDbContext db, Guid userId)
    {
        db.Notifications.AddRange(
            Item(userId, NotificationKind.EmployeeInvitedToFlight, 7),
            Item(userId, NotificationKind.EmployeeInvitedToFlight, 6, read: true),
            Item(userId, NotificationKind.FlightReminder, 5),
            Item(userId, NotificationKind.FlightReminder, 4, read: true),
            Item(userId, NotificationKind.StaffAssignedToFlight, 3),
            Item(userId, NotificationKind.FlightScheduleUpdated, 2),
            Item(userId, "LegacyFlightMessage", 1));
        foreach (var kind in new[] { NotificationKind.EmployeeInvitedToFlight, NotificationKind.FlightReminder, NotificationKind.StaffAssignedToFlight })
        {
            db.Notifications.Add(Item(userId, kind, 20, archived: true));
            db.Notifications.Add(Item(Guid.NewGuid(), kind, 30));
        }
        await db.SaveChangesAsync();
    }

    private static Notification Item(Guid userId, string kind, int minute, bool read = false, bool archived = false)
    {
        var notification = Notification.Create(Guid.NewGuid(), userId, kind, "Notification", "Notification details",
            "إشعار", "تفاصيل الإشعار", "{}", Now.AddMinutes(minute)).Value;
        if (read)
            notification.MarkAsRead(Now.AddHours(1));
        if (archived)
            notification.Archive(Now.AddHours(1));
        return notification;
    }

    private static NotificationsDbContext NewDb() => new(new DbContextOptionsBuilder<NotificationsDbContext>()
        .UseInMemoryDatabase($"notification-categories-{Guid.NewGuid()}").Options);

    private sealed class UserContext(Guid? userId) : IUserContext
    {
        public bool IsAuthenticated => userId.HasValue;
        public Guid? UserId => userId;
        public UserType? UserType => null;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => false;
    }
}
