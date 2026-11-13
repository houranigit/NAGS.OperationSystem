package com.nags.operations.ui.flights

import com.nags.operations.data.FlightDetail
import com.nags.operations.data.FlightServiceRecord
import com.nags.operations.data.FlightTaskRecord
import com.nags.operations.data.SaveFlightServiceRequest
import com.nags.operations.data.SaveFlightTaskRequest
import com.nags.operations.data.db.entities.FlightServicePendingEntity
import com.nags.operations.data.db.entities.FlightTaskPendingEntity
import kotlinx.serialization.json.Json
import java.time.OffsetDateTime
import java.time.ZoneId

private val workRecordJson = Json { ignoreUnknownKeys = true }

private fun sameRecordedEnd(first: String?, second: String?): Boolean {
    fun canonical(value: String?) = value?.let { runCatching { OffsetDateTime.parse(it).toInstant().toString() }.getOrDefault(it) }
    return canonical(first) == canonical(second)
}

internal fun FlightServicePendingEntity.reviewedRecord(): FlightServiceRecord? = reviewedServiceJson?.let { raw ->
    runCatching { workRecordJson.decodeFromString<FlightServiceRecord>(raw).takeIf { it.id == serviceRecordId } }.getOrNull()
}

internal fun FlightTaskPendingEntity.reviewedRecord(): FlightTaskRecord? = reviewedTaskJson?.let { raw ->
    runCatching { workRecordJson.decodeFromString<FlightTaskRecord>(raw).takeIf { it.id == taskRecordId } }.getOrNull()
}

/** Attribution belongs to a published record; a queue timestamp describes only a device save. */
internal data class FlightWorkRecordMetadata(
    val createdByUserId: String?,
    val createdByDisplayName: String?,
    val createdAtUtc: String?,
    val published: Boolean,
    val queued: Boolean,
    val pendingStatus: String?,
    val savedAtUtc: String?,
    val endedByUserId: String? = null,
    val endedByDisplayName: String? = null,
    val updatedByDisplayName: String? = null,
    val updatedAtUtc: String? = null,
    val pendingOperation: String? = null,
    val canCloseOnly: Boolean = false,
)

/** Keys classify published/queued occurrences. Unstarted local form drafts are deliberately absent. */
internal fun flightServiceRecordMetadata(
    flight: FlightDetail,
    pending: List<FlightServicePendingEntity>,
): Map<String, FlightWorkRecordMetadata> {
    val rows = pending.filter { it.flightId == flight.id }.associateBy { it.serviceRecordId }
    val records = flight.services.associateBy { it.id }
    return (records.keys + rows.keys).associateWith { id ->
        val row = rows[id]
        val original = records[id] ?: row?.reviewedRecord()
        val desired = row?.takeIf { it.operation == "save" }?.inputJson?.let { raw ->
            runCatching { workRecordJson.decodeFromString<SaveFlightServiceRequest>(raw) }.getOrNull()
        }
        val retainsCloser = desired == null || sameRecordedEnd(desired.endUtc, original?.endUtc)
        FlightWorkRecordMetadata(original?.createdByUserId?.takeIf(String::isNotBlank),
            original?.createdByDisplayName?.takeIf(String::isNotBlank), original?.createdAtUtc?.takeIf(String::isNotBlank),
            published = original != null, queued = row != null, pendingStatus = row?.status, savedAtUtc = row?.savedAtUtc,
            endedByUserId = original?.endedByUserId?.takeIf { retainsCloser && it.isNotBlank() },
            endedByDisplayName = original?.endedByDisplayName?.takeIf { retainsCloser && it.isNotBlank() },
            updatedByDisplayName = original?.updatedByDisplayName?.takeIf(String::isNotBlank),
            updatedAtUtc = original?.updatedAtUtc?.takeIf(String::isNotBlank), pendingOperation = row?.operation,
            canCloseOnly = original?.canCloseOnly == true)
    }
}

/** Desired task values are already projected by initialTaskEditorDraft; these captions retain their real owner. */
internal fun flightTaskRecordMetadata(
    flight: FlightDetail,
    pending: List<FlightTaskPendingEntity>,
): Map<String, FlightWorkRecordMetadata> {
    val rows = pending.filter { it.flightId == flight.id }.associateBy { it.taskRecordId }
    val records = flight.tasks.associateBy { it.id }
    return (records.keys + rows.keys).associateWith { id ->
        val row = rows[id]
        val original = records[id] ?: row?.reviewedRecord()
        val desired = row?.takeIf { it.operation == "save" }?.inputJson?.let { raw ->
            runCatching { workRecordJson.decodeFromString<SaveFlightTaskRequest>(raw) }.getOrNull()
        }
        val retainsCloser = desired == null || sameRecordedEnd(desired.endUtc, original?.endUtc)
        FlightWorkRecordMetadata(original?.createdByUserId?.takeIf(String::isNotBlank),
            original?.createdByDisplayName?.takeIf(String::isNotBlank), original?.createdAtUtc?.takeIf(String::isNotBlank),
            published = original != null, queued = row != null, pendingStatus = row?.status, savedAtUtc = row?.savedAtUtc,
            endedByUserId = original?.endedByUserId?.takeIf { retainsCloser && it.isNotBlank() },
            endedByDisplayName = original?.endedByDisplayName?.takeIf { retainsCloser && it.isNotBlank() },
            updatedByDisplayName = original?.updatedByDisplayName?.takeIf(String::isNotBlank),
            updatedAtUtc = original?.updatedAtUtc?.takeIf(String::isNotBlank), pendingOperation = row?.operation,
            canCloseOnly = original?.canCloseOnly == true)
    }
}


/** Display-only tombstones keep queued deletions reachable without restoring them to the desired graph. */
internal fun pendingDeletedServiceDrafts(
    flight: FlightDetail,
    pending: List<FlightServicePendingEntity>,
    catalogs: FlightOperationsCatalogs,
    zone: ZoneId,
): List<FlightServiceDraft> = pending.filter { it.flightId == flight.id && it.operation == "delete" }
    .distinctBy { it.serviceRecordId }.map { row ->
        val recorded = flight.services.firstOrNull { it.id == row.serviceRecordId } ?: row.reviewedRecord()
        recorded?.input()?.toEditorDraft(row.serviceRecordId, recorded.serviceName, recorded, catalogs, zone)
            ?: FlightServiceDraft(id = row.serviceRecordId)
    }

internal fun pendingDeletedTaskDrafts(
    flight: FlightDetail,
    pending: List<FlightTaskPendingEntity>,
    catalogs: FlightOperationsCatalogs,
    zone: ZoneId,
): List<FlightTaskDraft> = pending.filter { it.flightId == flight.id && it.operation == "delete" }
    .distinctBy { it.taskRecordId }.map { row ->
        val recorded = flight.tasks.firstOrNull { it.id == row.taskRecordId } ?: row.reviewedRecord()
        recorded?.input()?.toTaskEditorDraft(row.taskRecordId, recorded, catalogs, zone)
            ?: FlightTaskDraft(id = row.taskRecordId)
    }
