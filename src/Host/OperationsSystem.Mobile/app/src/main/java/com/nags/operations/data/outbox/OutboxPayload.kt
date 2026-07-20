package com.nags.operations.data.outbox

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.decodeFromJsonElement

/**
 * Persisted form of a queued work-order submission. Lives inside
 * [com.nags.operations.data.db.entities.WorkOrderOutboxEntity.payloadJson]
 * as a single JSON string so the schema can grow without database migrations
 * for every tweak.
 *
 * Attachments are stored as **file paths**, not base64, so the row stays small
 * even with multi-megabyte documents. The worker reads + base64-encodes lazily
 * at upload time.
 *
 * The shape mirrors the `/api/v1/mobile` write request shapes 1:1 — the worker decodes this
 * JSON, copies ids as-is, and inflates attachment bytes from the on-disk files.
 */
@Serializable
data class OutboxPayload(
    val kind: Kind,
    /** Null only for [Kind.CancelFlight], which files a cancellation work order with no form data. */
    val workOrder: WorkOrderInput? = null,
    val scratchFlight: ScratchFlightInput? = null,
    /** Set only for [Kind.CancelFlight]. */
    val cancelFlight: CancelFlightInput? = null,
    /** Base64 row version captured with an editable work order; required for safe offline updates. */
    val baseRowVersion: String? = null,
) {
    @Serializable
    enum class Kind {
        /** Existing flight (My / Per-Landing / Ad-Hoc); POST `.../flights/{flightId}/work-orders`. */
        ForFlight,

        /** Brand new ad-hoc flight + work order; POST `.../work-orders/scratch`. */
        ScratchAdHoc,

        /** Editable work-order resubmission; PUT `.../work-orders/{workOrderId}`. */
        UpdateExisting,

        /** Append return-to-ramp lines; POST `.../work-orders/{id}/return-to-ramp`. */
        ReturnToRamp,

        /** Cancel a flight (files a cancellation work order); POST `.../flights/{flightId}/cancel`. */
        CancelFlight,
    }

    /**
     * The editable work-order body — mirrors the server's `WorkOrderRequest`.
     * [type] is `Completion` or `Cancellation` and controls which fields are required.
     */
    @Serializable
    data class WorkOrderInput(
        val type: String,
        val actualFlightNumber: String?,
        val aircraftTypeId: String?,
        val aircraftTailNumber: String?,
        val ataIso: String?,
        val atdIso: String?,
        val canceledAtIso: String? = null,
        val cancellationReason: String? = null,
        val remarks: String?,
        val serviceLines: List<ServiceLineInput>,
        val tasks: List<TaskInput>,
        val customerSignaturePngBase64: String? = null,
        /** Version 1 means existing service rows carry stable server ids in an update payload. */
        val serviceLineIdentityVersion: Int = 0,
    )

    /** Flight-only fields needed by the scratch endpoint that aren't on the work-order body. */
    @Serializable
    data class ScratchFlightInput(
        val customerId: String,
        val flightNumber: String,
        val staIso: String,
        val stdIso: String,
        val aircraftTypeId: String? = null,
        val plannedServiceIds: List<String> = emptyList(),
    )

    /** Fields carried by a [Kind.CancelFlight] submission — the flight id lives on the outbox row. */
    @Serializable
    data class CancelFlightInput(
        val canceledAtIso: String,
        val reason: String,
    )

    @Serializable
    data class ServiceLineInput(
        val id: String? = null,
        val serviceId: String,
        val performedByStaffMemberIds: List<String> = emptyList(),
        val fromIso: String,
        val toIso: String,
        val description: String?,
        val isReturnToRamp: Boolean = false,
    )

    @Serializable
    data class TaskInput(
        /** Stable server task id when editing an existing work order (keeps attachments); null = new. */
        val id: String? = null,
        val taskType: String,
        val description: String?,
        val fromIso: String,
        val toIso: String,
        val employeeIds: List<String>,
        val tools: List<ResourceInput> = emptyList(),
        val materials: List<ResourceInput> = emptyList(),
        val generalSupports: List<ResourceInput> = emptyList(),
        val attachments: List<AttachmentInput> = emptyList(),
        val isReturnToRamp: Boolean = false,
    )

    /** One resource row (tool/material/general support) with its quantity. */
    @Serializable
    data class ResourceInput(
        val itemId: String,
        val quantity: Double = 1.0,
    )

    /**
     * Persistent reference to an attachment file under the outbox directory.
     *
     * @param relativePath path relative to the row's attachments dir, e.g. `0-photo.jpg`.
     * @param kind         `Image` / `Voice` / `Document` (server `TaskAttachmentKind` names).
     */
    @Serializable
    data class AttachmentInput(
        val relativePath: String,
        val kind: String,
        val contentType: String,
        val fileName: String,
        val capturedAtIso: String,
        val sizeBytes: Long,
    )
}

/**
 * Decodes queued payloads while upgrading the pre-multi-performer service-line field in memory.
 * This keeps submissions saved by an older app version deliverable after an app upgrade.
 */
internal fun decodeOutboxPayload(json: Json, payloadJson: String): OutboxPayload {
    val root = json.parseToJsonElement(payloadJson)
    val migrated = migrateLegacyOutboxServiceLinePerformers(root)
    return json.decodeFromJsonElement(OutboxPayload.serializer(), migrated)
}

private fun migrateLegacyOutboxServiceLinePerformers(root: JsonElement): JsonElement {
    val rootObject = root as? JsonObject ?: return root
    val workOrder = rootObject["workOrder"] as? JsonObject ?: return root
    val serviceLines = workOrder["serviceLines"] as? JsonArray ?: return root
    var changed = false
    val migratedLines = JsonArray(
        serviceLines.map { element ->
            val line = element as? JsonObject ?: return@map element
            if ("performedByStaffMemberIds" in line) return@map line
            val legacyId = (line["performedByStaffMemberId"] as? JsonPrimitive)
                ?.contentOrNull
                ?.takeIf { it.isNotBlank() }
                ?: return@map line
            changed = true
            JsonObject(
                line.toMutableMap().apply {
                    remove("performedByStaffMemberId")
                    put("performedByStaffMemberIds", JsonArray(listOf(JsonPrimitive(legacyId))))
                },
            )
        },
    )
    if (!changed) return root
    return JsonObject(
        rootObject + ("workOrder" to JsonObject(workOrder + ("serviceLines" to migratedLines))),
    )
}
