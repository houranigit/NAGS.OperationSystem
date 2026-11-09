package com.nags.operations.ui.notifications

import com.nags.operations.data.api.HttpClientFactory
import com.nags.operations.data.notifications.NotificationDto
import java.time.Instant
import java.time.ZoneId
import org.junit.Assert.*
import org.junit.Test

class NotificationFeedPresentationTest {
    @Test fun flightNumberStylingFindsFullLiteralReferencesInEitherCase() {
        val message = "Assigned to SV1020. View (sv1020) now."
        assertEquals(listOf("SV1020", "sv1020"), notificationFlightNumberRanges(message, "SV1020").map { message.substring(it) })
    }

    @Test fun flightNumberStylingDoesNotHighlightOtherFlightsWithTheSamePrefix() {
        val message = "SV1020A SV1020/01 XSV1020 SV10200 SV1020"
        assertEquals(listOf(message.lastIndexOf("SV1020") until message.length), notificationFlightNumberRanges(message, "SV1020"))
        val routeMessage = "Flight SV1020/01, then SV1020/02."
        assertEquals(listOf("SV1020/01"), notificationFlightNumberRanges(routeMessage, "SV1020/01").map { routeMessage.substring(it) })
    }

    @Test fun flightNumberStylingLeavesMessagesWithoutTheReferenceUnchanged() {
        assertTrue(notificationFlightNumberRanges("Flight SV1020", null).isEmpty())
        assertTrue(notificationFlightNumberRanges("Flight SV1020", "  ").isEmpty())
        assertTrue(notificationFlightNumberRanges("Your schedule changed", "SV1020").isEmpty())
    }

    private val now = Instant.parse("2026-11-09T13:00:00Z")
    private val zone = ZoneId.of("America/Chicago")

    @Test fun newRowsPrecedeHistoryAndRetainTheirSectionAfterAutomaticReads() {
        val notifications = listOf(row("old-today", "2026-11-09T12:00:00Z"),
            row("new-older", "2026-11-08T12:00:00Z"), row("unread", "2026-11-09T11:00:00Z", read = false))
        val result = notificationFeedRows(notifications, setOf("new-older"), now, zone)
        assertEquals(listOf("heading-new", "unread", "new-older", "heading-2026-11-09", "old-today"), result.map { it.key })
        assertEquals(listOf("New", "Today"), result.filterIsInstance<NotificationFeedRow.Heading>().map { it.label })
    }

    @Test fun dateGroupsUseLocalDaysAndEnglishDates() {
        val result = notificationFeedRows(listOf(row("yesterday", "2026-11-09T01:00:00Z"),
            row("earlier", "2026-11-07T12:00:00Z"), row("last-year", "2025-11-07T12:00:00Z")), emptySet(), now, zone)
        assertEquals(listOf("Yesterday", "November 7", "November 7, 2025"),
            result.filterIsInstance<NotificationFeedRow.Heading>().map { it.label })
    }

    @Test fun emptyInboxHasNoHeadingsAndInvalidDatesDoNotCrashHistory() {
        assertTrue(notificationFeedRows(emptyList(), emptySet(), now, zone).isEmpty())
        val result = notificationFeedRows(listOf(row("legacy", "invalid")), emptySet(), now, zone)
        assertEquals("Earlier", (result.first() as NotificationFeedRow.Heading).label)
        assertEquals("", relativeNotificationAge("invalid", now))
    }

    @Test fun compactAgesDistinguishMinutesFromMonthsAtUnitBoundaries() {
        val cases = mapOf(0L to "0s", 1L to "1s", 15L to "15s", 59L to "59s", 60L to "1m", 3599L to "59m",
            3600L to "1h", 86399L to "23h", 86400L to "1d", 604799L to "6d",
            604800L to "1w", 2591999L to "4w", 2592000L to "1mo", 5184000L to "2mo")
        cases.forEach { (seconds, expected) -> assertEquals(expected, relativeNotificationAge(now.minusSeconds(seconds).toString(), now)) }
        assertEquals("0s", relativeNotificationAge(now.plusSeconds(60).toString(), now))
    }

    @Test fun englishOnlyResponsesDoNotRequireArabicFields() {
        val row = HttpClientFactory.json.decodeFromString<NotificationDto>("""{"id":"one","kind":"StaffAssignedToFlight","titleEn":"New flight","bodyEn":"Assigned to XY123","isRead":false,"createdAtUtc":"2026-11-09T12:00:00Z"}""")
        assertEquals("New flight", row.titleEn)
        assertEquals("", row.titleAr)
        assertEquals("", row.bodyAr)
    }

    @Test fun headingRowsAreIncludedWhenCheckingTheEndOfTheVirtualList() {
        val state = NotificationsViewModel.UiState(items = List(10) { row("$it", "2026-11-09T12:00:00Z") },
            isLoading = false, hasMore = true)
        assertNull(nextNotificationPageToLoad(state, lastVisibleIndex = 8, renderedRowCount = 15))
        assertEquals(1, nextNotificationPageToLoad(state, lastVisibleIndex = 11, renderedRowCount = 15))
    }

    @Test fun partialSwipeAndShortFlickRevealTheDeleteButtonWithoutDeleting() {
        assertEquals(NotificationSwipePosition.Closed, notificationSwipeTarget(-15f, 0f, 400f, 80f))
        assertEquals(NotificationSwipePosition.Revealed, notificationSwipeTarget(-80f, 0f, 400f, 80f))
        assertEquals(NotificationSwipePosition.Revealed, notificationSwipeTarget(-120f, -1400f, 400f, 80f))
        assertEquals(NotificationSwipePosition.Revealed, notificationSwipeTarget(-259f, 0f, 400f, 80f))
    }

    @Test fun fullSwipeCommitsAndReversingBeforeReleaseCancelsTheAutomaticDelete() {
        assertEquals(NotificationSwipePosition.Committed, notificationSwipeTarget(-260f, 0f, 400f, 80f))
        assertEquals(NotificationSwipePosition.Committed, notificationSwipeTarget(-400f, -1000f, 400f, 80f))
        assertEquals(NotificationSwipePosition.Revealed, notificationSwipeTarget(-300f, 600f, 400f, 80f))
        assertEquals(NotificationSwipePosition.Closed, notificationSwipeTarget(-80f, 600f, 400f, 80f))
    }

    @Test fun viewRequiresOnlyAReferenceMatchingLocalMyFlights() {
        val id = "36780af4-97c1-4bb3-9af0-43c661b1ed92"
        val notification = row("one", now.toString()).copy(payload = mapOf("flightId" to id))
        assertTrue(canOpenNotification(notification, setOf(id)))
        assertTrue(canOpenNotification(notification.copy(payload = mapOf("flightId" to id.uppercase())), setOf(id)))
        assertFalse(canOpenNotification(notification, emptySet()))
        assertTrue(canOpenNotification(notification.copy(kind = "FlightScheduleUpdated"), setOf(id)))
        for (localId in listOf("legacy-flight-id", "1-1-1-1-1", "00000000-0000-0000-0000-000000000000")) {
            assertTrue(canOpenNotification(notification.copy(payload = mapOf("flightId" to localId)), setOf(localId)))
            assertFalse(canOpenNotification(notification.copy(payload = mapOf("flightId" to localId)), emptySet()))
        }
        assertFalse(canOpenNotification(notification.copy(payload = emptyMap()), setOf(id)))
    }

    @Test fun expiredReminderCanViewItsReferencedFlightWhenItIsInMyFlights() {
        val id = "36780af4-97c1-4bb3-9af0-43c661b1ed92"
        val reminder = row("reminder", now.toString()).copy(kind = "FlightReminder", payload = mapOf("flightId" to id,
            "scheduledArrivalUtc" to now.plusSeconds(60).toString()))
        assertTrue(canOpenNotification(reminder, setOf(id)))
        val expired = reminder.copy(payload = reminder.payload + ("scheduledArrivalUtc" to now.minusSeconds(60).toString()))
        assertTrue(canOpenNotification(expired, setOf(id)))
        assertFalse(canOpenNotification(expired, emptySet()))
    }

    private fun row(id: String, stamp: String, read: Boolean = true) = NotificationDto(id = id,
        kind = "StaffAssignedToFlight", titleEn = "Flight", bodyEn = "Flight updated", isRead = read, createdAtUtc = stamp)
}
