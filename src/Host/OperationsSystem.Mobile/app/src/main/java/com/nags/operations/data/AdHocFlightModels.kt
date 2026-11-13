package com.nags.operations.data

import java.time.ZoneId
import java.util.Locale
import kotlinx.serialization.Serializable

@Serializable
data class AdHocFlightInput(val customerId: String, val flightNumber: String,
    val scheduledArrivalUtc: String, val scheduledDepartureUtc: String) {
    fun normalized() = copy(flightNumber = flightNumber.trim().uppercase(Locale.ROOT),
        scheduledArrivalUtc = runCatching { parseFlightInstant(scheduledArrivalUtc).toString() }.getOrDefault(scheduledArrivalUtc),
        scheduledDepartureUtc = runCatching { parseFlightInstant(scheduledDepartureUtc).toString() }.getOrDefault(scheduledDepartureUtc))
    fun validationMessage(): String? = when {
        !validArrivalId(customerId) -> "Select a customer."
        flightNumber.isBlank() -> "Enter a flight number."
        flightNumber.length > 12 -> "Flight number must be at most 12 characters."
        runCatching { parseFlightInstant(scheduledArrivalUtc) }.isFailure -> "Choose a valid scheduled arrival."
        runCatching { parseFlightInstant(scheduledDepartureUtc) }.isFailure -> "Choose a valid scheduled departure."
        parseFlightInstant(scheduledDepartureUtc) < parseFlightInstant(scheduledArrivalUtc) -> "Scheduled departure cannot be before scheduled arrival."
        else -> null
    }
}

/** Explicit Operations grants for a new flight; never inferred from another flight's assignment. */
@Serializable
data class AdHocFlightContext(val stationId: String, val stationIata: String, val stationName: String,
    val stationTimeZoneId: String, val currentStaffMemberId: String, val canCreate: Boolean,
    val canSaveArrival: Boolean, val canSaveDeparture: Boolean, val canSaveServices: Boolean,
    val canSaveTasks: Boolean, val allowedServiceIds: List<String>, val asOfUtc: String,
    val windowStartUtc: String, val windowEndUtc: String) {
    fun isValid() = validArrivalId(stationId) && validArrivalId(currentStaffMemberId) &&
        runCatching { ZoneId.of(stationTimeZoneId); FlightScope(stationId, asOfUtc, windowStartUtc, windowEndUtc).isValid() }.getOrDefault(false)
}

internal const val LOCAL_ADHOC_VERSION = "AAAAAAAAAAA="
internal const val ADHOC_OPERATION_TYPE_ID = "30000000-0000-0000-0000-000000000001"
