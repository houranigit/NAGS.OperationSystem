using Notifications.Domain.Devices;
using Notifications.Domain.Notifications;
using Shouldly;

namespace Notifications.Domain.UnitTests;

public sealed class NotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_requires_bilingual_copy_and_preserves_payload()
    {
        var result = Notification.Create(
            Guid.NewGuid(), Guid.NewGuid(), NotificationKind.StaffAssignedToFlight,
            "You were assigned to a flight", "A teammate added you to flight SV100.",
            "تم تعيينك في رحلة", "أضافك أحد زملائك إلى الرحلة SV100.",
            "{\"flightId\":\"abc\"}", Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TitleAr.ShouldBe("تم تعيينك في رحلة");
        result.Value.PayloadJson.ShouldContain("flightId");
        result.Value.IsRead.ShouldBeFalse();
    }

    [Fact]
    public void Read_and_archive_are_idempotent_and_keep_first_timestamp()
    {
        var notification = CreateNotification();

        notification.MarkAsRead(Now.AddMinutes(1));
        notification.MarkAsRead(Now.AddMinutes(2));
        notification.Archive(Now.AddMinutes(3));
        notification.Archive(Now.AddMinutes(4));

        notification.ReadAtUtc.ShouldBe(Now.AddMinutes(1));
        notification.ArchivedAtUtc.ShouldBe(Now.AddMinutes(3));
    }

    [Fact]
    public void Device_registration_refresh_transfers_ownership_and_reactivates_token()
    {
        var firstUser = Guid.NewGuid();
        var secondUser = Guid.NewGuid();
        var device = DeviceToken.Register(firstUser, "fcm-token", DevicePlatform.Android, "install-1", "en", "1.0", Now).Value;

        device.Revoke(Now.AddMinutes(1));
        var refresh = device.Refresh(secondUser, "fcm-token-rotated", DevicePlatform.Android, "install-1", "ar", "1.1", Now.AddMinutes(2));

        refresh.IsSuccess.ShouldBeTrue();
        device.UserId.ShouldBe(secondUser);
        device.IsActive.ShouldBeTrue();
        device.Locale.ShouldBe("ar");
        device.TokenHash.Length.ShouldBe(64);
    }

    private static Notification CreateNotification() =>
        Notification.Create(
            Guid.NewGuid(), Guid.NewGuid(), NotificationKind.StaffAssignedToFlight,
            "Title", "Body", "عنوان", "محتوى", "{}", Now).Value;
}
