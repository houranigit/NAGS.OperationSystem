package com.nags.operations.ui.flights

import com.nags.operations.data.FlightDetail
import com.nags.operations.data.db.entities.FlightArrivalPendingEntity
import com.nags.operations.data.db.entities.FlightDeparturePendingEntity
import com.nags.operations.data.visibleArrival
import com.nags.operations.data.visibleDeparture

/** Display progress comes from saved submissions and durable intent, never unsaved form input. */
internal enum class FlightProgressStatus {
    Draft,
    ArrivalSubmitted,
    ArrivalPendingSync,
    ArrivalNeedsReview,
    DepartureSubmitted,
    DeparturePendingSync,
    DepartureNeedsReview,
}

internal fun FlightDetail.progressStatus(
    pendingArrival: FlightArrivalPendingEntity? = null,
    pendingDeparture: FlightDeparturePendingEntity? = null,
): FlightProgressStatus {
    val localDeparture = pendingDeparture?.takeIf {
        it.flightId == id && !currentStaffMemberId.isNullOrBlank() && it.staffMemberId.equals(currentStaffMemberId, true)
    }
    if (localDeparture != null) return if (localDeparture.status == "pending") FlightProgressStatus.DeparturePendingSync
        else FlightProgressStatus.DepartureNeedsReview
    if (visibleDeparture() != null) return FlightProgressStatus.DepartureSubmitted
    val localArrival = pendingArrival?.takeIf { it.flightId == id && !currentStaffMemberId.isNullOrBlank() }
    if (localArrival != null) return if (localArrival.status == "pending") FlightProgressStatus.ArrivalPendingSync
        else FlightProgressStatus.ArrivalNeedsReview
    return if (visibleArrival() != null) FlightProgressStatus.ArrivalSubmitted else FlightProgressStatus.Draft
}
