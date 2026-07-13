using MasterData.Contracts.Readers;
using Microsoft.EntityFrameworkCore;
using Notifications.Application.Abstractions;
using Notifications.Application.IntegrationEvents;
using Notifications.Contracts;
using Notifications.Infrastructure.Persistence;
using Operations.Contracts;
using Shouldly;

namespace Notifications.Application.UnitTests;

public sealed class FlightEmployeeAssignedHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Linked_recipient_gets_one_bilingual_persisted_and_pushed_notification()
    {
        await using var db = NewDb();
        var recipientStaffId = Guid.NewGuid();
        var inviterStaffId = Guid.NewGuid();
        var recipientUserId = Guid.NewGuid();
        var reader = new FakeStaffReader(
            new(recipientStaffId, "Recipient", recipientUserId),
            new(inviterStaffId, "Mona Dispatcher", Guid.NewGuid()));
        var pusher = new CapturingPusher();
        var handler = new FlightEmployeeAssignedHandler(db, reader, pusher, new StaticTimeProvider(Now));
        var flightId = Guid.NewGuid();

        var integrationEvent = Event(flightId, recipientStaffId, inviterStaffId, Guid.NewGuid());
        await handler.HandleAsync(integrationEvent);
        await handler.HandleAsync(integrationEvent);

        var notification = await db.Notifications.SingleAsync();
        notification.Id.ShouldBe(integrationEvent.EventId);
        notification.BodyEn.ShouldContain("Mona Dispatcher");
        notification.BodyAr.ShouldContain("SV204");
        notification.PayloadJson.ShouldContain(flightId.ToString());
        (await db.InboxMessages.CountAsync()).ShouldBe(1);
        pusher.Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Invite_source_uses_the_employee_invited_kind()
    {
        await using var db = NewDb();
        var staffId = Guid.NewGuid();
        var handler = new FlightEmployeeAssignedHandler(
            db,
            new FakeStaffReader(new StaffNotificationRecipient(staffId, "Recipient", Guid.NewGuid())),
            new CapturingPusher(),
            new StaticTimeProvider(Now));

        await handler.HandleAsync(Event(
            Guid.NewGuid(),
            staffId,
            null,
            Guid.NewGuid(),
            FlightAssignmentSource.Invite));

        (await db.Notifications.SingleAsync()).Kind
            .ShouldBe(Notifications.Domain.Notifications.NotificationKind.EmployeeInvitedToFlight);
    }

    [Fact]
    public async Task Unlinked_recipient_is_terminally_skipped()
    {
        await using var db = NewDb();
        var staffId = Guid.NewGuid();
        var handler = new FlightEmployeeAssignedHandler(
            db,
            new FakeStaffReader(new StaffNotificationRecipient(staffId, "No Portal", null)),
            new CapturingPusher(),
            new StaticTimeProvider(Now));

        await handler.HandleAsync(Event(Guid.NewGuid(), staffId, null, Guid.NewGuid()));

        (await db.Notifications.CountAsync()).ShouldBe(0);
        (await db.InboxMessages.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Self_assignment_is_terminally_skipped()
    {
        await using var db = NewDb();
        var staffId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var handler = new FlightEmployeeAssignedHandler(
            db,
            new FakeStaffReader(new StaffNotificationRecipient(staffId, "Self", userId)),
            new CapturingPusher(),
            new StaticTimeProvider(Now));

        await handler.HandleAsync(Event(Guid.NewGuid(), staffId, staffId, userId));

        (await db.Notifications.CountAsync()).ShouldBe(0);
        (await db.InboxMessages.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Persisted_notification_retries_transport_after_transient_failure()
    {
        await using var db = NewDb();
        var staffId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var pusher = new FlakyPusher();
        var handler = new FlightEmployeeAssignedHandler(
            db,
            new FakeStaffReader(new StaffNotificationRecipient(staffId, "Recipient", userId)),
            pusher,
            new StaticTimeProvider(Now));
        var integrationEvent = Event(Guid.NewGuid(), staffId, null, Guid.NewGuid());

        await Should.ThrowAsync<InvalidOperationException>(() => handler.HandleAsync(integrationEvent));
        (await db.Notifications.SingleAsync()).DeliveredAtUtc.ShouldBeNull();

        await handler.HandleAsync(integrationEvent);

        pusher.Attempts.ShouldBe(2);
        (await db.Notifications.SingleAsync()).DeliveredAtUtc.ShouldBe(Now);
        (await db.Notifications.CountAsync()).ShouldBe(1);
    }

    private static FlightEmployeeAssigned Event(
        Guid flightId,
        Guid recipientStaffId,
        Guid? inviterStaffId,
        Guid assignedByUserId,
        FlightAssignmentSource source = FlightAssignmentSource.Roster) =>
        new()
        {
            FlightId = flightId,
            FlightNumber = "SV204",
            StaffMemberId = recipientStaffId,
            AssignedByStaffMemberId = inviterStaffId,
            AssignedByUserId = assignedByUserId,
            Source = source,
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
