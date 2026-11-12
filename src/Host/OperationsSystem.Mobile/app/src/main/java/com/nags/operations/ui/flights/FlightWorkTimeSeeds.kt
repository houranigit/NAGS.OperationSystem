package com.nags.operations.ui.flights

import com.nags.operations.data.arrivalInputTime
import java.time.Instant
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.temporal.ChronoUnit

/** Optional actual/service limits affect defaults only; validation still owns entered values. */
internal data class FlightWorkTimeBounds(val lowerUtc: Instant? = null, val upperUtc: Instant? = null) {
    fun within(period: FlightWorkPeriod, zone: ZoneId) = FlightWorkTimeBounds(
        listOfNotNull(lowerUtc, periodInstant(period.start, period.originalStartUtc, zone)).maxOrNull(),
        listOfNotNull(upperUtc, periodInstant(period.end, period.originalEndUtc, zone)).minOrNull(),
    )
}

internal fun flightWorkTimeBounds(arrivalUtc: String?, departureUtc: String?) = FlightWorkTimeBounds(
    arrivalUtc?.let { runCatching { OffsetDateTime.parse(it).toInstant() }.getOrNull() },
    departureUtc?.let { runCatching { OffsetDateTime.parse(it).toInstant() }.getOrNull() },
)

internal fun seedBlankWorkStart(period: FlightWorkPeriod, zone: ZoneId, bounds: FlightWorkTimeBounds,
    now: Instant = Instant.now()): FlightWorkPeriod {
    if (period.start.isNotBlank()) return period
    val seed = boundedMinute(now, bounds)
    return period.copy(start = seed.atZone(zone).toLocalDateTime().toString(), originalStartUtc = seed.toString())
}

internal fun seedBlankWorkEnd(period: FlightWorkPeriod, zone: ZoneId, bounds: FlightWorkTimeBounds,
    preferNow: Boolean, now: Instant = Instant.now()): FlightWorkPeriod {
    if (period.end.isNotBlank()) return period
    val start = periodInstant(period.start, period.originalStartUtc, zone)
    val limits = bounds.copy(lowerUtc = listOfNotNull(bounds.lowerUtc, start).maxOrNull())
    val seed = boundedMinute(if (preferNow) now else start ?: now, limits)
    return period.copy(end = seed.atZone(zone).toLocalDateTime().toString(), originalEndUtc = seed.toString())
}

/** Only staff captured as blank when the first Start picker opened follow that selection session. */
internal fun servicePeriodWithInitialStaff(service: FlightServiceDraft, period: FlightWorkPeriod,
    followingStaffIds: Set<String>): FlightServiceDraft = service.copy(period = period,
    employees = service.employees.map { employee ->
        if (employee.id in followingStaffIds) employee.copy(period = employee.period.copy(
            start = period.start, originalStartUtc = period.originalStartUtc)) else employee
    })

private fun periodInstant(raw: String, original: String?, zone: ZoneId): Instant? =
    runCatching { Instant.parse(arrivalInputTime(raw, original, zone)) }.getOrNull()

private fun boundedMinute(preferred: Instant, bounds: FlightWorkTimeBounds): Instant {
    val candidate = preferred.truncatedTo(ChronoUnit.MINUTES)
    val lower = bounds.lowerUtc?.let { instant -> instant.truncatedTo(ChronoUnit.MINUTES).let {
        if (it < instant) it.plusSeconds(60) else it
    } }
    val upper = bounds.upperUtc?.truncatedTo(ChronoUnit.MINUTES)
    // No selectable minute fits: retain the ordinary default for existing field validation.
    // Do not widen actual bounds or manufacture a different business rule.
    if (lower != null && upper != null && lower > upper) return candidate
    return when {
        lower != null && candidate < lower -> lower
        upper != null && candidate > upper -> upper
        else -> candidate
    }
}
