namespace Notifications.Domain.Aggregates.Notification;

/// <summary>
/// Closed set of notification "types" understood by clients.  Stored as a string column so
/// new kinds can be added without an EF migration; clients deep-link based on the string
/// value (e.g. <c>EmployeeInvitedToFlight</c> opens that flight).
/// </summary>
public static class NotificationKind
{
    public const string EmployeeInvitedToFlight = "EmployeeInvitedToFlight";
    public const string WorkOrderApproved = "WorkOrderApproved";
    public const string WorkOrderRejected = "WorkOrderRejected";
    public const string WorkOrderRevoked = "WorkOrderRevoked";
}
