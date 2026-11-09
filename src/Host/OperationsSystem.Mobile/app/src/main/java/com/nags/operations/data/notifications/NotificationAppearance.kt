package com.nags.operations.data.notifications

import androidx.annotation.ColorInt
import androidx.annotation.DrawableRes
import com.nags.operations.R

internal data class NotificationAppearance(
    @param:ColorInt val accentArgb: Int,
    @param:DrawableRes val iconResource: Int,
)

private val assignmentAppearance = NotificationAppearance(0xFF007AFF.toInt(), R.drawable.ic_notification_assignment)
private val reminderAppearance = NotificationAppearance(0xFFC76A0A.toInt(), R.drawable.ic_notification_reminder)
private val scheduleAppearance = NotificationAppearance(0xFF1689B9.toInt(), R.drawable.ic_notification_schedule)
private val invitationAppearance = NotificationAppearance(0xFF248A3D.toInt(), R.drawable.ic_notification_invitation)

/** Shared by the inbox and Android alerts so a kind keeps the same color and glyph. */
internal fun notificationAppearance(kind: String): NotificationAppearance = when (kind) {
    NotificationKinds.FlightReminder -> reminderAppearance
    NotificationKinds.FlightScheduleUpdated -> scheduleAppearance
    NotificationKinds.EmployeeInvitedToFlight -> invitationAppearance
    else -> assignmentAppearance
}
