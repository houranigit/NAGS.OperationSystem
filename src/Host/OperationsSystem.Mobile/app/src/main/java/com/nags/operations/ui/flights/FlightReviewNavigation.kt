package com.nags.operations.ui.flights

import com.nags.operations.data.FlightOperationReviewTarget
import com.nags.operations.data.db.entities.FlightArrivalPendingEntity
import com.nags.operations.data.db.entities.FlightDeparturePendingEntity
import com.nags.operations.data.db.entities.FlightServicePendingEntity
import com.nags.operations.data.db.entities.FlightTaskPendingEntity

/** Follow the form's physical section order; stable IDs select the exact failed entry. */
internal fun flightReviewTargets(
    arrival: FlightArrivalPendingEntity?,
    departure: FlightDeparturePendingEntity?,
    services: List<FlightServicePendingEntity>,
    tasks: List<FlightTaskPendingEntity>,
): List<FlightOperationReviewTarget> = buildList {
    arrival?.takeIf { it.status != "pending" }?.let { add(FlightOperationReviewTarget("arrival", changeId = it.changeId)) }
    services.filter { it.status != "pending" }.forEach { add(FlightOperationReviewTarget("service", it.serviceRecordId, it.changeId)) }
    tasks.filter { it.status != "pending" }.forEach { add(FlightOperationReviewTarget("task", it.taskRecordId, it.changeId)) }
    departure?.takeIf { it.status != "pending" }?.let { add(FlightOperationReviewTarget("departure", changeId = it.changeId)) }
}
