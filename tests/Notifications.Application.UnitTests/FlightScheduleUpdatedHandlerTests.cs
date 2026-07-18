using MasterData.Contracts.Readers;
using Microsoft.EntityFrameworkCore;
using Notifications.Application.Abstractions;
using Notifications.Application.IntegrationEvents;
using Notifications.Contracts;
using Notifications.Domain.Notifications;
using Notifications.Infrastructure.Persistence;
using Operations.Contracts;
using Shouldly;

namespace Notifications.Application.UnitTests;

public sealed class FlightScheduleUpdatedHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Linked_recipient_gets_one_generic_notification_without_a_flight_deep_link()
    {
        await using var db = NewDb();
        var staffMemberId = Guid.NewGuid();
        var recipientUserId = Guid.NewGuid();
        var pusher = new CapturingPusher();
        var handler = new FlightScheduleUpdatedHandler(
            db,
            new FakeStaffReader(new StaffNotificationRecipient(staffMemberId, "Recipient", recipientUserId)),
            pusher,
            new StaticTimeProvider(Now));
        var integrationEvent = Event(staffMemberId, Guid.NewGuid());

        await handler.HandleAsync(integrationEvent);
        await handler.HandleAsync(integrationEvent);

        var notification = await db.Notifications.SingleAsync();
        notification.Id.ShouldBe(integrationEvent.EventId);
        notification.Kind.ShouldBe(NotificationKind.FlightScheduleUpdated);
        notification.TitleEn.ShouldBe("Your schedule was updated");
        notification.TitleAr.ShouldBe("تم تحديث جدولك");
        notification.PayloadJson.ShouldContain("flightCount");
        notification.PayloadJson.ShouldContain("3");
        notification.PayloadJson.ShouldNotContain("flightId");
        notification.DeliveredAtUtc.ShouldBe(Now);
        (await db.InboxMessages.CountAsync()).ShouldBe(1);
        pusher.Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Unlinked_recipient_is_terminally_skipped()
    {
        await using var db = NewDb();
        var staffMemberId = Guid.NewGuid();
        var pusher = new CapturingPusher();
        var handler = new FlightScheduleUpdatedHandler(
            db,
            new FakeStaffReader(new StaffNotificationRecipient(staffMemberId, "No portal user", null)),
            pusher,
            new StaticTimeProvider(Now));

        await handler.HandleAsync(Event(staffMemberId, Guid.NewGuid()));

        (await db.Notifications.CountAsync()).ShouldBe(0);
        (await db.InboxMessages.CountAsync()).ShouldBe(1);
        pusher.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Self_update_is_terminally_skipped()
    {
        await using var db = NewDb();
        var staffMemberId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var pusher = new CapturingPusher();
        var handler = new FlightScheduleUpdatedHandler(
            db,
            new FakeStaffReader(new StaffNotificationRecipient(staffMemberId, "Self", userId)),
            pusher,
            new StaticTimeProvider(Now));

        await handler.HandleAsync(Event(staffMemberId, userId));

        (await db.Notifications.CountAsync()).ShouldBe(0);
        (await db.InboxMessages.CountAsync()).ShouldBe(1);
        pusher.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Persisted_notification_retries_transport_after_transient_failure()
    {
        await using var db = NewDb();
        var staffMemberId = Guid.NewGuid();
        var pusher = new FlakyPusher();
        var handler = new FlightScheduleUpdatedHandler(
            db,
            new FakeStaffReader(new StaffNotificationRecipient(staffMemberId, "Recipient", Guid.NewGuid())),
            pusher,
            new StaticTimeProvider(Now));
        var integrationEvent = Event(staffMemberId, Guid.NewGuid());

        await Should.ThrowAsync<InvalidOperationException>(() => handler.HandleAsync(integrationEvent));
        (await db.Notifications.SingleAsync()).DeliveredAtUtc.ShouldBeNull();

        await handler.HandleAsync(integrationEvent);

        pusher.Attempts.ShouldBe(2);
        (await db.Notifications.SingleAsync()).DeliveredAtUtc.ShouldBe(Now);
        (await db.Notifications.CountAsync()).ShouldBe(1);
        (await db.InboxMessages.CountAsync()).ShouldBe(1);
    }

    private static FlightScheduleUpdated Event(Guid staffMemberId, Guid updatedByUserId) =>
        new()
        {
            StaffMemberId = staffMemberId,
            FlightCount = 3,
            UpdatedByUserId = updatedByUserId,
            OccurredOnUtc = Now
        };

    private static NotificationsDbContext NewDb() =>
        new(new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase($"notifications-{Guid.NewGuid()}")
            .Options);

    private sealed class FakeStaffReader(params StaffNotificationRecipient[] recipients) : IStaffNotificationReader
    {
        public Task<StaffNotificationRecipient?> GetStaffRecipientAsync(Guid staffMemberId, CancellationToken cancellationToken) =>
            Task.FromResult<StaffNotificationRecipient?>(recipients.FirstOrDefault(item => item.StaffMemberId == staffMemberId));
    }

    private sealed class CapturingPusher : INotificationPusher
    {
        public List<NotificationDto> Items { get; } = [];

        public Task PushAsync(Guid recipientUserId, NotificationDto notification, CancellationToken cancellationToken = default)
        {
            Items.Add(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class FlakyPusher : INotificationPusher
    {
        public int Attempts { get; private set; }

        public Task PushAsync(Guid recipientUserId, NotificationDto notification, CancellationToken cancellationToken = default)
        {
            Attempts++;
            return Attempts == 1
                ? Task.FromException(new InvalidOperationException("Transient transport failure."))
                : Task.CompletedTask;
        }
    }

    private sealed class StaticTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
