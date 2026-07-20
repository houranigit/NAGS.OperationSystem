package com.nags.operations.data.outbox

import android.content.Context
import android.util.Base64
import android.util.Log
import com.nags.operations.data.db.AppDatabase
import com.nags.operations.data.db.entities.WorkOrderOutboxEntity
import com.nags.operations.data.sync.OutboxOpStatus
import com.nags.operations.data.sync.PendingDisplayItem
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.serialization.json.Json
import java.io.File
import java.io.FileOutputStream
import java.io.IOException
import java.util.UUID

/**
 * Single writer for the offline work-order outbox. Pairs the SQL row in
 * [com.nags.operations.data.db.entities.WorkOrderOutboxEntity] with a durable
 * attachments directory under `filesDir/outbox/{clientMutationId}/` so neither
 * a process death nor a `cacheDir` wipe can lose the user's pending work.
 *
 * Read paths exposed as `Flow`s mirror the
 * [com.nags.operations.data.repo.WorkOrderDraftsRepository.observeFlightIdToLatestDraftId]
 * pattern — list ViewModels `combine` the outbox flow on top of their Room
 * flight flow, with no temporal coupling between the two.
 */
class WorkOrderOutboxRepository(
    private val context: Context,
    private val db: AppDatabase,
    private val onPendingWork: suspend () -> Unit = {},
) {
    private val dao get() = db.workOrderOutboxDao()
    private val mutationMutex = Mutex()

    @Volatile private var acceptingEnqueues = true
    @Volatile private var enqueueGeneration = 0L

    private val json = Json {
        ignoreUnknownKeys = true
        encodeDefaults = true
    }

    /** Root directory all per-mutation attachment folders live under. */
    private val rootDir: File
        get() = File(context.filesDir, "outbox").apply { mkdirs() }

    /**
     * Durable directory for one queued submission. Created lazily by [enqueue]
     * after attachments are written and removed by [deleteAndCleanup] when the
     * row is gone.
     */
    private fun directoryFor(clientMutationId: String): File {
        val root = rootDir.canonicalFile
        val directory = File(root, canonicalClientMutationId(clientMutationId)).canonicalFile
        check(directory.parentFile == root) { "Queued attachment directory escaped its storage root." }
        return directory
    }

    /** Resolves only the exact directory written by [enqueue], never a path supplied by sync data. */
    private fun attachmentDirectoryFor(row: WorkOrderOutboxEntity): File? {
        val storedPath = row.attachmentsDir ?: return null
        val expected = directoryFor(row.clientMutationId)
        val stored = File(storedPath).canonicalFile
        check(stored == expected) { "Queued attachment directory is outside its storage root." }
        return expected
    }

    /** Stream of every row (newest first) — used by the Sync Center. */
    fun observeAll(): Flow<List<WorkOrderOutboxEntity>> = dao.observeAll()

    /**
     * Cheap "is anything queued?" flow the [OutboxWorker] gates on alongside
     * `signedIn` and `online`. Distinct-until-changed so the worker only
     * wakes when the queue genuinely transitions in or out of empty.
     */
    fun observeHasPending(): Flow<Boolean> = dao.observeHasPending()

    /**
     * Outbox state overlay for the My / Per-Landing / AdHoc lists. The key is the
     * **server flight id** for existing flights; ad-hoc-scratch rows don't
     * appear here (they go through [observePendingAdHocScratch] which surfaces
     * synthetic flight rows).
     *
     * "Latest wins" per flight: if a flight somehow has two outbox rows (a
     * pre-existing failed one and a new pending submission), the newer row
     * decides the chip status. Mostly defensive — the create UI prevents
     * stacking submissions on the same flight under normal use.
     */
    fun observePendingByFlightId(): Flow<Map<String, PendingDisplayItem>> =
        dao.observeAll().map { rows ->
            rows.asSequence()
                .filter { it.flightKind != WorkOrderOutboxEntity.FLIGHT_KIND_AD_HOC_SCRATCH }
                .filter { it.status != WorkOrderOutboxEntity.STATUS_SUCCEEDED }
                .sortedByDescending { it.createdAtEpochMs }
                .associate { row ->
                    row.flightId to PendingDisplayItem(
                        id = row.clientMutationId,
                        flightId = row.flightId,
                        status = row.status.toOutboxOpStatus(),
                    )
                }
        }

    /**
     * Ad-hoc-scratch rows that don't have a server flight yet — the AdHoc list
     * renders synthetic flight cards from these so the user can see their
     * intent on the list immediately after submitting offline. Keyed by
     * [WorkOrderOutboxEntity.clientFlightId] (always non-null for this kind).
     *
     * Excludes [WorkOrderOutboxEntity.STATUS_SUCCEEDED] rows — once the server
     * acknowledges the scratch flight, the real row arrives via SignalR and
     * the synthetic one disappears.
     */
    fun observePendingAdHocScratch(): Flow<List<PendingAdHocFlight>> =
        dao.observeAll().map { rows ->
            rows.asSequence()
                .filter { it.flightKind == WorkOrderOutboxEntity.FLIGHT_KIND_AD_HOC_SCRATCH }
                .filter { it.status != WorkOrderOutboxEntity.STATUS_SUCCEEDED }
                .sortedByDescending { it.createdAtEpochMs }
                .mapNotNull { row -> row.toPendingAdHoc(json) }
                .toList()
        }

    suspend fun getById(clientMutationId: String): WorkOrderOutboxEntity? =
        dao.getById(clientMutationId)

    /** First [WorkOrderOutboxEntity.STATUS_PENDING] row by FIFO; null when the queue is empty. */
    suspend fun nextPending(): WorkOrderOutboxEntity? = dao.listPendingFifo().firstOrNull()

    /** FIFO snapshot used by the worker to skip rows whose retry backoff has not elapsed. */
    suspend fun pendingFifo(): List<WorkOrderOutboxEntity> = dao.listPendingFifo()

    /**
     * Atomic single-row insert: writes every attachment from its in-memory
     * base64 to a per-mutation directory under `filesDir/outbox/{id}/`, then
     * inserts the metadata row referencing those paths. If the disk write
     * fails we surface that to the caller and don't leave a half-formed row
     * behind.
     *
     * @return the inserted row so the ViewModel can stash anything else it
     *         needs (currently just used for the snackbar text path).
     */
    suspend fun enqueue(
        request: EnqueueRequest,
    ): WorkOrderOutboxEntity {
        // Capture before waiting for the mutex. A logout/account switch increments the generation,
        // so an enqueue already queued behind cleanup cannot publish stale work afterward.
        val observedGeneration = enqueueGeneration
        val entity = mutationMutex.withLock {
            check(acceptingEnqueues && observedGeneration == enqueueGeneration) {
                "The session changed before the submission could be saved. Sign in and try again."
            }

            val mutationId = canonicalClientMutationId(request.clientMutationId)
            check(!dao.hasOtherUnresolvedForFlight(request.flightId, mutationId)) {
                "This flight already has a queued or failed operation. Review it in Sync Center before submitting another."
            }
            val durableAttachments = mutableListOf<OutboxPayload.AttachmentInput>()
            var attachmentDir: File? = null

            try {
                if (request.attachmentsToPersist.isNotEmpty()) {
                    val dir = directoryFor(mutationId)
                    attachmentDir = dir
                    if (dir.exists() && !dir.deleteRecursively()) {
                        throw IOException("Could not reset the attachment staging directory.")
                    }
                    if (!dir.mkdirs() && !dir.isDirectory) {
                        throw IOException("Could not create the attachment staging directory.")
                    }
                    request.attachmentsToPersist.forEachIndexed { index, source ->
                        val safeName = sanitizeFileName(source.fileName)
                        val relative = "$index-$safeName"
                        val target = File(dir, relative)
                        FileOutputStream(target).use { out ->
                            out.write(Base64.decode(source.base64, Base64.NO_WRAP))
                            out.flush()
                            out.fd.sync()
                        }
                        durableAttachments += OutboxPayload.AttachmentInput(
                            relativePath = relative,
                            kind = source.kind,
                            contentType = source.contentType,
                            fileName = source.fileName,
                            capturedAtIso = source.capturedAtIso,
                            sizeBytes = source.sizeBytes,
                        )
                    }
                }

                // Re-stitch durable attachments onto the per-task list in the order they were enqueued.
                val workOrder = request.payload.workOrder
                val payloadOnDisk = if (workOrder == null) {
                    request.payload
                } else {
                    val attachmentCursor = AttachmentCursor(durableAttachments)
                    val tasksWithDurable = workOrder.tasks.map { task ->
                        val count = task.attachments.size
                        task.copy(attachments = attachmentCursor.takeNext(count))
                    }
                    request.payload.copy(workOrder = workOrder.copy(tasks = tasksWithDurable))
                }

                val now = System.currentTimeMillis()
                val entity = WorkOrderOutboxEntity(
                    clientMutationId = mutationId,
                    flightId = request.flightId,
                    flightKind = request.flightKind,
                    clientFlightId = request.clientFlightId,
                    payloadJson = json.encodeToString(OutboxPayload.serializer(), payloadOnDisk),
                    attachmentsDir = if (durableAttachments.isEmpty()) null else attachmentDir?.absolutePath,
                    status = WorkOrderOutboxEntity.STATUS_PENDING,
                    attempts = 0,
                    lastError = null,
                    createdAtEpochMs = now,
                    updatedAtEpochMs = now,
                    serverWorkOrderId = request.knownServerWorkOrderId,
                )
                dao.upsert(entity)
                entity
            } catch (e: Exception) {
                // A file failure or Room failure must not leave a half-formed directory that a
                // later worker could mistake for a durable submission.
                attachmentDir?.deleteRecursively()
                throw e
            }
        }
        notifyPendingWork()
        return entity
    }

    /**
     * Convenience enqueue for a "cancel flight" submission: no work-order body, no
     * attachments. Generates a fresh [clientMutationId] (the server's idempotency key)
     * and queues a [OutboxPayload.Kind.CancelFlight] row that the worker POSTs to
     * `.../flights/{flightId}/cancel` when connectivity returns.
     */
    suspend fun enqueueCancel(
        flightId: String,
        flightKind: Int,
        canceledAtIso: String,
        reason: String,
    ): WorkOrderOutboxEntity = enqueue(
        EnqueueRequest(
            clientMutationId = UUID.randomUUID().toString(),
            flightId = flightId,
            flightKind = flightKind,
            clientFlightId = null,
            payload = OutboxPayload(
                kind = OutboxPayload.Kind.CancelFlight,
                workOrder = null,
                cancelFlight = OutboxPayload.CancelFlightInput(
                    canceledAtIso = canceledAtIso,
                    reason = reason,
                ),
            ),
            attachmentsToPersist = emptyList(),
            knownServerWorkOrderId = null,
        ),
    )

    /**
     * Convenience enqueue for "update the cancellation details of an existing editable
     * cancellation work order". Reuses the [OutboxPayload.Kind.UpdateExisting] path (PUT
     * `.../work-orders/{workOrderId}`) with empty line collections — a cancellation work
     * order carries no service / task lines.
     */
    suspend fun enqueueCancellationUpdate(
        flightId: String,
        flightKind: Int,
        workOrderId: String,
        baseRowVersion: String,
        canceledAtIso: String,
        reason: String,
        remarks: String?,
    ): WorkOrderOutboxEntity = enqueue(
        EnqueueRequest(
            clientMutationId = UUID.randomUUID().toString(),
            flightId = flightId,
            flightKind = flightKind,
            clientFlightId = null,
            payload = OutboxPayload(
                kind = OutboxPayload.Kind.UpdateExisting,
                workOrder = OutboxPayload.WorkOrderInput(
                    type = "Cancellation",
                    actualFlightNumber = null,
                    aircraftTypeId = null,
                    aircraftTailNumber = null,
                    ataIso = null,
                    atdIso = null,
                    canceledAtIso = canceledAtIso,
                    cancellationReason = reason,
                    remarks = remarks,
                    serviceLines = emptyList(),
                    tasks = emptyList(),
                    customerSignaturePngBase64 = null,
                ),
                baseRowVersion = baseRowVersion,
            ),
            attachmentsToPersist = emptyList(),
            knownServerWorkOrderId = workOrderId,
        ),
    )

    /** Worker hook: flip a queued row to [WorkOrderOutboxEntity.STATUS_SENDING]. */
    suspend fun markSending(row: WorkOrderOutboxEntity) {
        dao.updateStatus(
            id = row.clientMutationId,
            status = WorkOrderOutboxEntity.STATUS_SENDING,
            attempts = row.attempts,
            lastError = null,
            serverWorkOrderId = row.serverWorkOrderId,
            updatedAtEpochMs = System.currentTimeMillis(),
        )
    }

    /**
     * Worker hook: bump attempts and re-queue. Used for transport / 5xx errors
     * where the user's intent is still valid and a backoff retry is the right
     * move.
     */
    suspend fun markPendingAfterRetry(
        row: WorkOrderOutboxEntity,
        error: String?,
        schedulePersistentWork: Boolean = true,
    ) {
        dao.updateStatus(
            id = row.clientMutationId,
            status = WorkOrderOutboxEntity.STATUS_PENDING,
            attempts = row.attempts + 1,
            lastError = error,
            serverWorkOrderId = row.serverWorkOrderId,
            updatedAtEpochMs = System.currentTimeMillis(),
        )
        if (schedulePersistentWork) notifyPendingWork()
    }

    /** Worker hook: terminal 4xx (non-409) — surfaces in Sync Center for review. */
    suspend fun markFailed(row: WorkOrderOutboxEntity, error: String) {
        dao.updateStatus(
            id = row.clientMutationId,
            status = WorkOrderOutboxEntity.STATUS_FAILED,
            attempts = row.attempts + 1,
            lastError = error,
            serverWorkOrderId = row.serverWorkOrderId,
            updatedAtEpochMs = System.currentTimeMillis(),
        )
    }

    /**
     * Worker hook: terminal 409 (ad-hoc-scratch collision). Hands the user a
     * "Re-open as draft" affordance — only realistic when the same user
     * submits the same offline content from two different installs.
     */
    suspend fun markConflict(row: WorkOrderOutboxEntity, error: String) {
        dao.updateStatus(
            id = row.clientMutationId,
            status = WorkOrderOutboxEntity.STATUS_CONFLICT,
            attempts = row.attempts + 1,
            lastError = error,
            serverWorkOrderId = row.serverWorkOrderId,
            updatedAtEpochMs = System.currentTimeMillis(),
        )
    }

    /**
     * Worker hook: 2xx success. Records [serverWorkOrderId] but keeps the row
     * around in [WorkOrderOutboxEntity.STATUS_SUCCEEDED] state so the SignalR
     * echo handler in `SyncCoordinator.applyChange` can delete it the instant
     * the originating-mutation echo lands. That two-step keeps the optimistic
     * chip visible until the real cache row exists, so the UI never flickers
     * between "pending" and the final state.
     */
    suspend fun markSucceeded(row: WorkOrderOutboxEntity, serverWorkOrderId: String?) {
        dao.updateStatus(
            id = row.clientMutationId,
            status = WorkOrderOutboxEntity.STATUS_SUCCEEDED,
            attempts = row.attempts + 1,
            lastError = null,
            serverWorkOrderId = serverWorkOrderId,
            updatedAtEpochMs = System.currentTimeMillis(),
        )
    }

    /**
     * Drops the row and its attachments directory. Called from
     * `SyncCoordinator.applyChange` when the server-truth echo for this
     * mutation lands, and from `clearForLogout` as part of a full reset.
     */
    suspend fun deleteAndCleanup(clientMutationId: String) {
        mutationMutex.withLock {
            // Sync events are server-controlled. Never turn their mutation id into a path: first
            // require an exact local row, then resolve the trusted path persisted by enqueue().
            val row = dao.getById(clientMutationId) ?: return@withLock
            val dir = attachmentDirectoryFor(row)
            if (dir != null && dir.exists() && !dir.deleteRecursively()) {
                throw IOException("Could not remove queued attachment files.")
            }
            dao.deleteById(row.clientMutationId)
        }
    }

    /** Logout-only — wipes every queued mutation along with the on-disk attachments. */
    suspend fun deleteAll() {
        mutationMutex.withLock {
            enqueueGeneration++
            acceptingEnqueues = false
            dao.deleteAll()
            val root = File(context.filesDir, "outbox")
            if (root.exists() && !root.deleteRecursively()) {
                throw IOException("Could not remove queued attachment files.")
            }
        }
    }

    /** Stops late screen coroutines from enqueueing after logout without deleting same-user work. */
    suspend fun pauseEnqueues() {
        mutationMutex.withLock {
            enqueueGeneration++
            acceptingEnqueues = false
        }
    }

    suspend fun resumeEnqueues() {
        mutationMutex.withLock { acceptingEnqueues = true }
    }

    /** Best-effort cleanup for directories orphaned by process death between SQL and file cleanup. */
    suspend fun cleanupOrphanedAttachmentDirectories() {
        mutationMutex.withLock {
            val liveIds = dao.snapshot().mapTo(mutableSetOf()) { it.clientMutationId }
            val root = File(context.filesDir, "outbox")
            root.listFiles()
                ?.filter { it.isDirectory && it.name !in liveIds }
                ?.forEach { it.deleteRecursively() }
        }
    }

    /** Completes a process-interrupted HTTP-acknowledgement cleanup without replaying payloads. */
    suspend fun cleanupSucceededRows() {
        mutationMutex.withLock {
            var firstFailure: IOException? = null
            dao.snapshot()
                .filter { it.status == WorkOrderOutboxEntity.STATUS_SUCCEEDED }
                .forEach { row ->
                    try {
                        val dir = attachmentDirectoryFor(row)
                        if (dir != null && dir.exists() && !dir.deleteRecursively()) {
                            throw IOException("Could not remove acknowledged attachment files.")
                        }
                        dao.deleteById(row.clientMutationId)
                    } catch (e: Exception) {
                        if (firstFailure == null) {
                            firstFailure = if (e is IOException) e else IOException(e)
                        }
                    }
                }
            firstFailure?.let { throw it }
        }
    }

    /**
     * Restore any rows the previous worker run left mid-flight back to
     * [WorkOrderOutboxEntity.STATUS_PENDING]. Called on every drain entry so a
     * cancelled POST (process death, gate cycling offline → online, etc.)
     * doesn't leave a row stuck visible as "Syncing…" on the flight list.
     */
    suspend fun recoverInterruptedSends() {
        dao.recoverInterruptedSends(System.currentTimeMillis())
    }

    /** Scheduling is best-effort here: the row is already durable and session resume retries it. */
    private suspend fun notifyPendingWork() {
        try {
            onPendingWork()
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            Log.w(TAG, "Could not schedule persistent outbox delivery", e)
        }
    }

    private fun WorkOrderOutboxEntity.toPendingAdHoc(json: Json): PendingAdHocFlight? {
        val syntheticFlightId = clientFlightId ?: return null
        val payload = runCatching {
            decodeOutboxPayload(json, payloadJson)
        }.getOrNull() ?: return null
        val scratch = payload.scratchFlight ?: return null
        return PendingAdHocFlight(
            clientMutationId = clientMutationId,
            clientFlightId = syntheticFlightId,
            flightNumber = scratch.flightNumber,
            customerName = "",
            customerIataCode = "",
            stationCode = "",
            aircraftModel = null,
            sta = scratch.staIso,
            std = scratch.stdIso,
            status = status.toOutboxOpStatus(),
        )
    }

    private class AttachmentCursor(private val all: List<OutboxPayload.AttachmentInput>) {
        private var nextIndex = 0
        fun takeNext(count: Int): List<OutboxPayload.AttachmentInput> {
            if (count == 0) return emptyList()
            val end = (nextIndex + count).coerceAtMost(all.size)
            val slice = all.subList(nextIndex, end).toList()
            nextIndex = end
            return slice
        }
    }

    private companion object {
        const val TAG = "WorkOrderOutbox"
    }
}

/** Strips path-traversal-friendly characters that the OS would otherwise let through. */
private fun sanitizeFileName(raw: String): String =
    raw.replace(Regex("[^A-Za-z0-9._-]"), "_").take(96).ifBlank { "file" }

/** UUIDs are the only supported mutation ids and therefore the only valid directory names. */
internal fun canonicalClientMutationId(raw: String): String {
    val parsed = runCatching { UUID.fromString(raw) }.getOrNull()
    require(parsed != null && parsed.toString() == raw) { "Client mutation id must be a canonical UUID." }
    return raw
}

/** Translate the persisted integer status to the user-facing chip state. */
private fun Int.toOutboxOpStatus(): OutboxOpStatus = when (this) {
    WorkOrderOutboxEntity.STATUS_SENDING -> OutboxOpStatus.Sending
    WorkOrderOutboxEntity.STATUS_FAILED,
    WorkOrderOutboxEntity.STATUS_CONFLICT -> OutboxOpStatus.Failed
    else -> OutboxOpStatus.Pending
}

/**
 * Input to [WorkOrderOutboxRepository.enqueue]. Attachments arrive in memory
 * from the create screen (base64 from the pickers) and are persisted to disk
 * before the row is committed.
 *
 * @param flightId       Server flight id (kinds 0-2) or the client-generated id (kind 3).
 * @param flightKind     One of the `FLIGHT_KIND_*` constants on [WorkOrderOutboxEntity].
 * @param clientFlightId Same as [flightId] for [WorkOrderOutboxEntity.FLIGHT_KIND_AD_HOC_SCRATCH],
 *                       `null` otherwise. Mirrors what the server's idempotency check expects.
 * @param attachmentsToPersist The base64 attachments in the order the ViewModel
 *                             holds them; the repository writes each to disk
 *                             and re-stitches them back onto the right tasks.
 */
data class EnqueueRequest(
    val clientMutationId: String,
    val flightId: String,
    val flightKind: Int,
    val clientFlightId: String?,
    val payload: OutboxPayload,
    val attachmentsToPersist: List<EnqueueAttachment>,
    /**
     * For [OutboxPayload.Kind.UpdateExisting] — the server's work order id so the worker
     * targets PUT `/work-orders/{id}` and the row survives retries with a stable id.
     */
    val knownServerWorkOrderId: String? = null,
)

/**
 * Volatile attachment data straight off the UI form, persisted to disk by
 * [WorkOrderOutboxRepository.enqueue]. After enqueue, the only reference to
 * the bytes is the per-mutation file under `filesDir/outbox/{id}/`.
 */
data class EnqueueAttachment(
    val base64: String,
    /** `Image` / `Voice` / `Document` — server `TaskAttachmentKind` enum names. */
    val kind: String,
    val contentType: String,
    val fileName: String,
    val capturedAtIso: String,
    val sizeBytes: Long,
)

/**
 * Synthetic ad-hoc flight produced by [WorkOrderOutboxRepository.observePendingAdHocScratch].
 * Rendered on the AdHoc list as a pending card alongside server-truth flights;
 * disappears the moment the server-truth flight echo arrives via SignalR and
 * the outbox row is cleaned up.
 */
data class PendingAdHocFlight(
    val clientMutationId: String,
    val clientFlightId: String,
    val flightNumber: String,
    val customerName: String,
    val customerIataCode: String,
    val stationCode: String,
    val aircraftModel: String?,
    val sta: String,
    val std: String,
    val status: OutboxOpStatus,
)
