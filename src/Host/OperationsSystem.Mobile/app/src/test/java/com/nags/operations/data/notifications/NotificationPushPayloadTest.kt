package com.nags.operations.data.notifications

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class NotificationPushPayloadTest {
    @Test
    fun parsesFlightAssignmentPayloadAndOpenRequest() {
        val payload = NotificationPushPayload.fromData(
            mapOf(
                "notificationId" to "notification-1",
                "kind" to "StaffAssignedToFlight",
                "recipientUserId" to "user-1",
                "flightId" to "flight-1",
                "flightNumber" to "SV123",
                "titleEn" to "You were assigned to a flight",
                "bodyEn" to "A teammate added you to flight SV123.",
                "titleAr" to "تم تعيينك في رحلة",
                "bodyAr" to "أضافك أحد زملائك إلى الرحلة SV123.",
                "createdAtUtc" to "2026-07-12T10:00:00Z",
            ),
        )

        requireNotNull(payload)
        assertEquals("flight-1", payload.flightId)
        assertEquals("SV123", payload.flightNumber)
        assertEquals("user-1", payload.openRequest()?.recipientUserId)
        assertEquals("notification-1", payload.toDto().id)
    }

    @Test
    fun supportsInviteKindAndLegacyNestedPayload() {
        val payload = NotificationPushPayload.fromData(
            mapOf(
                "id" to "notification-2",
                "kind" to "EmployeeInvitedToFlight",
                "payloadJson" to """{"flightId":"flight-2","flightNumber":"XY45"}""",
            ),
        )

        requireNotNull(payload)
        assertEquals("flight-2", payload.flightId)
        assertEquals("XY45", payload.flightNumber)
    }

    @Test
    fun rejectsUnknownKindsAndMissingIds() {
        assertNull(NotificationPushPayload.fromData(mapOf("kind" to "StaffAssignedToFlight")))
        assertNull(
            NotificationPushPayload.fromData(
                mapOf("notificationId" to "1", "kind" to "WorkOrderApproved"),
            ),
        )
    }
}

