package com.nags.operations.data.notifications

import java.time.Instant
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
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
        assertEquals("user-1", requireNotNull(payload.openRequest()).recipientUserId)
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
    fun supportsScheduleUpdateWithoutFlightDeepLink() {
        val payload = NotificationPushPayload.fromData(
            mapOf(
                "notificationId" to "notification-3",
                "kind" to "FlightScheduleUpdated",
                "recipientUserId" to "user-1",
                "titleEn" to "Your schedule was updated",
                "bodyEn" to "Open My Flights to review your latest schedule.",
            ),
        )

        requireNotNull(payload)
        assertNull(payload.flightId)
        assertNull(requireNotNull(payload.openRequest()).flightId)
        assertEquals("notification-3", payload.openRequest()?.notificationId)
    }

    @Test
    fun supportsFlightReminderWithFlightDeepLink() {
        val sta = Instant.parse("2026-07-18T18:00:00Z")
        val payload = NotificationPushPayload.fromData(
            mapOf(
                "notificationId" to "notification-4",
                "kind" to "FlightReminder",
                "flightId" to "flight-4",
                "flightNumber" to "SV404",
                "scheduledArrivalUtc" to sta.toString(),
                "leadTimeMinutes" to "720",
            ),
            now = sta.minusSeconds(12 * 60 * 60),
        )

        requireNotNull(payload)
        assertEquals("flight-4", requireNotNull(payload.openRequest(sta.minusSeconds(1))).flightId)
        assertEquals("SV404", payload.flightNumber)
        assertEquals(sta.toString(), payload.scheduledArrivalUtc)
        assertEquals(720, payload.leadTimeMinutes)
        assertEquals("720", payload.toDto().payload["leadTimeMinutes"])
        assertNull(payload.openRequest(sta))
        assertFalse(payload.toDto().isExpiredReminder(sta.minusSeconds(1)))
        assertTrue(payload.toDto().isExpiredReminder(sta))
    }

    @Test
    fun rejectsExpiredOrMalformedReminderPayload() {
        val sta = Instant.parse("2026-07-18T18:00:00Z")
        val data = mapOf(
            "notificationId" to "notification-5",
            "kind" to "FlightReminder",
            "flightId" to "flight-5",
            "scheduledArrivalUtc" to sta.toString(),
            "leadTimeMinutes" to "30",
        )

        assertNull(NotificationPushPayload.fromData(data, now = sta))
        assertNull(NotificationPushPayload.fromData(data, now = sta.plusSeconds(1)))
        assertNull(
            NotificationPushPayload.fromData(
                data + ("leadTimeMinutes" to "not-a-number"),
                now = sta.minusSeconds(1),
            ),
        )
    }

    @Test
    fun sameFlightNotificationsRetainDistinctRequestIdentity() {
        val first = NotificationOpenRequest("notification-a", "flight-1")
        val second = NotificationOpenRequest("notification-b", "flight-1")

        assertNotEquals(first, second)
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
