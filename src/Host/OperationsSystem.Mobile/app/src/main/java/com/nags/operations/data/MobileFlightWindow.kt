package com.nags.operations.data

import java.time.Duration
import java.time.Instant
import java.time.OffsetDateTime

const val MOBILE_FLIGHT_WINDOW_HOURS: Long = 12

enum class MobileFlightWindowPhase {
    Before,
    Within,
    After,
    Unknown,
}

data class MobileFlightWindowEvaluation(
    val phase: MobileFlightWindowPhase,
    val startsAt: Instant? = null,
    val endsAt: Instant? = null,
) {
    val isWithinWindow: Boolean get() = phase == MobileFlightWindowPhase.Within
}

/**
 * Client-side mirror used as a defensive cache/list check. The server remains authoritative and
 * supplies the same boundaries on every flight response. Invalid timestamps fail closed.
 */
fun evaluateMobileFlightWindow(
    scheduledArrivalUtc: String,
    now: Instant = Instant.now(),
    startsAtUtc: String? = null,
    endsAtUtc: String? = null,
): MobileFlightWindowEvaluation {
    val sta = parseMobileInstant(scheduledArrivalUtc)
        ?: return MobileFlightWindowEvaluation(MobileFlightWindowPhase.Unknown)
    val startsAt = startsAtUtc?.let(::parseMobileInstant)
        ?: sta.minus(Duration.ofHours(MOBILE_FLIGHT_WINDOW_HOURS))
    val endsAt = endsAtUtc?.let(::parseMobileInstant)
        ?: sta.plus(Duration.ofHours(MOBILE_FLIGHT_WINDOW_HOURS))

    val phase = when {
        now.isBefore(startsAt) -> MobileFlightWindowPhase.Before
        now.isAfter(endsAt) -> MobileFlightWindowPhase.After
        else -> MobileFlightWindowPhase.Within
    }
    return MobileFlightWindowEvaluation(phase, startsAt, endsAt)
}

fun MobileFlightDto.evaluateMobileWindow(now: Instant = Instant.now()): MobileFlightWindowEvaluation =
    evaluateMobileFlightWindow(
        scheduledArrivalUtc = scheduledArrivalUtc,
        now = now,
        startsAtUtc = mobileWindowStartsAtUtc,
        endsAtUtc = mobileWindowEndsAtUtc,
    )

fun MobileFlightDto.isLocallyWithinMobileWindow(now: Instant = Instant.now()): Boolean =
    evaluateMobileWindow(now).isWithinWindow

/** Actions require both the latest server decision and a valid local boundary evaluation. */
fun MobileFlightDto.areMobileActionsAvailable(now: Instant = Instant.now()): Boolean =
    isWithinMobileWindow && isLocallyWithinMobileWindow(now)

/** The three Room flight tables have deliberately different membership rules. */
enum class MobileFlightCache {
    MyFlights,
    PerLandingFlights,
    AdHocFlights,
}

private val MOBILE_LIST_STATUSES = setOf(
    FlightStatusKind.Scheduled.wire,
    FlightStatusKind.InProgress.wire,
    FlightStatusKind.Completed.wire,
)

/**
 * A snapshot or realtime row enters a list cache only when its status, list membership, and both
 * server/local window checks agree. Keeping this policy at the write boundary prevents a by-id
 * Ad Hoc upsert (for example, after an invitation) from leaking into My Flights.
 */
fun MobileFlightDto.shouldEnterMobileFlightCache(
    cache: MobileFlightCache,
    now: Instant = Instant.now(),
): Boolean {
    if (status !in MOBILE_LIST_STATUSES || !areMobileActionsAvailable(now)) return false

    return when (cache) {
        MobileFlightCache.MyFlights -> !isPerLanding && !isAdHoc
        MobileFlightCache.PerLandingFlights -> isPerLanding && !isAdHoc
        MobileFlightCache.AdHocFlights -> isAdHoc
    }
}

/** A cached deep-link fallback may render details, but can never inherit cached action authority. */
fun MobileFlightDto.asInformationOnlyMobileDetail(): MobileFlightDto =
    copy(isWithinMobileWindow = false)

private fun parseMobileInstant(value: String): Instant? =
    runCatching { OffsetDateTime.parse(value).toInstant() }
        .recoverCatching { Instant.parse(value) }
        .getOrNull()
