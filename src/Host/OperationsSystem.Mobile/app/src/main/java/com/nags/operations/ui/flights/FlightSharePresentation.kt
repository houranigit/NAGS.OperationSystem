package com.nags.operations.ui.flights

import java.time.Duration
import java.time.Instant
import java.time.OffsetDateTime

internal enum class ShareAgeKind { JustNow, Minutes, Hours, Days, Date }
internal data class SharePostAge(val kind: ShareAgeKind, val count: Int = 0)

/** Recent posts use relative recording time; after a week show the station-local full date. */
internal fun sharePostAge(createdAtUtc: String, now: Instant): SharePostAge {
    val created = runCatching { OffsetDateTime.parse(createdAtUtc).toInstant() }.getOrNull()
        ?: return SharePostAge(ShareAgeKind.Date)
    val seconds = Duration.between(created, now).seconds.coerceAtLeast(0)
    return when {
        seconds < 60 -> SharePostAge(ShareAgeKind.JustNow)
        seconds < 3600 -> SharePostAge(ShareAgeKind.Minutes, (seconds / 60).toInt())
        seconds < 86400 -> SharePostAge(ShareAgeKind.Hours, (seconds / 3600).toInt())
        seconds < 604800 -> SharePostAge(ShareAgeKind.Days, (seconds / 86400).toInt())
        else -> SharePostAge(ShareAgeKind.Date)
    }
}
