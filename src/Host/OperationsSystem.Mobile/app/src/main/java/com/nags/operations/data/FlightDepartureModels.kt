package com.nags.operations.data

import kotlinx.serialization.Serializable

@Serializable
data class FlightDepartureSubmission(
    val id: String,
    val submittedByEmployee: AssignedEmployee,
    val atd: String,
    val handledByEmployee: AssignedEmployee,
    val remarks: String? = null,
    val isAccepted: Boolean,
    val createdAtUtc: String,
    val updatedAtUtc: String? = null,
    val signatureVersion: String? = null,
)

fun FlightDetail.visibleDeparture(): FlightDepartureSubmission? = departureSubmissions.firstOrNull {
    it.submittedByEmployee.staffMemberId.equals(currentStaffMemberId, ignoreCase = true)
} ?: departureSubmissions.firstOrNull { it.isAccepted } ?: departureSubmissions.firstOrNull()

@Serializable
data class SaveFlightDepartureRequest(
    val atd: String,
    val signatureContent: String? = null,
    val signatureSignedAtUtc: String? = null,
    val remarks: String? = null,
    val acknowledgeConflictingSubmission: Boolean = false,
    val targetSubmissionId: String? = null,
) {
    fun withoutAcknowledgement() = copy(acknowledgeConflictingSubmission = false)
    fun normalized() = copy(remarks = remarks?.trim()?.takeIf(String::isNotEmpty))
}

@Serializable
data class FlightDepartureSaved(val submission: FlightDepartureSubmission, val rowVersion: String)

fun FlightDetail.departureEditable(): Boolean = canSaveDeparture && validArrivalId(currentStaffMemberId) &&
    !hasOwnCancellation() && (departureSubmissions.isEmpty() || departureSubmissions.any {
        it.submittedByEmployee.staffMemberId.equals(currentStaffMemberId, ignoreCase = true)
    })

/** A targeted correction never creates a report on behalf of the signed-in employee. */
fun FlightDetail.canUpdateDepartureSubmission(submissionId: String): Boolean =
    canSaveDeparture && validArrivalId(currentStaffMemberId) && !hasOwnCancellation() &&
        departureSubmissions.any { it.id.equals(submissionId, true) &&
            (it.submittedByEmployee.staffMemberId.equals(currentStaffMemberId, true) ||
                assignedEmployees.any { employee -> employee.staffMemberId.equals(currentStaffMemberId, true) }) }
