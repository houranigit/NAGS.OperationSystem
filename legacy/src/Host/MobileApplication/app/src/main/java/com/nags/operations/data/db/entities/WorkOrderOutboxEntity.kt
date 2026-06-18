package com.nags.operations.data.db.entities

import androidx.room.Entity
import androidx.room.Index
import androidx.room.PrimaryKey

/**
 * One pending or in-flight work-order submission. Lives in its own table so a
 * stale optimistic chip can never poison the server-truth flight caches — the
 * single-writer rule on `flights_*` (only `SyncCoordinator` writes there)
 * stays untouched.
 *
 * Row lifetime:
 *
 *   1. `enqueue` from the create screen → status = [STATUS_PENDING], attachments copied to
 *      `filesDir/outbox/{clientMutationId}/`.
 *   2. `OutboxWorker` picks the row, flips to [STATUS_SENDING], POSTs to the server.
 *   3a. 2xx → row marked [STATUS_SUCCEEDED]; the `SyncCoordinator` deletes it the moment
 *       the SignalR echo lands with a matching `originMutationId` (also cleans up
 *       [attachmentsDir]).
 *   3b. 5xx / network → bumped back to [STATUS_PENDING], [attempts]++ for backoff.
 *   3c. 4xx non-409 → terminal [STATUS_FAILED]; surfaces in Sync Center.
 *   3d. 409 (ad-hoc-scratch only) → terminal [STATUS_CONFLICT]; user re-opens as draft.
 *
 * @param flightKind Distinguishes which list cache to overlay the optimistic chip onto.
 *                   0 = My / 1 = AOG / 2 = AdHoc (existing) / 3 = AdHocScratch (no server flight yet).
 * @param flightId   For kinds 0-2: the server flight id. For kind 3 (AdHocScratch): the
 *                   client-generated id we send to the server as `clientFlightId` — the
 *                   AdHoc list shows a synthetic row keyed by this id until the server
 *                   echoes a real one back.
 * @param clientFlightId Same value as [flightId] for kind 3, null for kinds 0-2.
 * @param payloadJson Serialized [OutboxPayload] (see WorkOrderOutboxRepository). File paths,
 *                    not base64, for attachments — the worker reads + encodes them at upload time.
 * @param attachmentsDir Absolute path to the per-mutation attachment directory inside
 *                       `filesDir`. Cleaned up when the row is deleted. Null when the
 *                       submission has no attachments.
 */
@Entity(
    tableName = "work_order_outbox",
    indices = [
        Index("flightId"),
        Index("status"),
        Index("createdAtEpochMs"),
    ],
)
data class WorkOrderOutboxEntity(
    @PrimaryKey val clientMutationId: String,
    val flightId: String,
    val flightKind: Int,
    val clientFlightId: String?,
    val payloadJson: String,
    val attachmentsDir: String?,
    val status: Int,
    val attempts: Int,
    val lastError: String?,
    val createdAtEpochMs: Long,
    val updatedAtEpochMs: Long,
    /** Server work order id, populated after a successful POST. */
    val serverWorkOrderId: String?,
) {
    companion object {
        const val FLIGHT_KIND_MY = 0
        const val FLIGHT_KIND_AOG = 1
        const val FLIGHT_KIND_AD_HOC = 2
        const val FLIGHT_KIND_AD_HOC_SCRATCH = 3

        const val STATUS_PENDING = 0
        const val STATUS_SENDING = 1
        const val STATUS_FAILED = 2
        const val STATUS_CONFLICT = 3
        const val STATUS_SUCCEEDED = 4
    }
}
