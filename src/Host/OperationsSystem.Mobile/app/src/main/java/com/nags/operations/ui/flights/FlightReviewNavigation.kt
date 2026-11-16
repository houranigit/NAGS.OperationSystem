package com.nags.operations.ui.flights

import com.nags.operations.data.FlightOperationReviewTarget
import com.nags.operations.data.db.entities.FlightArrivalPendingEntity
import com.nags.operations.data.db.entities.FlightDeparturePendingEntity
import com.nags.operations.data.db.entities.FlightReturnToRampPendingEntity
import com.nags.operations.data.db.entities.FlightServicePendingEntity
import com.nags.operations.data.db.entities.FlightTaskPendingEntity
import kotlinx.serialization.json.Json

/** Follow the form's physical section order; stable IDs select the exact failed entry. */
internal fun flightReviewTargets(
    arrival: FlightArrivalPendingEntity?,
    departure: FlightDeparturePendingEntity?,
    services: List<FlightServicePendingEntity>,
    tasks: List<FlightTaskPendingEntity>,
    returns: List<FlightReturnToRampPendingEntity> = emptyList(),
): List<FlightOperationReviewTarget> = buildList {
    arrival?.takeIf { it.status != "pending" }?.let { add(FlightOperationReviewTarget("arrival", changeId = it.changeId)) }
    services.filter { it.status != "pending" }.forEach { add(FlightOperationReviewTarget("service", it.serviceRecordId, it.changeId)) }
    tasks.filter { it.status != "pending" }.forEach { add(FlightOperationReviewTarget("task", it.taskRecordId, it.changeId)) }
    departure?.takeIf { it.status != "pending" }?.let { add(FlightOperationReviewTarget("departure", changeId = it.changeId)) }
    returns.filter { it.status != "pending" }.inOccurrenceOrder().forEach {
        add(FlightOperationReviewTarget("returns-to-ramp", it.occurrenceId, it.changeId))
    }
}

/** Normal queued work follows the same physical order without entering Review or changing its source. */
internal fun flightPendingTargets(
    arrival: FlightArrivalPendingEntity?,
    departure: FlightDeparturePendingEntity?,
    services: List<FlightServicePendingEntity>,
    tasks: List<FlightTaskPendingEntity>,
    returns: List<FlightReturnToRampPendingEntity> = emptyList(),
): List<FlightOperationReviewTarget> = buildList {
    arrival?.takeIf { it.status == "pending" }?.let { add(FlightOperationReviewTarget("arrival", changeId = it.changeId)) }
    services.filter { it.status == "pending" }.forEach { add(FlightOperationReviewTarget("service", it.serviceRecordId, it.changeId)) }
    tasks.filter { it.status == "pending" }.forEach { add(FlightOperationReviewTarget("task", it.taskRecordId, it.changeId)) }
    departure?.takeIf { it.status == "pending" }?.let { add(FlightOperationReviewTarget("departure", changeId = it.changeId)) }
    returns.filter { it.status == "pending" }.inOccurrenceOrder().forEach {
        add(FlightOperationReviewTarget("returns-to-ramp", it.occurrenceId, it.changeId))
    }
}

private fun List<FlightReturnToRampPendingEntity>.inOccurrenceOrder(): List<FlightReturnToRampPendingEntity> =
    sortedWith(compareBy<FlightReturnToRampPendingEntity> {
        runCatching { it.preview(Json).sequence }.getOrDefault(Int.MAX_VALUE)
    }.thenBy { it.occurrenceId })

internal fun FlightOperationReviewTarget.navigationKey(): String = when (kind) {
    "service", "task", "returns-to-ramp" -> "$kind:${checkNotNull(recordId)}"
    else -> kind
}
