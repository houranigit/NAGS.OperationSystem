package com.nags.operations.data

import java.time.Duration
import java.time.Instant
import kotlinx.serialization.Serializable

enum class FlightCategory { MyFlights, PerLanding, AdHoc }

/** Membership comes from the server; operation display names never determine a tab. */
fun FlightListItem.matches(category: FlightCategory?): Boolean = when (category) {
    FlightCategory.MyFlights -> isMyFlight
    FlightCategory.PerLanding -> isPerLanding && !isMyAdHocFlight
    FlightCategory.AdHoc -> isMyAdHocFlight
    null -> isMyFlight || isPerLanding || isMyAdHocFlight
}

fun FlightListItem.isInFlightWindow(at: Instant): Boolean =
    overlapsFlightWindow(scheduledArrivalUtc, scheduledDepartureUtc, at)

fun FlightDetail.isInFlightWindow(at: Instant): Boolean =
    overlapsFlightWindow(scheduledArrivalUtc, scheduledDepartureUtc, at)

private fun overlapsFlightWindow(arrival: String, departure: String, at: Instant): Boolean = runCatching {
    val sta = Instant.parse(arrival)
    val std = Instant.parse(departure)
    !std.isBefore(sta) && !sta.isAfter(at.plus(FLIGHT_WINDOW_HALF)) && !std.isBefore(at.minus(FLIGHT_WINDOW_HALF))
}.getOrDefault(false)

internal val FLIGHT_WINDOW_HALF: Duration = Duration.ofHours(12)

@Serializable
internal data class FlightScope(
    val stationId: String,
    val asOfUtc: String,
    val windowStartUtc: String,
    val windowEndUtc: String,
) {
    fun isValid(): Boolean = stationId.isNotBlank() && runCatching {
        val at = Instant.parse(asOfUtc)
        Instant.parse(windowStartUtc) == at.minus(FLIGHT_WINDOW_HALF) &&
            Instant.parse(windowEndUtc) == at.plus(FLIGHT_WINDOW_HALF)
    }.getOrDefault(false)
}

internal fun FlightPage.scopeOrNull(): FlightScope? {
    val scope = FlightScope(stationId ?: return null, asOfUtc ?: return null,
        windowStartUtc ?: return null, windowEndUtc ?: return null)
    return scope.takeIf { it.isValid() }
}
