package com.nags.operations.data

import java.time.OffsetDateTime
import java.util.Base64
import kotlinx.serialization.Serializable
import kotlinx.serialization.Transient

@Serializable
data class FlightServiceAttachmentInput(val id: String, val kind: Int, val fileName: String,
    val contentType: String, val content: String? = null, @Transient val contentUri: String? = null)

@Serializable
data class FlightServiceAttachmentRecord(val id: String, val kind: Int, val fileName: String,
    val contentType: String, val sizeBytes: Long, val uploadedByUserId: String = "", val uploadedAtUtc: String = "")

@Serializable
data class SaveFlightServicePerformerRequest(val id: String, val staffMemberId: String, val startUtc: String, val endUtc: String? = null)

@Serializable
data class SaveFlightServiceRequest(
    val serviceId: String,
    val startUtc: String,
    val endUtc: String? = null,
    val customerReferenceNumber: String? = null,
    val notes: String? = null,
    val performers: List<SaveFlightServicePerformerRequest> = emptyList(),
    val attachments: List<FlightServiceAttachmentInput>? = null,
) {
    fun normalized() = copy(startUtc = canonicalServiceTime(startUtc), endUtc = endUtc?.let(::canonicalServiceTime),
        customerReferenceNumber = customerReferenceNumber?.trim()?.takeIf(String::isNotEmpty),
        notes = notes?.trim()?.takeIf(String::isNotEmpty), performers = performers.map {
            it.copy(startUtc = canonicalServiceTime(it.startUtc), endUtc = it.endUtc?.let(::canonicalServiceTime))
        }.sortedBy { it.id },
        attachments = attachments?.map { it.copy(fileName = it.fileName.replace('\\', '/').substringAfterLast('/').take(255),
            contentType = canonicalServiceMime(it.contentType)) }?.sortedBy { it.id })

    fun validationErrors(rowVersion: String, actualArrivalUtc: String? = null, actualDepartureUtc: String? = null,
        allowedServiceIds: Set<String>? = null): Set<String> = buildSet {
        if (!validArrivalId(serviceId)) add("serviceId")
        if (allowedServiceIds != null && allowedServiceIds.none { it.equals(serviceId, true) }) add("serviceId")
        if (serviceId.equals("40000000-0000-0000-0000-000000000003", true) && notes.isNullOrBlank()) add("notes")
        if (runCatching { Base64.getDecoder().decode(rowVersion).size }.getOrDefault(0) != 8) add("rowVersion")
        if ((customerReferenceNumber?.trim()?.length ?: 0) > 200) add("customerReferenceNumber")
        if ((notes?.trim()?.length ?: 0) > 2000) add("notes")
        val start = serviceInstant(startUtc)
        val end = endUtc?.let(::serviceInstant)
        if (start == null) add("startUtc")
        if (endUtc != null && (end == null || start != null && end < start)) add("endUtc")
        val arrival = actualArrivalUtc?.let(::serviceInstant)
        val departure = actualDepartureUtc?.let(::serviceInstant)
        if (start != null && (arrival != null && start < arrival || departure != null && start > departure)) add("startUtc")
        if (end != null && (arrival != null && end < arrival || departure != null && end > departure)) add("endUtc")
        if (performers.map { it.id.lowercase() }.distinct().size != performers.size ||
            performers.map { it.staffMemberId.lowercase() }.distinct().size != performers.size || performers.isEmpty()) add("performers")
        performers.forEach { performer ->
            val prefix = "performers.${performer.id}."
            val from = serviceInstant(performer.startUtc)
            val to = performer.endUtc?.let(::serviceInstant)
            if (!validArrivalId(performer.id)) add(prefix + "id")
            if (!validArrivalId(performer.staffMemberId)) add(prefix + "staffMemberId")
            if (from == null || start != null && from < start || end != null && from > end ||
                arrival != null && from < arrival || departure != null && from > departure)
                add(prefix + "startUtc")
            if (performer.endUtc != null && (to == null || from != null && to < from ||
                    end != null && to > end || arrival != null && to < arrival || departure != null && to > departure))
                add(prefix + "endUtc")
        }
        if (attachments != null && (attachments.size > 10 || attachments.map { it.id.lowercase() }.distinct().size != attachments.size ||
            attachments.any { !validArrivalId(it.id) || it.kind !in 0..2 || it.fileName.isBlank() || it.contentType.isBlank() || it.contentType.length > 100 }))
            add("attachments")
    }
}

private fun serviceInstant(value: String) = runCatching { OffsetDateTime.parse(value).takeIf {
    it.year in 1..9999 && it.toInstant() != java.time.Instant.parse("0001-01-01T00:00:00Z")
}?.toInstant() }.getOrNull()
private fun canonicalServiceTime(value: String) = runCatching { OffsetDateTime.parse(value).toInstant().toString() }.getOrDefault(value)
private fun canonicalServiceMime(value: String) = value.trim().lowercase().let { if (it == "audio/x-m4a") "audio/mp4" else it }

@Serializable
data class FlightServicePerformerRecord(val id: String, val staffMemberId: String, val fullName: String,
    val employeeId: String, val startUtc: String, val endUtc: String? = null)

@Serializable
data class FlightServiceRecord(
    val id: String, val serviceId: String, val serviceName: String, val startUtc: String,
    val endUtc: String? = null, val customerReferenceNumber: String? = null, val notes: String? = null,
    val createdByUserId: String, val endedByUserId: String? = null, val createdAtUtc: String,
    val updatedAtUtc: String? = null, val updatedByUserId: String? = null,
    val performers: List<FlightServicePerformerRecord> = emptyList(), val canEdit: Boolean = false,
    val canSetEnd: Boolean = false, val attachments: List<FlightServiceAttachmentRecord> = emptyList(),
) {
    fun input() = SaveFlightServiceRequest(serviceId, startUtc, endUtc, customerReferenceNumber, notes,
        performers.map { SaveFlightServicePerformerRequest(it.id, it.staffMemberId, it.startUtc, it.endUtc) },
        attachments.map { FlightServiceAttachmentInput(it.id, it.kind, it.fileName, it.contentType) }).normalized()
}

@Serializable data class FlightServiceSaved(val service: FlightServiceRecord, val rowVersion: String,
    val activeWorkCount: Int? = null, val completedWorkCount: Int? = null)
@Serializable data class FlightServiceDeleted(val id: String, val rowVersion: String,
    val activeWorkCount: Int? = null, val completedWorkCount: Int? = null)

/** Null input removes this exact stable record, never another service occurrence. */
data class FlightServiceChange(val serviceRecordId: String, val input: SaveFlightServiceRequest?)

/** Opaque editor state preserves unstarted work, task drafts, and explicit planned-row removals. */
data class FlightServiceFormDraft(val json: String, val attachments: List<FlightServiceFormDraftAttachment> = emptyList())
data class FlightServiceFormDraftAttachment(val serviceRecordId: String, val attachment: FlightServiceAttachmentInput)
data class FlightServiceActualBounds(val arrivalUtc: String?, val departureUtc: String?)
