package com.nags.operations.data

import java.time.Instant
import java.time.OffsetDateTime
import java.util.Locale

/** Each occurrence keeps its own ID even when its service or employee repeats. */
internal data class OperationalWorkPeriod(val id: String, val ownerId: String, val startUtc: String, val endUtc: String?)

/** Adjacent windows are distinct; an open End reserves the remaining time. */
internal fun conflictingWorkPeriodIds(occurrences: List<OperationalWorkPeriod>): Set<String> {
    fun instant(raw: String) = runCatching { OffsetDateTime.parse(raw).takeIf {
        it.year in 1..9999 && it.toInstant() != Instant.parse("0001-01-01T00:00:00Z")
    }?.toInstant() }.getOrNull()
    val periods = occurrences.mapNotNull { occurrence ->
        val start = instant(occurrence.startUtc) ?: return@mapNotNull null
        val end = occurrence.endUtc?.let(::instant)
        if (occurrence.endUtc != null && end == null || end != null && end < start) return@mapNotNull null
        Triple(occurrence, start, end)
    }.groupBy { it.first.ownerId.lowercase(Locale.ROOT) }
    return buildSet {
        periods.values.forEach { group ->
            group.forEachIndexed { index, (first, start, end) ->
                group.drop(index + 1).forEach { (second, otherStart, otherEnd) ->
                    // Duplicate occurrence IDs are an identity error, not a second working period.
                    if (!first.id.equals(second.id, true) &&
                        (start == otherStart && end == otherEnd || end != start && otherEnd != otherStart &&
                            (otherEnd == null || start < otherEnd) && (end == null || otherStart < end))) {
                        add(first.id)
                        add(second.id)
                    }
                }
            }
        }
    }
}
