using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Notifications.Application.Features;
using Notifications.Contracts;
using Notifications.Infrastructure.Persistence;
using Shouldly;

namespace Notifications.Application.UnitTests;

public sealed class MarkNotificationCategoryReadCommandTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 16, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("All")]
    [InlineData("invitations")]
    [InlineData("FlightUpdate")]
    public async Task Requires_an_exact_supported_category(string? category)
    {
        await using var db = NewDb();
        var handler = new MarkNotificationCategoryReadCommandHandler(db, new UserContext(Guid.NewGuid()), new Clock());

        var result = await handler.Handle(new MarkNotificationCategoryReadCommand(category, Now), CancellationToken.None);

        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe("Notifications.Category.Invalid");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Requires_a_supplied_nondefault_creation_time_cutoff(bool supplied)
    {
        await using var db = NewDb();
        var handler = new MarkNotificationCategoryReadCommandHandler(db, new UserContext(Guid.NewGuid()), new Clock());
        DateTimeOffset? cutoff = supplied ? default(DateTimeOffset) : null;

        var result = await handler.Handle(new MarkNotificationCategoryReadCommand(NotificationCategories.Invitations, cutoff), CancellationToken.None);

        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe("Notifications.CategoryRead.CutoffRequired");
    }

    [Fact]
    public async Task Rejects_a_cutoff_in_the_future()
    {
        await using var db = NewDb();
        var handler = new MarkNotificationCategoryReadCommandHandler(db, new UserContext(Guid.NewGuid()), new Clock());

        var result = await handler.Handle(new MarkNotificationCategoryReadCommand(NotificationCategories.Reminders, Now.AddTicks(1)), CancellationToken.None);

        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe("Notifications.CategoryRead.CutoffInFuture");
    }

    [Fact]
    public async Task Requires_an_authenticated_user()
    {
        await using var db = NewDb();
        var handler = new MarkNotificationCategoryReadCommandHandler(db, new UserContext(null), new Clock());

        var result = await handler.Handle(new MarkNotificationCategoryReadCommand(NotificationCategories.FlightUpdates, Now), CancellationToken.None);

        result.Error.Type.ShouldBe(ErrorType.Unauthorized);
        result.Error.Code.ShouldBe("Notifications.Unauthenticated");
    }

    private static NotificationsDbContext NewDb() => new(new DbContextOptionsBuilder<NotificationsDbContext>()
        .UseInMemoryDatabase($"notification-category-read-{Guid.NewGuid()}").Options);

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class UserContext(Guid? userId) : IUserContext
    {
        public bool IsAuthenticated => userId.HasValue;
        public Guid? UserId => userId;
        public UserType? UserType => null;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => false;
    }
}
