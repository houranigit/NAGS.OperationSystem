using Notifications.Contracts;
using Notifications.Domain.Notifications;
using Notifications.Infrastructure.Push;
using Shouldly;

namespace Notifications.Application.UnitTests;

public sealed class FcmNotificationPusherTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Reminder_ttl_is_capped_at_sta()
    {
        var notification = Notification(
            NotificationKind.FlightReminder,
            new Dictionary<string, string>
            {
                ["scheduledArrivalUtc"] = Now.AddHours(2).ToString("O")
            });

        FcmNotificationPusher.ResolveTimeToLive(notification, Now).ShouldBe(TimeSpan.FromHours(2));
    }

    [Fact]
    public void Reminder_ttl_retains_the_normal_one_day_upper_bound()
    {
        var notification = Notification(
            NotificationKind.FlightReminder,
            new Dictionary<string, string>
            {
                ["scheduledArrivalUtc"] = Now.AddDays(2).ToString("O")
            });

        FcmNotificationPusher.ResolveTimeToLive(notification, Now).ShouldBe(TimeSpan.FromDays(1));
    }

    [Theory]
    [InlineData("2026-07-18T12:00:00.0000000+00:00")]
    [InlineData("not-a-timestamp")]
    public void Expired_or_malformed_reminder_is_not_eligible_for_fcm(string scheduledArrivalUtc)
    {
        var notification = Notification(
            NotificationKind.FlightReminder,
            new Dictionary<string, string> { ["scheduledArrivalUtc"] = scheduledArrivalUtc });

        FcmNotificationPusher.ResolveTimeToLive(notification, Now).ShouldBeNull();
    }

    [Fact]
    public void Other_notification_kinds_keep_the_normal_one_day_ttl()
    {
        var notification = Notification(
            NotificationKind.StaffAssignedToFlight,
            new Dictionary<string, string>
            {
                ["scheduledArrivalUtc"] = Now.AddMinutes(30).ToString("O")
            });

        FcmNotificationPusher.ResolveTimeToLive(notification, Now).ShouldBe(TimeSpan.FromDays(1));
    }

    private static NotificationDto Notification(string kind, IReadOnlyDictionary<string, string> payload) =>
        new(
            Guid.NewGuid(),
            kind,
            "Title",
            "Body",
            "العنوان",
            "النص",
            payload,
            false,
            Now,
            null);
}
