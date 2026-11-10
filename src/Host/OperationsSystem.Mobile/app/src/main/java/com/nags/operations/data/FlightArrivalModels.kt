package com.nags.operations.data

import java.time.OffsetDateTime
import java.time.LocalDateTime
import java.time.ZoneId
import java.util.Base64
import java.util.UUID
import kotlinx.serialization.Serializable

@Serializable
data class FlightArrivalSubmission(
    val id: String,
    val submittedByEmployee: AssignedEmployee,
    val ata: String,
    val aircraftTypeId: String,
    val aircraftTypeModel: String,
    val aircraftTailNumber: String,
    val customerWorkReferenceNumber: String? = null,
    val receivedByEmployee: AssignedEmployee,
    val remarks: String? = null,
    val isAccepted: Boolean,
    val createdAtUtc: String,
    val updatedAtUtc: String? = null,
) {
    fun input() = SaveFlightArrivalRequest(ata, aircraftTypeId, aircraftTailNumber, customerWorkReferenceNumber, remarks)
}

@Serializable
data class SaveFlightArrivalRequest(
    val ata: String,
    val aircraftTypeId: String,
    val aircraftTailNumber: String,
    val customerWorkReferenceNumber: String? = null,
    val remarks: String? = null,
    val acknowledgeConflictingSubmission: Boolean = false,
) {
    fun withoutAcknowledgement() = copy(acknowledgeConflictingSubmission = false)
    fun normalized() = copy(aircraftTailNumber = aircraftTailNumber.trim(),
        customerWorkReferenceNumber = customerWorkReferenceNumber?.trim()?.takeIf(String::isNotEmpty),
        remarks = remarks?.trim()?.takeIf(String::isNotEmpty))
}

@Serializable
data class FlightArrivalSaved(val submission: FlightArrivalSubmission, val rowVersion: String)

enum class ArrivalInputError { AtaRequired, AircraftRequired, TailRequired, VersionRequired }

/** Mirrors the arrival boundary, without imposing schedule bounds or invented text limits. */
fun SaveFlightArrivalRequest.validationErrors(rowVersion: String): Set<ArrivalInputError> = buildSet {
    val time = runCatching { OffsetDateTime.parse(ata) }.getOrNull()
    if (time == null || time.year !in 1..9999 || time.toInstant() == OffsetDateTime.parse("0001-01-01T00:00:00Z").toInstant())
        add(ArrivalInputError.AtaRequired)
    if (!validArrivalId(aircraftTypeId)) add(ArrivalInputError.AircraftRequired)
    if (aircraftTailNumber.isBlank()) add(ArrivalInputError.TailRequired)
    if (runCatching { Base64.getDecoder().decode(rowVersion).size }.getOrDefault(0) != 8)
        add(ArrivalInputError.VersionRequired)
}

internal fun validArrivalId(id: String?): Boolean = id != null &&
    Regex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}").matches(id) &&
    runCatching { UUID.fromString(id) != UUID(0, 0) }.getOrDefault(false)

/** Keep an unchanged instant during the repeated hour at the end of daylight saving. */
fun arrivalInputTime(local: String, original: String?, zone: ZoneId): String = runCatching {
    val value = LocalDateTime.parse(local)
    val previous = original?.let { OffsetDateTime.parse(it) }
    if (previous != null && previous.atZoneSameInstant(zone).toLocalDateTime() == value) previous.toInstant().toString()
    else {
        val offset = zone.rules.getValidOffsets(value).singleOrNull() ?: return@runCatching ""
        value.toInstant(offset).toString()
    }
}.getOrDefault("")

fun FlightDetail.visibleArrival(): FlightArrivalSubmission? = arrivalSubmissions.firstOrNull {
    it.submittedByEmployee.staffMemberId.equals(currentStaffMemberId, ignoreCase = true)
} ?: arrivalSubmissions.firstOrNull { it.isAccepted } ?: arrivalSubmissions.firstOrNull()

fun FlightDetail.arrivalEditable(): Boolean = canSaveArrival && validArrivalId(currentStaffMemberId) &&
    !hasOwnCancellation() && (arrivalSubmissions.isEmpty() || arrivalSubmissions.any {
        it.submittedByEmployee.staffMemberId.equals(currentStaffMemberId, ignoreCase = true)
    })
