using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Notifications.Application.Abstractions;
using Notifications.Contracts;
using Notifications.Infrastructure.Push;
using Notifications.Infrastructure.Persistence;
using Notifications.Domain.Preferences;
using Shouldly;

namespace Notifications.Application.UnitTests;

public sealed class CompositeNotificationPusherTests
{
    [Fact]
    public async Task A_failed_transport_does_not_prevent_the_other_transport_attempt()
    {
        await using var db = NewDb();
        var successful = new RecordingTransport();
        var composite = new CompositeNotificationPusher(
            [new FailingTransport(), successful],
            NullLogger<CompositeNotificationPusher>.Instance,
            db);
        var notification = Notification();

        var exception = await Should.ThrowAsync<AggregateException>(
            () => composite.PushAsync(Guid.NewGuid(), notification));

        exception.InnerExceptions.Count.ShouldBe(1);
        successful.NotificationIds.ShouldBe([notification.Id]);
    }

    [Fact]
    public async Task Current_opt_out_suppresses_every_transport_without_removing_inbox_history()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        db.NotificationPreferences.Add(NotificationPreference.Create(userId, NotificationPreferenceKeys.FlightAssignments, false).Value);
        await db.SaveChangesAsync();
        var transport = new RecordingTransport();
        var composite = new CompositeNotificationPusher([transport], NullLogger<CompositeNotificationPusher>.Instance, db);

        await composite.PushAsync(userId, Notification());
        transport.NotificationIds.ShouldBeEmpty();

        await composite.PushAsync(Guid.NewGuid(), Notification());
        transport.NotificationIds.Count.ShouldBe(1);
    }

    private static NotificationsDbContext NewDb() => new(new DbContextOptionsBuilder<NotificationsDbContext>()
        .UseInMemoryDatabase($"notification-delivery-preferences-{Guid.NewGuid()}").Options);

    private static NotificationDto Notification() => new(
        Guid.NewGuid(),
        "StaffAssignedToFlight",
        "You were assigned to a flight",
        "A teammate added you to flight SV204.",
        "تم تعيينك في رحلة",
        "أضافك أحد زملائك إلى الرحلة SV204.",
        new Dictionary<string, string> { ["flightId"] = Guid.NewGuid().ToString() },
        false,
        DateTimeOffset.UtcNow,
        null);

    private sealed class FailingTransport : INotificationTransport
    {
        public Task PushAsync(Guid recipientUserId, NotificationDto notification, CancellationToken cancellationToken = default) =>
            Task.FromException(new InvalidOperationException("Transport unavailable."));
    }

    private sealed class RecordingTransport : INotificationTransport
    {
        public List<Guid> NotificationIds { get; } = [];

        public Task PushAsync(Guid recipientUserId, NotificationDto notification, CancellationToken cancellationToken = default)
        {
            NotificationIds.Add(notification.Id);
            return Task.CompletedTask;
        }
    }
}
