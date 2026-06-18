namespace Notifications.Domain.Aggregates.DeviceToken;

/// <summary>
/// Mobile platform identifier for a registered FCM/APNs device token. Stored as int so the
/// table can grow with new clients (e.g. Web push) without an enum-versioning migration.
/// </summary>
public enum DevicePlatform
{
    Android = 0,
    Ios = 1,
    Web = 2,
}
