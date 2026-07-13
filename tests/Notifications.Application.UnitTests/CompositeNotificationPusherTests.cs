using Microsoft.Extensions.Logging.Abstractions;
using Notifications.Application.Abstractions;
using Notifications.Contracts;
using Notifications.Infrastructure.Push;
using Shouldly;

namespace Notifications.Application.UnitTests;

public sealed class CompositeNotificationPusherTests
{
    [Fact]
    public async Task A_failed_transport_does_not_prevent_the_other_transport_attempt()
    {
        var successful = new RecordingTransport();
        var composite = new CompositeNotificationPusher(
            [new FailingTransport(), successful],
            NullLogger<CompositeNotificationPusher>.Instance);
        var notification = Notification();

        var exception = await Should.ThrowAsync<AggregateException>(
            () => composite.PushAsync(Guid.NewGuid(), notification));

        exception.InnerExceptions.Count.ShouldBe(1);
        successful.NotificationIds.ShouldBe([notification.Id]);
    }

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
