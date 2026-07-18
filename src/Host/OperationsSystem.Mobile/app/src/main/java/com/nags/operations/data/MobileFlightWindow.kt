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

/** A realtime row enters a list cache only when both server and local boundary checks agree. */
fun MobileFlightDto.shouldEnterMobileFlightCache(now: Instant = Instant.now()): Boolean =
    areMobileActionsAvailable(now)

/** A cached deep-link fallback may render details, but can never inherit cached action authority. */
fun MobileFlightDto.asInformationOnlyMobileDetail(): MobileFlightDto =
    copy(isWithinMobileWindow = false)

private fun parseMobileInstant(value: String): Instant? =
    runCatching { OffsetDateTime.parse(value).toInstant() }
        .recoverCatching { Instant.parse(value) }
        .getOrNull()
