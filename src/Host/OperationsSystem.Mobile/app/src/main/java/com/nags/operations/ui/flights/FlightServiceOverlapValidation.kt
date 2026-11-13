package com.nags.operations.ui.flights

import com.nags.operations.R
import java.time.OffsetDateTime
import java.time.ZoneId

/** Compare the final visible graph; unchanged historical pairs do not prevent unrelated saves. */
internal fun serviceOverlapValidation(
    services: List<FlightServiceDraft>,
    zone: ZoneId,
    changedIds: Set<String>,
): Map<String, Map<String, Int>> {
    val periods = services.filter { it.period.start.isNotBlank() &&
        !it.serviceId.equals("40000000-0000-0000-0000-000000000003", true) }.mapNotNull { draft ->
        val input = draft.serviceInput(zone)
        val start = runCatching { OffsetDateTime.parse(input.startUtc).toInstant() }.getOrNull() ?: return@mapNotNull null
        val end = input.endUtc?.let { runCatching { OffsetDateTime.parse(it).toInstant() }.getOrNull() }
        if (input.endUtc != null && end == null || end != null && end <= start) return@mapNotNull null
        Triple(draft, start, end)
    }
    val conflicts = mutableSetOf<String>()
    periods.forEachIndexed { index, (first, from, to) ->
        periods.drop(index + 1).forEach { (second, otherFrom, otherTo) ->
            if (!first.serviceId.equals(second.serviceId, true) || first.id !in changedIds && second.id !in changedIds) return@forEach
            if ((otherTo == null || from < otherTo) && (to == null || otherFrom < to)) {
                if (first.id in changedIds) conflicts += first.id
                if (second.id in changedIds) conflicts += second.id
            }
        }
    }
    return conflicts.associateWith { mapOf("startUtc" to R.string.flight_service_overlap) }
}
