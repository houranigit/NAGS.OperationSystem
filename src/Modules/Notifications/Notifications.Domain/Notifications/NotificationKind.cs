namespace Notifications.Domain.Notifications;

/// <summary>Stable client-facing kinds. New kinds can be added without a persistence migration.</summary>
public static class NotificationKind
{
    public const string StaffAssignedToFlight = "StaffAssignedToFlight";
    public const string EmployeeInvitedToFlight = "EmployeeInvitedToFlight";
}
