namespace Notifications.Contracts;

/// <summary>Account-wide choices shared by the portal and mobile clients.</summary>
public sealed record NotificationPreferencesDto(
    bool Enabled = true,
    bool FlightAssignments = true,
    bool FlightInvitations = true,
    bool ScheduleChanges = true,
    bool ColleagueEdits = true,
    bool SyncIssues = true,
    bool DirectShares = true,
    bool StationShares = true,
    bool FlightReminders = true);
