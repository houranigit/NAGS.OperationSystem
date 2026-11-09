package com.nags.operations.ui.notifications

import com.nags.operations.data.notifications.NotificationDto
import java.time.Duration
import java.time.Instant
import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

internal sealed interface NotificationFeedRow {
    val key: String
    data class Heading(override val key: String, val label: String) : NotificationFeedRow
    data class Item(val notification: NotificationDto) : NotificationFeedRow {
        override val key: String get() = notification.id
    }
}

internal fun notificationFeedRows(
    notifications: List<NotificationDto>,
    newIds: Set<String>,
    now: Instant = Instant.now(),
    zone: ZoneId = ZoneId.systemDefault(),
): List<NotificationFeedRow> = buildList {
    val sorted = notifications.sortedWith(compareByDescending<NotificationDto> { notificationInstant(it.createdAtUtc) }
        .thenByDescending { it.id })
    val (new, older) = sorted.partition { it.id in newIds || !it.isRead }
    if (new.isNotEmpty()) {
        add(NotificationFeedRow.Heading("heading-new", "New"))
        new.forEach { add(NotificationFeedRow.Item(it)) }
    }
    val today = now.atZone(zone).toLocalDate()
    older.groupBy { notificationInstant(it.createdAtUtc)?.atZone(zone)?.toLocalDate() }.forEach { (date, rows) ->
        add(NotificationFeedRow.Heading("heading-${date ?: "earlier"}", notificationDateLabel(date, today)))
        rows.forEach { add(NotificationFeedRow.Item(it)) }
    }
}

private fun notificationDateLabel(date: LocalDate?, today: LocalDate): String = when (date) {
    null -> "Earlier"
    today -> "Today"
    today.minusDays(1) -> "Yesterday"
    else -> date.format(DateTimeFormatter.ofPattern(if (date.year == today.year) "MMMM d" else "MMMM d, yyyy", Locale.ENGLISH))
}

internal fun relativeNotificationAge(createdAtUtc: String, now: Instant = Instant.now()): String {
    val created = notificationInstant(createdAtUtc) ?: return ""
    val seconds = Duration.between(created, now).seconds.coerceAtLeast(0)
    return when {
        seconds < 60 -> "now"
        seconds < 3_600 -> "${seconds / 60}m"
        seconds < 86_400 -> "${seconds / 3_600}h"
        seconds < 604_800 -> "${seconds / 86_400}d"
        seconds < 2_592_000 -> "${seconds / 604_800}w"
        else -> "${seconds / 2_592_000}mo"
    }
}

private fun notificationInstant(value: String): Instant? =
    runCatching { OffsetDateTime.parse(value).toInstant() }.getOrNull()


internal enum class NotificationSwipePosition { Closed, Revealed, Committed }

/** A short flick reveals the action; automatic removal needs a deliberate full-width swipe. */
internal fun notificationSwipeTarget(offset: Float, velocity: Float, width: Float, actionWidth: Float): NotificationSwipePosition {
    val distance = (-offset).coerceAtLeast(0f)
    if (velocity > 300f) return if (distance <= actionWidth * 1.5f) NotificationSwipePosition.Closed else NotificationSwipePosition.Revealed
    if (distance >= width * 0.65f) return NotificationSwipePosition.Committed
    if (distance >= actionWidth * 0.4f || velocity < -500f) return NotificationSwipePosition.Revealed
    return NotificationSwipePosition.Closed
}

internal fun notificationAccentArgb(kind: String): Long = when (kind) {
    com.nags.operations.data.notifications.NotificationKinds.FlightReminder -> 0xFFC76A0A
    com.nags.operations.data.notifications.NotificationKinds.EmployeeInvitedToFlight -> 0xFF248A3D
    com.nags.operations.data.notifications.NotificationKinds.FlightScheduleUpdated -> 0xFF1689B9
    else -> 0xFF007AFF // Matches the Scheduled badge on flight cards.
}
