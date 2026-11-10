package com.nags.operations.ui.flights

import com.nags.operations.data.AircraftTypeCatalogItem
import com.nags.operations.data.SaveFlightArrivalRequest
import com.nags.operations.data.SaveFlightDepartureRequest
import com.nags.operations.data.StaffCatalogItem
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.db.entities.FlightArrivalPendingEntity
import com.nags.operations.data.db.entities.FlightDeparturePendingEntity
import com.nags.operations.data.visibleArrival
import com.nags.operations.data.visibleDeparture
import java.time.OffsetDateTime
import java.util.Locale
import kotlinx.serialization.json.Json

private val actualDetailsJson = Json { ignoreUnknownKeys = true; isLenient = true }

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
    val arrival = visibleArrival()
    val departure = visibleDeparture()
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

    val receiver = if (localArrival != null) checkNotNull(currentStaffMemberId).let { it to actorName(it) }
        else arrival?.receivedByEmployee?.let { it.staffMemberId to it.fullName }
    val handler = if (localDeparture != null) checkNotNull(currentStaffMemberId).let { it to actorName(it) }
        else departure?.handledByEmployee?.let { it.staffMemberId to it.fullName }
    val model = if (localArrival != null) {
        aircraftTypes.firstOrNull { it.id.equals(localArrival.aircraftTypeId, true) }?.model
            ?: arrivalSubmissions.firstOrNull { it.aircraftTypeId.equals(localArrival.aircraftTypeId, true) }?.aircraftTypeModel
            ?: aircraftTypeModel?.takeIf { aircraftTypeId.equals(localArrival.aircraftTypeId, true) }
    } else arrival?.aircraftTypeModel
    return FlightActualDetails(model, localArrival?.aircraftTailNumber ?: arrival?.aircraftTailNumber, ata, atd,
        listOfNotNull(receiver, handler).distinctBy { it.first.lowercase(Locale.ROOT) }.map { it.second },
        savedLocally = localArrival != null || localDeparture != null)
}
