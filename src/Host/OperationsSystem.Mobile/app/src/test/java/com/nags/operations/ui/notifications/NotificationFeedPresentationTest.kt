package com.nags.operations.ui.notifications

import com.nags.operations.data.api.HttpClientFactory
import com.nags.operations.data.notifications.NotificationDto
import java.time.Instant
import java.time.ZoneId
import org.junit.Assert.*
import org.junit.Test

class NotificationFeedPresentationTest {
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
        val cases = mapOf(0L to "now", 59L to "now", 60L to "1m", 3599L to "59m",
            3600L to "1h", 86399L to "23h", 86400L to "1d", 604799L to "6d",
            604800L to "1w", 2591999L to "4w", 2592000L to "1mo", 5184000L to "2mo")
        cases.forEach { (seconds, expected) -> assertEquals(expected, relativeNotificationAge(now.minusSeconds(seconds).toString(), now)) }
        assertEquals("now", relativeNotificationAge(now.plusSeconds(60).toString(), now))
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

    @Test fun openRequiresALocalSyncedFlightAndAValidConcreteReference() {
        val id = "36780af4-97c1-4bb3-9af0-43c661b1ed92"
        val notification = row("one", now.toString()).copy(payload = mapOf("flightId" to id))
        assertTrue(canOpenNotification(notification, setOf(id), now))
        assertTrue(canOpenNotification(notification.copy(payload = mapOf("flightId" to id.uppercase())), setOf(id), now))
        assertFalse(canOpenNotification(notification, emptySet(), now))
        assertFalse(canOpenNotification(notification.copy(kind = "FlightScheduleUpdated"), setOf(id), now))
        for (invalid in listOf("", "not-a-guid", "1-1-1-1-1", "00000000-0000-0000-0000-000000000000")) {
            assertFalse(canOpenNotification(notification.copy(payload = mapOf("flightId" to invalid)), setOf(invalid), now))
        }
    }

    @Test fun expiredReminderCannotOfferOpenEvenForASyncedFlight() {
        val id = "36780af4-97c1-4bb3-9af0-43c661b1ed92"
        val reminder = row("reminder", now.toString()).copy(kind = "FlightReminder", payload = mapOf("flightId" to id,
            "scheduledArrivalUtc" to now.plusSeconds(60).toString()))
        assertTrue(canOpenNotification(reminder, setOf(id), now))
        assertFalse(canOpenNotification(reminder, setOf(id), now.plusSeconds(60)))
    }

    private fun row(id: String, stamp: String, read: Boolean = true) = NotificationDto(id = id,
        kind = "StaffAssignedToFlight", titleEn = "Flight", bodyEn = "Flight updated", isRead = read, createdAtUtc = stamp)
}
