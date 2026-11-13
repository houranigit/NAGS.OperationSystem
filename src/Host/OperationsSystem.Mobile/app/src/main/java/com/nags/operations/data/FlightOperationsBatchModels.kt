package com.nags.operations.data

import com.nags.operations.data.db.entities.*
import kotlinx.serialization.Serializable

@Serializable
data class FlightServiceBatchChange(val recordId: String, val input: SaveFlightServiceRequest? = null)
@Serializable
data class FlightTaskBatchChange(val recordId: String, val input: SaveFlightTaskRequest? = null)
@Serializable
data class SaveFlightOperationsBatchRequest(
    val arrival: SaveFlightArrivalRequest? = null,
    val departure: SaveFlightDepartureRequest? = null,
    val serviceChanges: List<FlightServiceBatchChange> = emptyList(),
    val taskChanges: List<FlightTaskBatchChange> = emptyList(),
)

/** Private-file references and reviewed records travel with both desired and immutable attempts. */
@Serializable
data class FlightOperationsBatchInput(
    val arrival: FlightArrivalPendingEntity? = null,
    val departure: FlightDeparturePendingEntity? = null,
    val services: List<FlightServicePendingEntity> = emptyList(),
    val tasks: List<FlightTaskPendingEntity> = emptyList(),
) {
    fun isEmpty() = arrival == null && departure == null && services.isEmpty() && tasks.isEmpty()
    fun contains(kind: String, id: String? = null) = when (kind) {
        "arrival" -> arrival != null
        "departure" -> departure != null
        "service" -> services.any { it.serviceRecordId == id }
        "task" -> tasks.any { it.taskRecordId == id }
        else -> false
    }
    fun versions() = listOfNotNull(arrival?.rowVersion, departure?.rowVersion) + services.map { it.rowVersion } + tasks.map { it.rowVersion }
}

object FlightOperationsBatchLimits {
    const val MAX_INLINE_BYTES = 100L * 1024 * 1024
    const val MAX_JSON_BYTES = 150L * 1024 * 1024
}
