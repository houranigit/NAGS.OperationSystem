package com.nags.operations.data

import java.time.Duration
import java.time.Instant
import java.time.OffsetDateTime
import kotlinx.serialization.Serializable

enum class FlightCategory { MyFlights, PerLanding, AdHoc }

/** Membership comes from the server; operation display names never determine a tab. */
fun FlightListItem.matches(category: FlightCategory?): Boolean = when (category) {
    FlightCategory.MyFlights -> isMyFlight
    FlightCategory.PerLanding -> isPerLanding && !isMyAdHocFlight && !isAdHocFlight
    FlightCategory.AdHoc -> isAdHocFlight || isMyAdHocFlight
    null -> isMyFlight || isPerLanding || isAdHocFlight || isMyAdHocFlight
}

/** Current operational evidence keeps unresolved flights visible beyond their scheduled departure. */
fun FlightListItem.isInFlightWindow(at: Instant): Boolean = runCatching {
    if (visibilityPolicyVersion != FLIGHT_VISIBILITY_POLICY_VERSION)
        return@runCatching overlapsFlightWindow(scheduledArrivalUtc, scheduledDepartureUtc, at)
    if (hasArrivalSubmission == null || hasDepartureSubmission == null || hasCancellationSubmission == null ||
        hasOpenReturnToRamp == null || hasDepartureSubmission && latestDepartureAtUtc == null ||
        hasCancellationSubmission && latestCancellationAtUtc == null) return@runCatching false
    operationallyVisible(scheduledArrivalUtc, scheduledDepartureUtc, hasArrivalSubmission, hasDepartureSubmission,
        hasCancellationSubmission, hasOpenReturnToRamp,
        listOfNotNull(latestDepartureAtUtc, latestReturnToRampDepartureAtUtc, latestCancellationAtUtc), at)
}.getOrDefault(false)

fun FlightDetail.isInFlightWindow(at: Instant): Boolean = runCatching {
    // Legacy cancellation snapshots without actual records cannot establish an unlimited operational view.
    if (hasCancellationSubmission && cancellationSubmissions.isEmpty())
        return@runCatching overlapsFlightWindow(scheduledArrivalUtc, scheduledDepartureUtc, at)
    operationallyVisible(scheduledArrivalUtc, scheduledDepartureUtc, arrivalSubmissions.isNotEmpty(),
        departureSubmissions.isNotEmpty(), hasCancellationSubmission || cancellationSubmissions.isNotEmpty(),
        returnToRamps.any { it.isOpen }, departureSubmissions.map { it.atd } +
            returnToRamps.mapNotNull { it.departedAtUtc } + cancellationSubmissions.map { it.canceledAtUtc }, at)
}.getOrDefault(false)

/** Planning input has no operational evidence to override the upcoming-arrival cutoff. */
fun isWithinFlightPlanningWindow(arrival: String, departure: String, at: Instant): Boolean = runCatching {
    val sta = parseFlightInstant(arrival)
    !parseFlightInstant(departure).isBefore(sta) && !sta.isAfter(at.plus(FLIGHT_WINDOW_HALF))
}.getOrDefault(false)

private fun operationallyVisible(arrival: String, departure: String, hasArrival: Boolean, hasDeparture: Boolean,
    hasCancellation: Boolean, hasOpenReturn: Boolean, terminalTimes: List<String>, at: Instant): Boolean {
    val sta = parseFlightInstant(arrival)
    if (parseFlightInstant(departure).isBefore(sta)) return false
    if (hasOpenReturn || hasArrival && !hasDeparture) return true
    if (!hasArrival && !hasDeparture && !hasCancellation && terminalTimes.isEmpty())
        return !sta.isAfter(at.plus(FLIGHT_WINDOW_HALF))
    return terminalTimes.map(::parseFlightInstant).maxOrNull()?.let { !it.isBefore(at.minus(FLIGHT_WINDOW_HALF)) } == true
}

private fun overlapsFlightWindow(arrival: String, departure: String, at: Instant): Boolean = runCatching {
    val sta = parseFlightInstant(arrival)
    val std = parseFlightInstant(departure)
    !std.isBefore(sta) && !sta.isAfter(at.plus(FLIGHT_WINDOW_HALF)) && !std.isBefore(at.minus(FLIGHT_WINDOW_HALF))
}.getOrDefault(false)

/** Complete detail reads and acknowledged writes supply every field required by the visibility policy. */
fun FlightListItem.withVisibilityReports(detail: FlightDetail): FlightListItem = copy(
    hasArrivalSubmission = detail.arrivalSubmissions.isNotEmpty(),
    hasDepartureSubmission = detail.departureSubmissions.isNotEmpty(),
    hasCancellationSubmission = detail.hasCancellationSubmission || detail.cancellationSubmissions.isNotEmpty(),
    hasOpenReturnToRamp = detail.returnToRamps.any { it.isOpen },
    latestDepartureAtUtc = latestFlightTime(detail.departureSubmissions.map { it.atd }),
    latestReturnToRampDepartureAtUtc = latestFlightTime(detail.returnToRamps.mapNotNull { it.departedAtUtc }),
    latestCancellationAtUtc = latestFlightTime(detail.cancellationSubmissions.map { it.canceledAtUtc }),
    visibilityPolicyVersion = FLIGHT_VISIBILITY_POLICY_VERSION,
    approvalStatus = detail.approvalStatus,
    approvedByDisplayName = detail.approvedByDisplayName,
    approvedAtUtc = detail.approvedAtUtc,
    approvedReturnToRampSequence = detail.approvedReturnToRampSequence,
)

internal fun latestFlightTime(values: List<String>): String? = values.mapNotNull { value ->
    runCatching { parseFlightInstant(value) to value }.getOrNull()
}.maxByOrNull { it.first }?.second

internal const val FLIGHT_VISIBILITY_POLICY_VERSION = 1

// Older Android Instant parsers reject the numeric offsets returned by the API.
internal fun parseFlightInstant(value: String): Instant = OffsetDateTime.parse(value).toInstant()

internal val FLIGHT_WINDOW_HALF: Duration = Duration.ofHours(12)

@Serializable
internal data class FlightScope(
    val stationId: String,
    val asOfUtc: String,
    val windowStartUtc: String,
    val windowEndUtc: String,
) {
    fun isValid(): Boolean = stationId.isNotBlank() && runCatching {
        val at = parseFlightInstant(asOfUtc)
        // The bounds now describe terminal retention and upcoming STA, not a hard schedule interval.
        parseFlightInstant(windowStartUtc) == at.minus(FLIGHT_WINDOW_HALF) &&
            parseFlightInstant(windowEndUtc) == at.plus(FLIGHT_WINDOW_HALF)
    }.getOrDefault(false)
}

internal fun FlightPage.scopeOrNull(): FlightScope? {
    val scope = FlightScope(stationId ?: return null, asOfUtc ?: return null,
        windowStartUtc ?: return null, windowEndUtc ?: return null)
    return scope.takeIf { it.isValid() }
}
