package com.nags.operations.data.notifications

import com.nags.operations.R
import org.junit.Assert.assertEquals
import org.junit.Test

class NotificationAppearanceTest {
    @Test fun eachNotificationKindUsesItsInboxColorAndMatchingMaterialGlyph() {
        val expected = listOf(
            Triple(NotificationKinds.StaffAssignedToFlight, 0xFF007AFF.toInt(), R.drawable.ic_notification_assignment),
            Triple(NotificationKinds.FlightReminder, 0xFFC76A0A.toInt(), R.drawable.ic_notification_reminder),
            Triple(NotificationKinds.FlightScheduleUpdated, 0xFF1689B9.toInt(), R.drawable.ic_notification_schedule),
            Triple(NotificationKinds.EmployeeInvitedToFlight, 0xFF248A3D.toInt(), R.drawable.ic_notification_invitation),
        )
        expected.forEach { (kind, color, icon) ->
            val appearance = notificationAppearance(kind)
            assertEquals(kind, color, appearance.accentArgb)
            assertEquals(kind, icon, appearance.iconResource)
        }
    }

    @Test fun unknownLegacyKindsUseTheAssignmentAppearance() {
        assertEquals(notificationAppearance(NotificationKinds.StaffAssignedToFlight), notificationAppearance("LegacyNotification"))
    }
}
