package com.nags.operations.data

import java.time.OffsetDateTime
import java.util.Base64
import kotlinx.serialization.Serializable

@Serializable
data class FlightReturnToRampRecord(
    val id: String, val sequence: Int, val arrivedAtUtc: String,
    val receivedByStaffMemberId: String, val receivedByFullName: String,
    val arrivalRemarks: String? = null, val departedAtUtc: String? = null,
    val departureRemarks: String? = null, val handledByStaffMemberId: String? = null,
    val handledByFullName: String? = null, val hasSignature: Boolean = false,
    val signatureSignedAtUtc: String? = null, val createdByUserId: String, val createdAtUtc: String,
    val updatedByUserId: String? = null, val updatedAtUtc: String? = null,
    val createdByDisplayName: String? = null, val updatedByDisplayName: String? = null,
    val canEdit: Boolean = false, val isOpen: Boolean = departedAtUtc == null,
    val services: List<FlightServiceRecord> = emptyList(), val tasks: List<FlightTaskRecord> = emptyList(),
    @kotlinx.serialization.Transient val localStatus: String? = null,
    @kotlinx.serialization.Transient val localSavedAtUtc: String? = null,
) {
    fun input() = SaveFlightReturnToRampRequest(
        FlightReturnToRampArrivalInput(arrivedAtUtc, arrivalRemarks),
        departedAtUtc?.let { FlightReturnToRampDepartureInput(it, departureRemarks) },
        services.map { FlightReturnToRampServiceInput(it.id, it.input()) },
        tasks.map { FlightReturnToRampTaskInput(it.id, it.input()) },
    )
    val signatureVersion: String get() = updatedAtUtc ?: createdAtUtc
}

@Serializable data class FlightReturnToRampArrivalInput(val arrivedAtUtc: String, val remarks: String? = null)
@Serializable data class FlightReturnToRampDepartureInput(
    val departedAtUtc: String, val remarks: String? = null, val signatureContent: String? = null,
    val signatureFileName: String? = null, val signatureContentType: String? = null,
    val signatureSignedAtUtc: String? = null, val removeSignature: Boolean = false,
)
@Serializable data class FlightReturnToRampServiceInput(val recordId: String, val input: SaveFlightServiceRequest)
@Serializable data class FlightReturnToRampTaskInput(val recordId: String, val input: SaveFlightTaskRequest)
@Serializable data class SaveFlightReturnToRampRequest(
    val arrival: FlightReturnToRampArrivalInput,
    val departure: FlightReturnToRampDepartureInput? = null,
    val services: List<FlightReturnToRampServiceInput> = emptyList(),
    val tasks: List<FlightReturnToRampTaskInput> = emptyList(),
) {
    fun normalized() = copy(arrival = arrival.copy(arrivedAtUtc = canonicalRtrTime(arrival.arrivedAtUtc),
        remarks = arrival.remarks?.trim()?.takeIf(String::isNotEmpty)),
        departure = departure?.let { it.copy(departedAtUtc = canonicalRtrTime(it.departedAtUtc), remarks = it.remarks?.trim()?.takeIf(String::isNotEmpty),
            signatureFileName = if (it.signatureContent != null) it.signatureFileName ?: "signature.png" else it.signatureFileName,
            signatureContentType = if (it.signatureContent != null) it.signatureContentType ?: "image/png" else it.signatureContentType) },
        services = services.map { it.copy(input = it.input.normalized()) }.sortedBy { it.recordId },
        tasks = tasks.map { it.copy(input = it.input.normalized()) }.sortedBy { it.recordId })

    fun validationErrors(rowVersion: String, allowedServiceIds: Set<String>? = null,
        resourceCatalogs: FlightTaskResourceCatalogs? = null, baseline: FlightReturnToRampRecord? = null): Set<String> = buildSet {
        if (runCatching { Base64.getDecoder().decode(rowVersion).size }.getOrDefault(0) != 8) add("rowVersion")
        val arrived = rtrInstant(arrival.arrivedAtUtc)
        if (arrived == null) add("arrival.arrivedAtUtc")
        if ((arrival.remarks?.length ?: 0) > 2000) add("arrival.remarks")
        val departed = departure?.departedAtUtc?.let(::rtrInstant)
        if (departure != null && (departed == null || arrived != null && departed < arrived)) add("departure.departedAtUtc")
        if ((departure?.remarks?.length ?: 0) > 2000) add("departure.remarks")
        if (departure?.signatureContent != null && (departure.removeSignature || departure.signatureSignedAtUtc?.let(::rtrInstant) == null)) add("departure.signature")
        if (departure != null && services.isEmpty() && tasks.isEmpty()) add("work")
        if (services.map { it.recordId.lowercase() }.distinct().size != services.size) add("services")
        if (tasks.map { it.recordId.lowercase() }.distinct().size != tasks.size) add("tasks")
        services.forEach { record ->
            if (!validArrivalId(record.recordId)) add("services.${record.recordId}.id")
            val retained = baseline?.services?.firstOrNull { it.id == record.recordId }
            val eligibility = if (retained?.input() == record.input.normalized()) null else allowedServiceIds
            record.input.validationErrors(rowVersion, arrival.arrivedAtUtc, departure?.departedAtUtc, eligibility,
                requireClosedPerformers = departure != null).forEach { add("services.${record.recordId}.$it") }
            if (departure != null && record.input.endUtc == null) add("services.${record.recordId}.endUtc")
        }
        tasks.forEach { record ->
            if (!validArrivalId(record.recordId)) add("tasks.${record.recordId}.id")
            record.input.validationErrors(rowVersion, arrival.arrivedAtUtc, departure?.departedAtUtc, resourceCatalogs,
                baseline?.tasks?.firstOrNull { it.id == record.recordId }, requireClosedChildren = departure != null)
                .forEach { add("tasks.${record.recordId}.$it") }
            if (departure != null && record.input.endUtc == null) add("tasks.${record.recordId}.endUtc")
        }
    }
}

fun FlightDetail.canAddReturnToRamp(): Boolean = canSaveReturnsToRamp && departureSubmissions.isNotEmpty() &&
    !hasCancellationSubmission && returnToRamps.none { it.isOpen }

/** Latest occurrence owns the card's work summary; normal work remains its separate initial cycle. */
fun FlightListItem.withReturnToRampReports(detail: FlightDetail): FlightListItem {
    val latest = detail.returnToRamps.maxByOrNull { it.sequence }
    val services = latest?.services ?: detail.services
    val tasks = latest?.tasks ?: detail.tasks
    return copy(hasOpenReturnToRamp = detail.returnToRamps.any { it.isOpen }, latestReturnToRampSequence = latest?.sequence,
        activeWorkCount = services.count { it.endUtc == null } + tasks.count { it.endUtc == null },
        completedWorkCount = services.count { it.endUtc != null } + tasks.count { it.endUtc != null })
}
private fun canonicalRtrTime(value: String) = runCatching { OffsetDateTime.parse(value).toInstant().toString() }.getOrDefault(value)
private fun rtrInstant(value: String) = runCatching { OffsetDateTime.parse(value).takeIf { it.year in 1..9999 }?.toInstant() }.getOrNull()
