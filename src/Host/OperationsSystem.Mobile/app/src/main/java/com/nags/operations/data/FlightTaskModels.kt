package com.nags.operations.data

import java.math.BigDecimal
import java.time.OffsetDateTime
import java.util.Base64
import java.util.Locale
import kotlinx.serialization.Serializable

@Serializable
data class SaveFlightTaskPerformerRequest(val id: String, val staffMemberId: String, val startUtc: String,
    val endUtc: String? = null, val notes: String? = null)

/** Quantity uses a decimal string so the wire never rounds decimal(18,2) through a double. */
@Serializable
data class SaveFlightTaskResourceRequest(val id: String, val resourceId: String, val quantity: String? = null,
    val startUtc: String? = null, val endUtc: String? = null, val notes: String? = null)

data class FlightTaskResourceCatalogs(val tools: Map<String, Int> = emptyMap(), val materials: Map<String, Int> = emptyMap(),
    val generalSupports: Map<String, Int> = emptyMap())

fun ResourceCatalogItem.taskCalculationType(): Int? = when (calculationType.lowercase()) {
    "quantity", "0" -> 0
    "duration", "1" -> 1
    else -> null
}

@Serializable
data class SaveFlightTaskRequest(
    val taskType: Int,
    val startUtc: String,
    val endUtc: String? = null,
    val ataChapterId: String? = null,
    val customerReferenceNumber: String? = null,
    val notes: String? = null,
    val performers: List<SaveFlightTaskPerformerRequest> = emptyList(),
    val tools: List<SaveFlightTaskResourceRequest> = emptyList(),
    val materials: List<SaveFlightTaskResourceRequest> = emptyList(),
    val generalSupports: List<SaveFlightTaskResourceRequest> = emptyList(),
    val attachments: List<FlightServiceAttachmentInput>? = null,
) {
    fun normalized(): SaveFlightTaskRequest {
        fun optional(value: String?) = value?.trim()?.takeIf(String::isNotEmpty)
        fun resources(items: List<SaveFlightTaskResourceRequest>) = items.map { it.copy(
            quantity = it.quantity?.let(::canonicalTaskQuantity), startUtc = it.startUtc?.let(::canonicalTaskTime),
            endUtc = it.endUtc?.let(::canonicalTaskTime), notes = optional(it.notes)) }.sortedBy { it.id }
        return copy(startUtc = canonicalTaskTime(startUtc), endUtc = endUtc?.let(::canonicalTaskTime),
            customerReferenceNumber = optional(customerReferenceNumber)?.uppercase(Locale.ROOT), notes = optional(notes),
            performers = performers.map { it.copy(startUtc = canonicalTaskTime(it.startUtc), endUtc = it.endUtc?.let(::canonicalTaskTime),
                notes = optional(it.notes)) }.sortedBy { it.id }, tools = resources(tools), materials = resources(materials),
            generalSupports = resources(generalSupports), attachments = attachments?.map { it.copy(
                fileName = it.fileName.replace('\\', '/').substringAfterLast('/').take(255),
                contentType = it.contentType.trim().lowercase().let { mime -> if (mime == "audio/x-m4a") "audio/mp4" else mime }) }?.sortedBy { it.id })
    }

    fun validationErrors(rowVersion: String, actualArrivalUtc: String? = null, actualDepartureUtc: String? = null,
        resourceCatalogs: FlightTaskResourceCatalogs? = null, baseline: FlightTaskRecord? = null,
        requireClosedChildren: Boolean = true): Set<String> = buildSet {
        if (taskType !in 0..1) add("taskType")
        if (ataChapterId != null && !validArrivalId(ataChapterId)) add("ataChapterId")
        if (runCatching { Base64.getDecoder().decode(rowVersion).size }.getOrDefault(0) != 8) add("rowVersion")
        if ((customerReferenceNumber?.trim()?.length ?: 0) > 200) add("customerReferenceNumber")
        if ((notes?.trim()?.length ?: 0) > 2000) add("notes")
        val start = taskInstant(startUtc); val end = endUtc?.let(::taskInstant)
        val arrival = actualArrivalUtc?.let(::taskInstant); val departure = actualDepartureUtc?.let(::taskInstant)
        fun outOfActuals(value: java.time.Instant?) = value != null &&
            (arrival != null && value < arrival || departure != null && value > departure)
        if (start == null || outOfActuals(start)) add("startUtc")
        if (endUtc != null && (end == null || start != null && end < start || outOfActuals(end))) add("endUtc")
        if (performers.isEmpty() || performers.map { it.id.lowercase() }.distinct().size != performers.size ||
            performers.map { it.staffMemberId.lowercase() }.distinct().size != performers.size) add("performers")
        val completionRequired = requireClosedChildren && (endUtc != null || departure != null)
        if (completionRequired && (endUtc == null || performers.any { it.endUtc == null })) add("endUtc")
        performers.forEach { person ->
            val key = "performers.${person.id}."
            if (!validArrivalId(person.id)) add(key + "id")
            if (!validArrivalId(person.staffMemberId)) add(key + "staffMemberId")
            if ((person.notes?.trim()?.length ?: 0) > 2000) add(key + "notes")
            val from = taskInstant(person.startUtc); val to = person.endUtc?.let(::taskInstant)
            if (from == null || start != null && from < start || end != null && from > end || outOfActuals(from)) add(key + "startUtc")
            if (person.endUtc != null && (to == null || from != null && to < from || end != null && to > end || outOfActuals(to))) add(key + "endUtc")
            if (completionRequired && person.endUtc == null) add(key + "endUtc")
        }
        fun resources(kind: String, values: List<SaveFlightTaskResourceRequest>, types: Map<String, Int>?, saved: List<FlightTaskResourceRecord>, unknown: String) {
            if (values.map { it.id.lowercase() }.distinct().size != values.size ||
                values.map { it.resourceId.lowercase() }.distinct().size != values.size) add(kind)
            values.forEach { item ->
                val key = "$kind.${item.id}."
                if (!validArrivalId(item.id)) add(key + "id")
                if (!validArrivalId(item.resourceId)) add(key + "resourceId")
                if ((item.notes?.trim()?.length ?: 0) > 2000 || item.resourceId.equals(unknown, true) && item.notes.isNullOrBlank()) add(key + "notes")
                val retained = saved.firstOrNull { it.id == item.id && it.resourceId.equals(item.resourceId, true) }
                val type = retained?.calculationType ?: types?.entries?.firstOrNull { it.key.equals(item.resourceId, true) }?.value
                    ?: if (types == null) (if (item.quantity != null) 0 else 1) else null
                when (type) {
                    0 -> {
                        val quantity = item.quantity?.takeIf { it.length <= 128 }?.let { runCatching { BigDecimal(it) }.getOrNull() }
                        if (quantity == null || quantity <= BigDecimal.ZERO || quantity > MAX_TASK_QUANTITY || quantity.stripTrailingZeros().scale() > 2)
                            add(key + "quantity")
                        if (item.startUtc != null) add(key + "startUtc")
                        if (item.endUtc != null) add(key + "endUtc")
                    }
                    1 -> {
                        if (item.quantity != null) add(key + "quantity")
                        if (completionRequired && item.endUtc == null) {
                            add("endUtc")
                            add(key + "endUtc")
                        }
                        val from = item.startUtc?.let(::taskInstant); val to = item.endUtc?.let(::taskInstant)
                        if (from == null || start != null && from < start) add(key + "startUtc")
                        if (item.endUtc != null && (to == null || from != null && to < from || end != null && to > end)) add(key + "endUtc")
                    }
                    else -> add(key + "resourceId")
                }
            }
        }
        resources("tools", tools, resourceCatalogs?.tools, baseline?.tools.orEmpty(), "60000000-0000-0000-0000-000000000001")
        resources("materials", materials, resourceCatalogs?.materials, baseline?.materials.orEmpty(), "70000000-0000-0000-0000-000000000001")
        resources("generalSupports", generalSupports, resourceCatalogs?.generalSupports, baseline?.generalSupports.orEmpty(), "80000000-0000-0000-0000-000000000001")
        if (attachments != null && (attachments.size > 10 || attachments.map { it.id.lowercase() }.distinct().size != attachments.size ||
            attachments.any { !validArrivalId(it.id) || it.kind !in 0..2 || it.fileName.isBlank() || it.contentType.isBlank() || it.contentType.length > 100 })) add("attachments")
    }
}

private val MAX_TASK_QUANTITY = BigDecimal("9999999999999999.99")
private fun taskInstant(value: String) = runCatching { OffsetDateTime.parse(value).takeIf {
    it.year in 1..9999 && it.toInstant() != java.time.Instant.parse("0001-01-01T00:00:00Z")
}?.toInstant() }.getOrNull()
private fun canonicalTaskTime(value: String) = runCatching { OffsetDateTime.parse(value).toInstant().toString() }.getOrDefault(value)
private fun canonicalTaskQuantity(value: String): String {
    if (value.length > 128) return value
    val parsed = runCatching { BigDecimal(value).stripTrailingZeros() }.getOrNull() ?: return value
    // Only valid bounded decimals can expand to plain text; hostile exponents stay invalid input.
    if (parsed <= BigDecimal.ZERO || parsed > MAX_TASK_QUANTITY || parsed.scale() > 2) return value
    return parsed.toPlainString()
}

@Serializable
data class FlightTaskPerformerRecord(val id: String, val staffMemberId: String, val fullName: String,
    val employeeId: String, val startUtc: String, val endUtc: String? = null, val notes: String? = null)

@Serializable
data class FlightTaskResourceRecord(val id: String, val resourceId: String, val name: String, val calculationType: Int,
    val quantity: String? = null, val startUtc: String? = null, val endUtc: String? = null, val notes: String? = null)

@Serializable
data class FlightTaskRecord(val id: String, val taskType: Int, val startUtc: String, val endUtc: String? = null,
    val ataChapterId: String? = null, val ataChapterCode: String? = null, val ataChapterTitle: String? = null,
    val customerReferenceNumber: String? = null, val notes: String? = null,
    val performers: List<FlightTaskPerformerRecord> = emptyList(), val tools: List<FlightTaskResourceRecord> = emptyList(),
    val materials: List<FlightTaskResourceRecord> = emptyList(), val generalSupports: List<FlightTaskResourceRecord> = emptyList(),
    val attachments: List<FlightServiceAttachmentRecord> = emptyList(), val createdByUserId: String, val endedByUserId: String? = null,
    val createdAtUtc: String, val updatedByUserId: String? = null, val updatedAtUtc: String? = null,
    val canEdit: Boolean = false, val canSetEnd: Boolean = false, val canDelete: Boolean = false, val canChangeType: Boolean = false,
    val createdByDisplayName: String? = null,
    val endedByDisplayName: String? = null,
    val updatedByDisplayName: String? = null,
    val canCloseOnly: Boolean = false,
) {
    fun input(): SaveFlightTaskRequest {
        fun resources(items: List<FlightTaskResourceRecord>) = items.map {
            SaveFlightTaskResourceRequest(it.id, it.resourceId, it.quantity, it.startUtc, it.endUtc, it.notes)
        }
        return SaveFlightTaskRequest(taskType, startUtc, endUtc, ataChapterId, customerReferenceNumber, notes,
            performers.map { SaveFlightTaskPerformerRequest(it.id, it.staffMemberId, it.startUtc, it.endUtc, it.notes) },
            resources(tools), resources(materials), resources(generalSupports),
            attachments.map { FlightServiceAttachmentInput(it.id, it.kind, it.fileName, it.contentType) }).normalized()
    }
}

@Serializable data class FlightTaskSaved(val task: FlightTaskRecord, val rowVersion: String,
    val activeWorkCount: Int? = null, val completedWorkCount: Int? = null)
@Serializable data class FlightTaskDeleted(val id: String, val rowVersion: String,
    val activeWorkCount: Int? = null, val completedWorkCount: Int? = null)
data class FlightTaskChange(val taskRecordId: String, val input: SaveFlightTaskRequest?)
