package com.nags.operations.data.notifications

import kotlinx.serialization.Serializable

/** Account preferences shared by the API, portal and Android client. */
@Serializable
data class NotificationPreferences(
    val enabled: Boolean = true,
    val flightAssignments: Boolean = true,
    val flightInvitations: Boolean = true,
    val scheduleChanges: Boolean = true,
    val colleagueEdits: Boolean = true,
    val syncIssues: Boolean = true,
    val directShares: Boolean = true,
    val stationShares: Boolean = true,
    val flightReminders: Boolean = true,
) {
    fun value(key: NotificationPreferenceKey): Boolean = when (key) {
        NotificationPreferenceKey.Enabled -> enabled
        NotificationPreferenceKey.FlightAssignments -> flightAssignments
        NotificationPreferenceKey.FlightInvitations -> flightInvitations
        NotificationPreferenceKey.ScheduleChanges -> scheduleChanges
        NotificationPreferenceKey.ColleagueEdits -> colleagueEdits
        NotificationPreferenceKey.SyncIssues -> syncIssues
        NotificationPreferenceKey.DirectShares -> directShares
        NotificationPreferenceKey.StationShares -> stationShares
        NotificationPreferenceKey.FlightReminders -> flightReminders
    }

    fun allows(kind: String): Boolean = enabled && when (kind) {
        NotificationKinds.StaffAssignedToFlight -> flightAssignments
        NotificationKinds.EmployeeInvitedToFlight -> flightInvitations
        NotificationKinds.FlightScheduleUpdated -> scheduleChanges
        NotificationKinds.FlightSubmissionUpdated -> colleagueEdits
        NotificationKinds.FlightReminder -> flightReminders
        NotificationKinds.FlightSharedWithEmployee -> directShares
        NotificationKinds.FlightSharedWithStation -> stationShares
        "FlightSyncIssue" -> syncIssues
        else -> true
    }
}

enum class NotificationPreferenceKey(val wireName: String) {
    Enabled("enabled"),
    FlightAssignments("flightAssignments"),
    FlightInvitations("flightInvitations"),
    ScheduleChanges("scheduleChanges"),
    ColleagueEdits("colleagueEdits"),
    SyncIssues("syncIssues"),
    DirectShares("directShares"),
    StationShares("stationShares"),
    FlightReminders("flightReminders"),
}

@Serializable
data class UpdateNotificationPreferenceRequest(val key: String, val enabled: Boolean)
