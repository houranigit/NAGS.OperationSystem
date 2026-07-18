using MasterData.Contracts.Readers;
using Microsoft.EntityFrameworkCore;
using Notifications.Application.Abstractions;
using Notifications.Application.IntegrationEvents;
using Notifications.Contracts;
using Notifications.Domain.Notifications;
using Notifications.Infrastructure.Persistence;
using Operations.Contracts;
using Operations.Contracts.Readers;
using Shouldly;

namespace Notifications.Application.UnitTests;

public sealed class FlightReminderDueHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(720, "12 hours")]
    [InlineData(120, "2 hours")]
    [InlineData(30, "30 minutes")]
    public async Task Linked_recipient_gets_one_deep_linked_notification_for_each_supported_milestone(
        int leadTimeMinutes,
        string expectedCopy)
    {
        await using var db = NewDb();
        var staffMemberId = Guid.NewGuid();
        var recipientUserId = Guid.NewGuid();
        var pusher = new CapturingPusher();
        var handler = new FlightReminderDueHandler(
            db,
            new FakeStaffReader(new StaffNotificationRecipient(staffMemberId, "Recipient", recipientUserId)),
            new FakeEligibilityReader(),
            pusher,
            new StaticTimeProvider(Now));
        var integrationEvent = Event(staffMemberId, leadTimeMinutes);

        await handler.HandleAsync(integrationEvent);
        await handler.HandleAsync(integrationEvent);

        var notification = await db.Notifications.SingleAsync();
        notification.Id.ShouldBe(integrationEvent.EventId);
        notification.Kind.ShouldBe(NotificationKind.FlightReminder);
        notification.BodyEn.ShouldContain(expectedCopy);
        notification.PayloadJson.ShouldContain(integrationEvent.FlightId.ToString());
        notification.PayloadJson.ShouldContain("scheduledArrivalUtc");
        notification.DeliveredAtUtc.ShouldBe(Now);
        (await db.InboxMessages.CountAsync()).ShouldBe(1);
        pusher.Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Unlinked_recipient_is_terminally_skipped()
    {
        await using var db = NewDb();
        var staffMemberId = Guid.NewGuid();
        var handler = new FlightReminderDueHandler(
            db,
            new FakeStaffReader(new StaffNotificationRecipient(staffMemberId, "No portal user", null)),
            new FakeEligibilityReader(),
            new CapturingPusher(),
            new StaticTimeProvider(Now));

        await handler.HandleAsync(Event(staffMemberId, 120));

        (await db.Notifications.CountAsync()).ShouldBe(0);
        (await db.InboxMessages.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Persisted_notification_retries_transport_after_transient_failure()
    {
        await using var db = NewDb();
        var staffMemberId = Guid.NewGuid();
        var pusher = new FlakyPusher();
        var handler = new FlightReminderDueHandler(
            db,
            new FakeStaffReader(new StaffNotificationRecipient(staffMemberId, "Recipient", Guid.NewGuid())),
            new FakeEligibilityReader(),
            pusher,
            new StaticTimeProvider(Now));
        var integrationEvent = Event(staffMemberId, 30);

        await Should.ThrowAsync<InvalidOperationException>(() => handler.HandleAsync(integrationEvent));
        (await db.Notifications.SingleAsync()).DeliveredAtUtc.ShouldBeNull();

        await handler.HandleAsync(integrationEvent);

        pusher.Attempts.ShouldBe(2);
        (await db.Notifications.SingleAsync()).DeliveredAtUtc.ShouldBe(Now);
        (await db.Notifications.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Undelivered_notification_is_removed_when_flight_changes_before_transport_retry()
    {
        await using var db = NewDb();
        var staffMemberId = Guid.NewGuid();
        var pusher = new FlakyPusher();
        var eligibility = new FakeEligibilityReader();
        var handler = new FlightReminderDueHandler(
            db,
            new FakeStaffReader(new StaffNotificationRecipient(staffMemberId, "Recipient", Guid.NewGuid())),
            eligibility,
            pusher,
            new StaticTimeProvider(Now));
        var integrationEvent = Event(staffMemberId, 30);

        await Should.ThrowAsync<InvalidOperationException>(() => handler.HandleAsync(integrationEvent));
        eligibility.DefaultResponse = false;

        await handler.HandleAsync(integrationEvent);

        pusher.Attempts.ShouldBe(1);
        (await db.Notifications.CountAsync()).ShouldBe(0);
        (await db.InboxMessages.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Reminder_at_or_after_sta_is_terminally_skipped_without_persisting_or_pushing()
    {
        await using var db = NewDb();
        var staffMemberId = Guid.NewGuid();
        var eligibility = new FakeEligibilityReader();
        var pusher = new CapturingPusher();
        var handler = new FlightReminderDueHandler(
            db,
            new FakeStaffReader(new StaffNotificationRecipient(staffMemberId, "Recipient", Guid.NewGuid())),
            eligibility,
            pusher,
            new StaticTimeProvider(Now));
        var integrationEvent = Event(staffMemberId, 30) with { ScheduledArrivalUtc = Now };

        await handler.HandleAsync(integrationEvent);

        (await db.Notifications.CountAsync()).ShouldBe(0);
        (await db.InboxMessages.CountAsync()).ShouldBe(1);
        pusher.Items.ShouldBeEmpty();
        eligibility.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Reminder_is_terminally_skipped_when_live_flight_snapshot_changed_after_enqueue()
    {
        await using var db = NewDb();
        var staffMemberId = Guid.NewGuid();
        var pusher = new CapturingPusher();
        var handler = new FlightReminderDueHandler(
            db,
            new FakeStaffReader(new StaffNotificationRecipient(staffMemberId, "Recipient", Guid.NewGuid())),
            new FakeEligibilityReader(false),
            pusher,
            new StaticTimeProvider(Now));

        await handler.HandleAsync(Event(staffMemberId, 120));

        (await db.Notifications.CountAsync()).ShouldBe(0);
        (await db.InboxMessages.CountAsync()).ShouldBe(1);
        pusher.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reminder_is_not_pushed_when_flight_changes_during_notification_persistence()
    {
        await using var db = NewDb();
        var staffMemberId = Guid.NewGuid();
        var pusher = new CapturingPusher();
        var eligibility = new FakeEligibilityReader(true, true, false);
        var handler = new FlightReminderDueHandler(
            db,
            new FakeStaffReader(new StaffNotificationRecipient(staffMemberId, "Recipient", Guid.NewGuid())),
            eligibility,
            pusher,
            new StaticTimeProvider(Now));

        await handler.HandleAsync(Event(staffMemberId, 120));

        (await db.Notifications.CountAsync()).ShouldBe(0);
        (await db.InboxMessages.CountAsync()).ShouldBe(1);
        pusher.Items.ShouldBeEmpty();
        eligibility.Calls.ShouldBe(3);
    }

    private static FlightReminderDue Event(Guid staffMemberId, int leadTimeMinutes) =>
        new()
        {
            FlightId = Guid.NewGuid(),
            FlightNumber = "SV204",
            StaffMemberId = staffMemberId,
            ScheduledArrivalUtc = Now.AddMinutes(leadTimeMinutes),
            LeadTimeMinutes = leadTimeMinutes,
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

    private sealed class FakeEligibilityReader(params bool[] responses) : IFlightReminderEligibilityReader
    {
        private readonly Queue<bool> _responses = new(responses);

        public int Calls { get; private set; }
        public bool DefaultResponse { get; set; } = responses.LastOrDefault(true);

        public Task<bool> IsEligibleAsync(
            Guid flightId,
            Guid staffMemberId,
            DateTimeOffset scheduledArrivalUtc,
            DateTimeOffset evaluatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_responses.TryDequeue(out var response) ? response : DefaultResponse);
        }
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
