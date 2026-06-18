package com.nags.operations.data.outbox

import kotlinx.serialization.Serializable

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
 * The shape mirrors the v2 mobile API request shapes 1:1 (see
 * `MobileCreateWorkOrderRequest` and `MobileCreateFromScratchRequest` on the
 * server) — the worker just decodes this JSON, copies the GUIDs as-is, and
 * inflates attachment bytes from the on-disk files.
 */
@Serializable
data class OutboxPayload(
    val kind: Kind,
    /** Null only for [Kind.CancelFlight], which files an empty cancel work order with no form data. */
    val workOrder: WorkOrderInput? = null,
    val scratchFlight: ScratchFlightInput? = null,
    /** Set only for [Kind.CancelFlight]. */
    val cancelFlight: CancelFlightInput? = null,
) {
    @Serializable
    enum class Kind {
        /** Existing flight (My / AOG / AdHoc); POST `.../flights/{flightId}/work-orders`. */
        ForFlight,

        /** Brand new ad-hoc flight + work order; POST `.../work-orders/scratch`. */
        ScratchAdHoc,

        /** Under-review work order resubmission; PUT `.../work-orders/{workOrderId}`. */
        UpdateExisting,

        /** Append return-to-ramp lines; POST `.../work-orders/{id}/return-to-ramp`. */
        ReturnToRamp,

        /** Cancel a flight with no work-order body; POST `.../flights/{flightId}/cancel`. */
        CancelFlight,
    }

    /**
     * Common work-order fields. For [Kind.ForFlight] these go straight onto the
     * request body. For [Kind.ScratchAdHoc] they're merged with [ScratchFlightInput].
     */
    @Serializable
    data class WorkOrderInput(
        val flightNumber: String,
        val aircraftTypeId: String?,
        val aircraftTailNumber: String?,
        val ata: String?,
        val atd: String?,
        val remarks: String?,
        val serviceLines: List<ServiceLineInput>,
        val tasks: List<TaskInput>,
        val customerSignaturePngBase64: String?,
        /** True only when updating an under-review cancel work order via [Kind.UpdateExisting]. */
        val isCanceled: Boolean = false,
        /** Cancellation time (ISO-8601) carried alongside [isCanceled]; null for non-cancellation work orders. */
        val cancellationAt: String? = null,
    )

    /** Flight-only fields needed by the scratch endpoint that aren't on the work-order body. */
    @Serializable
    data class ScratchFlightInput(
        val customerId: String,
        val sta: String,
        val std: String,
        val isCanceled: Boolean,
        val cancellationAt: String?,
    )

    /** The only field carried by a [Kind.CancelFlight] submission — the flight id lives on the outbox row. */
    @Serializable
    data class CancelFlightInput(
        val canceledAtIso: String,
    )

    @Serializable
    data class ServiceLineInput(
        val serviceId: String,
        val employeeId: String,
        val fromIso: String,
        val toIso: String,
        val description: String?,
    )

    @Serializable
    data class TaskInput(
        val taskType: Int,
        val description: String?,
        val fromIso: String,
        val toIso: String,
        val employeeIds: List<String>,
        val toolIds: List<String>,
        val materialIds: List<String>,
        val generalSupportIds: List<String>,
        val attachments: List<AttachmentInput>,
    )

    /**
     * Persistent reference to an attachment file under the outbox directory.
     *
     * @param relativePath path relative to the row's [com.nags.operations.data.db.entities.WorkOrderOutboxEntity.attachmentsDir],
     *                     e.g. `0-photo.jpg`. The worker resolves it back to an absolute file at upload time.
     * @param kind         matches `TaskAttachmentKind` on the server (see [com.nags.operations.data.TaskAttachmentKindValue]).
     */
    @Serializable
    data class AttachmentInput(
        val relativePath: String,
        val kind: Int,
        val contentType: String,
        val fileName: String,
        val capturedAtIso: String,
        val sizeBytes: Long,
    )
}
