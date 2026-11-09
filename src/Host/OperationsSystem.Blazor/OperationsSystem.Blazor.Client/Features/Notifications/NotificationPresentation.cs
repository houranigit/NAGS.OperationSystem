using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Localization;
using OperationsSystem.Blazor.Client.State;

namespace OperationsSystem.Blazor.Client.Features.Notifications;

public static class NotificationPresentation
{
    public static string Category(NotificationDto notification) => notification.Kind switch
    {
        "EmployeeInvitedToFlight" => NotificationCategories.Invitations,
        "FlightReminder" => NotificationCategories.Reminders,
        _ => NotificationCategories.FlightUpdates
    };

    public static string CategoryLabel(string category) => category switch
    {
        NotificationCategories.Invitations => UiStrings.Notifications.Invitations,
        NotificationCategories.Reminders => UiStrings.Notifications.Reminders,
        _ => UiStrings.Notifications.FlightUpdates
    };

    public static string KindLabel(NotificationDto notification) => notification.Kind switch
    {
        "StaffAssignedToFlight" => UiStrings.Notifications.FlightAssignment,
        "FlightScheduleUpdated" => UiStrings.Notifications.ScheduleUpdated,
        _ => CategoryLabel(Category(notification))
    };

    public static string Icon(NotificationDto notification) => notification.Kind switch
    {
        "EmployeeInvitedToFlight" => "person_add",
        "FlightReminder" => "alarm",
        "FlightScheduleUpdated" => "event_repeat",
        _ => "flight_takeoff"
    };

    public static string Title(NotificationDto notification, LocaleState locale) =>
        locale.IsRightToLeft && !string.IsNullOrWhiteSpace(notification.TitleAr)
            ? notification.TitleAr
            : notification.TitleEn;

    public static string Body(NotificationDto notification, LocaleState locale) =>
        locale.IsRightToLeft && !string.IsNullOrWhiteSpace(notification.BodyAr)
            ? notification.BodyAr
            : notification.BodyEn;

    public static bool TryGetFlightId(NotificationDto notification, out Guid flightId)
    {
        foreach (var item in notification.Payload)
        {
            if (string.Equals(item.Key, "flightId", StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParse(item.Value, out flightId) &&
                flightId != Guid.Empty)
            {
                return true;
            }
        }

        flightId = Guid.Empty;
        return false;
    }

    public static string? DeepLink(NotificationDto notification) =>
        TryGetFlightId(notification, out var flightId)
            ? $"/operations/flights?search={flightId}"
            : null;

    public static string InboxLink(NotificationDto notification) =>
        $"/notifications?category={Category(notification)}";
}
