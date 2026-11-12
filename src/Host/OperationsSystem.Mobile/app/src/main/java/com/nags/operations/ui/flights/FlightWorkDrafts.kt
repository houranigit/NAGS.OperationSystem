package com.nags.operations.ui.flights

import androidx.compose.runtime.saveable.Saver
import java.time.LocalDateTime
import java.time.ZoneId
import java.util.UUID
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

/** Local form-draft values; Services use a separate UTC API contract when started. */
@Serializable
internal data class FlightWorkDrafts(
    val services: List<FlightServiceDraft> = emptyList(),
    val tasks: List<FlightTaskDraft> = emptyList(),
)

// Save creation times and generated IDs explicitly so restoring later cannot change
// work times or associate nested editors with a different entry.
private val WorkDraftJson = Json { encodeDefaults = true }

internal val FlightWorkDraftsSaver = Saver<FlightWorkDrafts, String>(
    save = { WorkDraftJson.encodeToString(it) },
    restore = { WorkDraftJson.decodeFromString<FlightWorkDrafts>(it) },
)

@Serializable
internal data class FlightWorkPeriod(
    val start: String = "",
    val end: String = "",
    val originalStartUtc: String? = null,
    val originalEndUtc: String? = null,
) {
    companion object {
        fun now(zone: ZoneId) = FlightWorkPeriod(
            start = LocalDateTime.now(zone).withSecond(0).withNano(0).toString())
    }
}

@Serializable
internal data class FlightServiceDraft(
    val id: String = UUID.randomUUID().toString(),
    val serviceId: String = "",
    val serviceName: String = "",
    val period: FlightWorkPeriod = FlightWorkPeriod(),
    val reference: String = "",
    val remarks: String = "",
    val employees: List<FlightEmployeeDraft> = emptyList(),
    val attachments: List<FlightAttachmentDraft> = emptyList(),
    val plannedServiceId: String? = null,
)

@Serializable
internal data class FlightTaskDraft(
    val id: String = UUID.randomUUID().toString(),
    val taskType: String = "",
    val ataChapterId: String = "",
    val ataChapterCode: String = "",
    val ataChapterTitle: String = "",
    val period: FlightWorkPeriod = FlightWorkPeriod(),
    val reference: String = "",
    val remarks: String = "",
    val employees: List<FlightEmployeeDraft> = emptyList(),
    val tools: List<FlightResourceDraft> = emptyList(),
    val materials: List<FlightResourceDraft> = emptyList(),
    val generalSupports: List<FlightResourceDraft> = emptyList(),
    val attachments: List<FlightAttachmentDraft> = emptyList(),
)

@Serializable
internal data class FlightEmployeeDraft(
    val id: String = UUID.randomUUID().toString(),
    val employeeId: String = "",
    val employeeName: String = "",
    val employeeCode: String = "",
    val period: FlightWorkPeriod = FlightWorkPeriod(),
    val remarks: String = "",
)

@Serializable
internal data class FlightResourceDraft(
    val id: String = UUID.randomUUID().toString(),
    val resourceId: String = "",
    val resourceName: String = "",
    val period: FlightWorkPeriod = FlightWorkPeriod(),
    val remarks: String = "",
)

/** Selected media, durable pending media, or a scoped preview of an uploaded attachment. */
@Serializable
internal data class FlightAttachmentDraft(
    val id: String = UUID.randomUUID().toString(),
    val uri: String,
    val name: String,
    val mimeType: String = "",
    val durationMillis: Long = 0,
    val ownedCapture: Boolean = false,
    val uploadedAtUtc: String? = null,
)
