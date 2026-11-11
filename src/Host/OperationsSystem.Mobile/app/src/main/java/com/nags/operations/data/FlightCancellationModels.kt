package com.nags.operations.data

import com.nags.operations.data.db.entities.FlightCancellationPendingEntity
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

@Serializable
data class FlightCancellationSubmission(
    val id: String,
    val submittedByEmployee: AssignedEmployee,
    val canceledAtUtc: String,
    val remarks: String? = null,
    val isAccepted: Boolean,
    val createdAtUtc: String,
    val updatedAtUtc: String? = null,
)

@Serializable
data class SaveFlightCancellationRequest(
    val canceledAtUtc: String,
    val remarks: String? = null,
    val acknowledgeConflictingSubmission: Boolean = false,
) {
    fun normalized() = copy(remarks = remarks?.trim()?.takeIf(String::isNotEmpty))
    fun withoutAcknowledgement() = copy(acknowledgeConflictingSubmission = false)
}

@Serializable
data class FlightCancellationSaved(val submission: FlightCancellationSubmission, val rowVersion: String)

@Serializable
data class FlightCancellationReverted(val cancellationSubmissions: List<FlightCancellationSubmission>, val rowVersion: String)

/** A revert may still represent the immutable save whose server outcome is unknown. */
internal fun FlightCancellationPendingEntity.cancellationAtUtcForCard(): String? {
    val savedInput = when {
        operation == "save" -> inputJson
        attemptOperation == "save" -> attemptInputJson
        else -> null
    } ?: return null
    return runCatching {
        Json.decodeFromString<SaveFlightCancellationRequest>(savedInput).canceledAtUtc.also { parseFlightInstant(it) }
    }.getOrNull()
}

fun FlightDetail.ownCancellation(): FlightCancellationSubmission? = cancellationSubmissions.firstOrNull {
    !currentStaffMemberId.isNullOrBlank() && it.submittedByEmployee.staffMemberId.equals(currentStaffMemberId, true)
}

/** A legacy cache without submission attribution must be refreshed before allowing work. */
fun FlightDetail.hasOwnCancellation(): Boolean = ownCancellation() != null ||
    (hasCancellationSubmission && cancellationSubmissions.isEmpty())

fun FlightDetail.hasOwnWork(): Boolean = !currentStaffMemberId.isNullOrBlank() &&
    (arrivalSubmissions.any { it.submittedByEmployee.staffMemberId.equals(currentStaffMemberId, true) } ||
        departureSubmissions.any { it.submittedByEmployee.staffMemberId.equals(currentStaffMemberId, true) })

fun FlightDetail.cancellationEditable(): Boolean = canSaveCancellation && validArrivalId(currentStaffMemberId) && !hasOwnWork()
