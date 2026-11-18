using BuildingBlocks.Domain.Results;

namespace Notifications.Domain.Preferences;

/// <summary>One account-owned choice. Absent choices retain the enabled default.</summary>
public sealed class NotificationPreference
{
    private NotificationPreference() { }

    public Guid UserId { get; private set; }
    public string Key { get; private set; } = null!;
    public bool Enabled { get; private set; }

    public static Result<NotificationPreference> Create(Guid userId, string key, bool enabled)
    {
        if (userId == Guid.Empty)
            return Error.Validation("User id is required.", "Notifications.Preference.UserRequired");
        if (!NotificationPreferenceKeys.IsSupported(key))
            return Error.Validation("Choose a valid notification preference.", "Notifications.Preference.Invalid");

        return new NotificationPreference { UserId = userId, Key = key, Enabled = enabled };
    }

    public void SetEnabled(bool enabled) => Enabled = enabled;
}

public static class NotificationPreferenceKeys
{
    public const string Enabled = "enabled";
    public const string FlightAssignments = "flightAssignments";
    public const string FlightInvitations = "flightInvitations";
    public const string ScheduleChanges = "scheduleChanges";
    public const string ColleagueEdits = "colleagueEdits";
    public const string SyncIssues = "syncIssues";
    public const string DirectShares = "directShares";
    public const string StationShares = "stationShares";
    public const string FlightReminders = "flightReminders";

    public static bool IsSupported(string? key) => key is Enabled or FlightAssignments or FlightInvitations
        or ScheduleChanges or ColleagueEdits or SyncIssues or DirectShares or StationShares or FlightReminders;
}
