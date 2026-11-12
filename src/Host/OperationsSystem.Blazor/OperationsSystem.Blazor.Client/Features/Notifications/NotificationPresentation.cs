using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Localization;
using OperationsSystem.Blazor.Client.State;
using System.Globalization;

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
        "FlightScheduleUpdated" or "FlightSubmissionUpdated" => "event_repeat",
        _ => "flight_takeoff"
    };

    public static string ToneClass(NotificationDto notification) => notification.Kind switch
    {
        "FlightReminder" => "notification-tone--reminder",
        "EmployeeInvitedToFlight" => "notification-tone--invitation",
        "FlightScheduleUpdated" or "FlightSubmissionUpdated" => "notification-tone--info",
        _ => "notification-tone--assignment"
    };

    public static string Title(NotificationDto notification, LocaleState locale) => notification.TitleEn;

    public static string Body(NotificationDto notification, LocaleState locale) => notification.BodyEn;

    public static string RelativeAge(DateTimeOffset createdAtUtc, DateTimeOffset now)
    {
        var elapsed = now - createdAtUtc;
        if (elapsed < TimeSpan.FromMinutes(1)) return "now";
        if (elapsed < TimeSpan.FromHours(1)) return $"{(int)elapsed.TotalMinutes}m";
        if (elapsed < TimeSpan.FromDays(1)) return $"{(int)elapsed.TotalHours}h";
        if (elapsed < TimeSpan.FromDays(7)) return $"{(int)elapsed.TotalDays}d";
        if (elapsed < TimeSpan.FromDays(30)) return $"{(int)(elapsed.TotalDays / 7)}w";
        return $"{(int)(elapsed.TotalDays / 30)}mo";
    }

    public static string DateHeading(DateOnly date, DateOnly today) => date == today ? "Today"
        : date == today.AddDays(-1) ? "Yesterday"
        : date.ToString(date.Year == today.Year ? "MMMM d" : "MMMM d, yyyy", CultureInfo.InvariantCulture);

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
        notification.Kind != "FlightScheduleUpdated" && TryGetFlightId(notification, out var flightId)
            ? $"/operations/flights?search={flightId}"
            : null;

    public static string InboxLink(NotificationDto notification) =>
        "/notifications";
}
