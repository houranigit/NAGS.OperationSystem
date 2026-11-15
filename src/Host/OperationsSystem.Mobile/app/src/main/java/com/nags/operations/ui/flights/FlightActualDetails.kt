package com.nags.operations.ui.flights

import com.nags.operations.data.AircraftTypeCatalogItem
import com.nags.operations.data.AssignedEmployee
import com.nags.operations.data.SaveFlightArrivalRequest
import com.nags.operations.data.SaveFlightDepartureRequest
import com.nags.operations.data.StaffCatalogItem
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.FlightArrivalSubmission
import com.nags.operations.data.FlightDepartureSubmission
import com.nags.operations.data.db.entities.FlightArrivalPendingEntity
import com.nags.operations.data.db.entities.FlightDeparturePendingEntity
import com.nags.operations.data.visibleArrival
import com.nags.operations.data.visibleDeparture
import java.time.OffsetDateTime
import java.util.Locale
import kotlinx.serialization.json.Json

private val actualDetailsJson = Json { ignoreUnknownKeys = true; isLenient = true }

/** Desired values decorate this exact recorded report; attribution and acceptance remain server-owned. */
internal fun FlightArrivalSubmission.withPendingCorrection(
    pending: FlightArrivalPendingEntity?,
    aircraftTypes: List<AircraftTypeCatalogItem> = emptyList(),
): FlightArrivalSubmission {
    val input = pending?.let { runCatching {
        actualDetailsJson.decodeFromString<SaveFlightArrivalRequest>(it.inputJson).also { value -> OffsetDateTime.parse(value.ata) }
    }.getOrNull() }?.takeIf { it.targetSubmissionId.equals(id, true) } ?: return this
    val model = aircraftTypes.firstOrNull { it.id.equals(input.aircraftTypeId, true) }?.model
        ?: aircraftTypeModel.takeIf { aircraftTypeId.equals(input.aircraftTypeId, true) }.orEmpty()
    return copy(ata = input.ata, aircraftTypeId = input.aircraftTypeId, aircraftTypeModel = model,
        aircraftTailNumber = input.aircraftTailNumber, customerWorkReferenceNumber = input.customerWorkReferenceNumber,
        remarks = input.remarks)
}

/** A local signature preview is loaded separately; this overlay never invents a new signature version. */
internal fun FlightDepartureSubmission.withPendingCorrection(pending: FlightDeparturePendingEntity?): FlightDepartureSubmission {
    val input = pending?.let { runCatching {
        actualDetailsJson.decodeFromString<SaveFlightDepartureRequest>(it.inputJson).also { value -> OffsetDateTime.parse(value.atd) }
    }.getOrNull() }?.takeIf { it.targetSubmissionId.equals(id, true) } ?: return this
    return copy(atd = input.atd, remarks = input.remarks)
}

/** Saved form cards show desired local values without borrowing another report's attribution. */
internal fun FlightDetail.formArrivalSummary(
    pending: FlightArrivalPendingEntity?,
    aircraftTypes: List<AircraftTypeCatalogItem> = emptyList(),
): FlightArrivalSubmission? {
    val row = pending?.takeIf { it.flightId == id && !currentStaffMemberId.isNullOrBlank() }
        ?: return visibleArrival()
    val input = runCatching {
        actualDetailsJson.decodeFromString<SaveFlightArrivalRequest>(row.inputJson).also { OffsetDateTime.parse(it.ata) }
    }.getOrNull() ?: return visibleArrival()
    val targetId = input.targetSubmissionId
    fun matches(original: FlightArrivalSubmission) = if (targetId == null)
        original.submittedByEmployee.staffMemberId.equals(currentStaffMemberId, true)
    else original.id.equals(targetId, true)
    val original = arrivalSubmissions.firstOrNull(::matches) ?: row.reviewedArrivalJson?.let { raw ->
        runCatching { actualDetailsJson.decodeFromString<FlightArrivalSubmission?>(raw)?.takeIf(::matches) }.getOrNull()
    }
    if (targetId != null && original == null) return visibleArrival()
    val model = aircraftTypes.firstOrNull { it.id.equals(input.aircraftTypeId, true) }?.model
        ?: original?.takeIf { it.aircraftTypeId.equals(input.aircraftTypeId, true) }?.aircraftTypeModel
        ?: aircraftTypeModel?.takeIf { aircraftTypeId.equals(input.aircraftTypeId, true) }.orEmpty()
    if (original != null) return original.copy(ata = input.ata, aircraftTypeId = input.aircraftTypeId,
        aircraftTypeModel = model, aircraftTailNumber = input.aircraftTailNumber,
        customerWorkReferenceNumber = input.customerWorkReferenceNumber, remarks = input.remarks)
    val actor = assignedEmployees.firstOrNull { it.staffMemberId.equals(currentStaffMemberId, true) }
        ?: AssignedEmployee(checkNotNull(currentStaffMemberId), "", "")
    return FlightArrivalSubmission("", actor, input.ata, input.aircraftTypeId, model, input.aircraftTailNumber,
        input.customerWorkReferenceNumber, actor, input.remarks, isAccepted = false, createdAtUtc = row.savedAtUtc)
}

/** An unsynchronized signature is visible while its original server version stays unchanged. */
internal fun FlightDetail.formDepartureSummary(pending: FlightDeparturePendingEntity?): FlightDepartureSubmission? {
    val row = pending?.takeIf { it.flightId == id && !currentStaffMemberId.isNullOrBlank() &&
        it.staffMemberId.equals(currentStaffMemberId, true) } ?: return visibleDeparture()
    val input = runCatching {
        actualDetailsJson.decodeFromString<SaveFlightDepartureRequest>(row.inputJson).also { OffsetDateTime.parse(it.atd) }
    }.getOrNull() ?: return visibleDeparture()
    val targetId = input.targetSubmissionId
    fun matches(original: FlightDepartureSubmission) = if (targetId == null)
        original.submittedByEmployee.staffMemberId.equals(currentStaffMemberId, true)
    else original.id.equals(targetId, true)
    val original = departureSubmissions.firstOrNull(::matches) ?: runCatching {
        actualDetailsJson.decodeFromString<List<FlightDepartureSubmission>>(row.reviewedDepartureJson).firstOrNull(::matches)
    }.getOrNull()
    if (targetId != null && original == null) return visibleDeparture()
    if (original != null) return original.copy(atd = input.atd, remarks = input.remarks,
        hasSignature = original.hasSignature || row.signatureFile != null)
    val actor = assignedEmployees.firstOrNull { it.staffMemberId.equals(currentStaffMemberId, true) }
        ?: AssignedEmployee(checkNotNull(currentStaffMemberId), "", "")
    return FlightDepartureSubmission("", actor, input.atd, actor, input.remarks,
        isAccepted = false, createdAtUtc = row.savedAtUtc, hasSignature = row.signatureFile != null)
}

/** Presentation only: local intent must never become an accepted submission or change a rowversion. */
internal data class FlightActualDetails(
    val aircraftModel: String?,
    val tailNumber: String?,
    val ata: String?,
    val atd: String?,
    val employeeNames: List<String>,
    val savedLocally: Boolean,
)

internal fun FlightDetail.actualDetails(
    pendingArrival: FlightArrivalPendingEntity? = null,
    pendingDeparture: FlightDeparturePendingEntity? = null,
    aircraftTypes: List<AircraftTypeCatalogItem> = emptyList(),
    stationStaff: List<StaffCatalogItem> = emptyList(),
    currentUserName: String? = null,
    youLabel: String,
): FlightActualDetails? {
    val localArrival = pendingArrival?.takeIf { it.flightId == id && !currentStaffMemberId.isNullOrBlank() }?.let {
        runCatching {
            actualDetailsJson.decodeFromString<SaveFlightArrivalRequest>(it.inputJson).also { input -> OffsetDateTime.parse(input.ata) }
        }.getOrNull()
    }
    val localDeparture = pendingDeparture?.takeIf {
        it.flightId == id && !currentStaffMemberId.isNullOrBlank() && it.staffMemberId.equals(currentStaffMemberId, true)
    }?.let {
        runCatching {
            actualDetailsJson.decodeFromString<SaveFlightDepartureRequest>(it.inputJson).also { input -> OffsetDateTime.parse(input.atd) }
        }.getOrNull()
    }
    val arrival = localArrival?.targetSubmissionId?.let { target ->
        arrivalSubmissions.firstOrNull { it.id.equals(target, true) }
            ?: pendingArrival.reviewedArrivalJson?.let { raw -> runCatching {
                actualDetailsJson.decodeFromString<FlightArrivalSubmission?>(raw)?.takeIf { it.id.equals(target, true) }
            }.getOrNull() }
    } ?: visibleArrival().takeIf { localArrival?.targetSubmissionId == null }
    val departure = localDeparture?.targetSubmissionId?.let { target ->
        departureSubmissions.firstOrNull { it.id.equals(target, true) }
            ?: pendingDeparture.reviewedDepartureJson.let { raw -> runCatching {
                actualDetailsJson.decodeFromString<List<FlightDepartureSubmission>>(raw).firstOrNull { it.id.equals(target, true) }
            }.getOrNull() }
    } ?: visibleDeparture().takeIf { localDeparture?.targetSubmissionId == null }
    val ata = localArrival?.ata ?: arrival?.ata
    val atd = localDeparture?.atd ?: departure?.atd
    if (ata == null && atd == null) return null

    fun actorName(staffId: String): String = stationStaff.firstOrNull {
        it.id.equals(staffId, true) && it.stationId.equals(stationId, true)
    }?.fullName?.takeIf { it.isNotBlank() }
        ?: (assignedEmployees + arrivalSubmissions.map { it.submittedByEmployee } + departureSubmissions.map { it.submittedByEmployee })
            .firstOrNull { it.staffMemberId.equals(staffId, true) && it.fullName.isNotBlank() }?.fullName
        ?: currentUserName?.takeIf { it.isNotBlank() }
        ?: youLabel

    val receiver = if (localArrival != null && localArrival.targetSubmissionId == null) checkNotNull(currentStaffMemberId).let { it to actorName(it) }
        else arrival?.receivedByEmployee?.let { it.staffMemberId to it.fullName }
    val handler = if (localDeparture != null && localDeparture.targetSubmissionId == null) checkNotNull(currentStaffMemberId).let { it to actorName(it) }
        else departure?.handledByEmployee?.let { it.staffMemberId to it.fullName }
    val model = if (localArrival != null) {
        aircraftTypes.firstOrNull { it.id.equals(localArrival.aircraftTypeId, true) }?.model
            ?: arrival?.takeIf { it.aircraftTypeId.equals(localArrival.aircraftTypeId, true) }?.aircraftTypeModel
            ?: arrivalSubmissions.firstOrNull { it.aircraftTypeId.equals(localArrival.aircraftTypeId, true) }?.aircraftTypeModel
            ?: aircraftTypeModel?.takeIf { aircraftTypeId.equals(localArrival.aircraftTypeId, true) }
    } else arrival?.aircraftTypeModel
    return FlightActualDetails(model, localArrival?.aircraftTailNumber ?: arrival?.aircraftTailNumber, ata, atd,
        listOfNotNull(receiver, handler).distinctBy { it.first.lowercase(Locale.ROOT) }.map { it.second },
        savedLocally = localArrival != null || localDeparture != null)
}
