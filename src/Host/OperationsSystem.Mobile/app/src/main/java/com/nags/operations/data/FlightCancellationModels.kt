package com.nags.operations.data

import kotlinx.serialization.Serializable

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
