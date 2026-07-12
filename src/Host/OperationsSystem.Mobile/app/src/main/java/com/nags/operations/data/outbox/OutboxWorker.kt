package com.nags.operations.data.outbox

import android.util.Base64
import android.util.Log
import com.nags.operations.data.ApiException
import com.nags.operations.data.TokenStore
import com.nags.operations.data.api.MobileApi
import com.nags.operations.data.api.MobileCancelFlightRequest
import com.nags.operations.data.api.MobileReturnToRampRequest
import com.nags.operations.data.api.MobileScratchWorkOrderRequest
import com.nags.operations.data.api.MobileWorkOrderWriteRequest
import com.nags.operations.data.api.WorkOrderServiceLineInput
import com.nags.operations.data.api.WorkOrderSignatureInput
import com.nags.operations.data.api.WorkOrderTaskAttachmentInput
import com.nags.operations.data.api.WorkOrderTaskInput
import com.nags.operations.data.api.WorkOrderTaskResourceInput
import com.nags.operations.data.api.WorkOrderWireRequest
import com.nags.operations.data.db.entities.WorkOrderOutboxEntity
import com.nags.operations.data.network.NetworkMonitor
import com.nags.operations.data.sync.SyncCoordinator
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.serialization.json.Json
import java.io.File

/**
 * Drains the offline work-order outbox into the `/api/v1/mobile` write endpoints.
 *
 * Discipline (mirrors [com.nags.operations.data.sync.SyncScheduler]):
 *
 *  • Combine `(signedIn, online)` as the cancellation gate; `hasPending` is only a wake-up.
 *  • Process one row at a time, FIFO, so a stuck submission never starves the queue order.
 *  • Per-row attempt-driven exponential backoff.
 *  • Terminal transitions (Succeeded / Failed / Conflict) and the retryable transition
 *    (back to Pending) are explicit so the Sync Center always knows why a row is queued.
 *
 * The worker does **not** write to `flights_*` directly — the server's SignalR echo flows
 * through [SyncCoordinator.applyChange] like every other change, keeping the single-writer
 * rule intact. `refreshAll` after success is a belt-and-braces for SignalR downtime.
 */
class OutboxWorker(
    private val tokenStore: TokenStore,
    private val networkMonitor: NetworkMonitor,
    private val outboxRepository: WorkOrderOutboxRepository,
    private val mobileApi: MobileApi,
    private val syncCoordinator: SyncCoordinator,
    private val appScope: CoroutineScope,
) {
    @Volatile private var loop: Job? = null
    private val lifecycleLock = Any()
    private val drainMutex = Mutex()
    private val retryAfterEpochMs = mutableMapOf<String, Long>()

    private val json = Json {
        ignoreUnknownKeys = true
        encodeDefaults = true
    }

    /** Idempotent. Repeated [start] calls after the first are no-ops. */
    fun start() {
        synchronized(lifecycleLock) {
            if (loop?.isActive == true) return
            loop = appScope.launch {
                runCatching { outboxRepository.cleanupOrphanedAttachmentDirectories() }
                    .onFailure { Log.w(TAG, "Failed to clean orphaned outbox files", it) }
                runCatching { outboxRepository.cleanupSucceededRows() }
                    .onFailure { Log.w(TAG, "Failed to finish acknowledged outbox cleanup", it) }
            combine(
                tokenStore.accessTokenFlow,
                networkMonitor.isOnline,
            ) { token, online -> token != null && online }
                .distinctUntilChanged()
                .collectLatest { canDrain ->
                    if (!canDrain) return@collectLatest

                    // Sweep rows a prior run left in STATUS_SENDING (process death, offline
                    // mid-POST) back to Pending so this drain picks them up.
                    runCatching { recoverInterruptedSends() }
                        .onFailure { Log.w(TAG, "Failed to recover interrupted sends", it) }

                    outboxRepository.observeHasPending()
                        .collect { hasPending ->
                            if (hasPending) drainUntilEmpty()
                        }
                }
            }
        }
    }

    /** Cancels and joins an in-flight POST before logout/account cleanup can touch the queue. */
    suspend fun stop() {
        val job = synchronized(lifecycleLock) {
            loop.also { loop = null }
        }
        job?.cancelAndJoin()
        retryAfterEpochMs.clear()
    }

    private suspend fun drainUntilEmpty() {
        while (true) {
            val step = drainMutex.withLock { nextForegroundStepLocked() }
            if (step.refreshCache) {
                // Belt-and-braces: if SignalR is down, refreshAll picks up the accepted row.
                runCatching { syncCoordinator.refreshAll() }
            }
            when (step) {
                ForegroundStep.Complete -> return
                is ForegroundStep.Wait -> delay(step.delayMs)
                is ForegroundStep.Processed -> Unit
            }
        }
    }

    /**
     * Bounded WorkManager pass. It never sleeps: retryable rows return [PersistentDrainResult.Retry]
     * so the OS owns connectivity-aware exponential backoff. The same [drainMutex] guards the
     * foreground loop, preventing two process-local uploaders from claiming one row.
     */
    internal suspend fun drainForBackground(): PersistentDrainResult {
        if (tokenStore.getAccessToken().isNullOrBlank()) return PersistentDrainResult.Complete

        val state = drainMutex.withLock {
            if (tokenStore.getAccessToken().isNullOrBlank()) {
                return@withLock BackgroundDrainState(signedIn = false)
            }

            outboxRepository.recoverInterruptedSends()
            val batch = outboxRepository.pendingFifo().take(MAX_BACKGROUND_ROWS_PER_RUN)
            var signedIn = true
            var sawRetryable = false

            for (snapshotRow in batch) {
                if (tokenStore.getAccessToken().isNullOrBlank()) {
                    signedIn = false
                    break
                }

                // Another drain may have consumed the snapshot row while this worker waited.
                val row = outboxRepository.getById(snapshotRow.clientMutationId)
                    ?.takeIf { it.status == WorkOrderOutboxEntity.STATUS_PENDING }
                    ?: continue
                when (processRowLocked(row, scheduleRetryWakeup = false)) {
                    ProcessResult.Succeeded -> Unit
                    ProcessResult.Terminal -> Unit
                    ProcessResult.Retryable -> sawRetryable = true
                }
            }

            BackgroundDrainState(
                signedIn = signedIn,
                sawRetryable = sawRetryable,
                pendingRemaining = outboxRepository.pendingFifo().isNotEmpty(),
            )
        }

        return backgroundDrainDecision(
            signedIn = state.signedIn,
            sawRetryable = state.sawRetryable,
            pendingRemaining = state.pendingRemaining,
        )
    }

    private suspend fun recoverInterruptedSends() {
        drainMutex.withLock { outboxRepository.recoverInterruptedSends() }
    }

    private suspend fun nextForegroundStepLocked(): ForegroundStep {
        val pending = outboxRepository.pendingFifo()
        if (pending.isEmpty()) return ForegroundStep.Complete

        val liveIds = pending.mapTo(mutableSetOf()) { it.clientMutationId }
        retryAfterEpochMs.keys.retainAll(liveIds)
        val now = System.currentTimeMillis()
        val row = selectEligiblePending(pending, retryAfterEpochMs, now)
        if (row == null) {
            val nextDue = pending.minOf { retryAfterEpochMs[it.clientMutationId] ?: now }
            return ForegroundStep.Wait((nextDue - now).coerceAtLeast(50L))
        }

        return when (processRowLocked(row, scheduleRetryWakeup = true)) {
            ProcessResult.Succeeded -> {
                retryAfterEpochMs.remove(row.clientMutationId)
                ForegroundStep.Processed(refreshCache = true)
            }
            ProcessResult.Terminal -> {
                retryAfterEpochMs.remove(row.clientMutationId)
                ForegroundStep.Processed(refreshCache = false)
            }
            ProcessResult.Retryable -> {
                // Defer this row, then immediately give later eligible rows a chance. Strict FIFO
                // retrying made one unavailable flight permanently starve the queue.
                retryAfterEpochMs[row.clientMutationId] =
                    System.currentTimeMillis() + backoffMs(row.attempts + 1)
                ForegroundStep.Processed(refreshCache = false)
            }
        }
    }

    private suspend fun processRowLocked(
        row: WorkOrderOutboxEntity,
        scheduleRetryWakeup: Boolean,
    ): ProcessResult {
        val outcome = runCatching { submit(row) }
            .getOrElse { e ->
                if (e is CancellationException) throw e
                Log.w(TAG, "Outbox submission threw unexpectedly", e)
                Outcome.Retry("Unexpected error: ${e.message ?: e.javaClass.simpleName}")
            }

        return when (outcome) {
            is Outcome.Succeeded -> {
                // A 2xx (including an idempotent replay) is authoritative. Remove the row and
                // attachment files now rather than waiting for a SignalR echo that may be missed.
                outboxRepository.markSucceeded(row, outcome.serverWorkOrderId)
                runCatching { outboxRepository.deleteAndCleanup(row.clientMutationId) }
                    .onFailure { Log.w(TAG, "Server accepted row; deferred local cleanup", it) }
                ProcessResult.Succeeded
            }
            is Outcome.Failed -> {
                outboxRepository.markFailed(row, outcome.message)
                ProcessResult.Terminal
            }
            is Outcome.Conflict -> {
                outboxRepository.markConflict(row, outcome.message)
                ProcessResult.Terminal
            }
            is Outcome.Retry -> {
                outboxRepository.markPendingAfterRetry(
                    row = row,
                    error = outcome.message,
                    schedulePersistentWork = scheduleRetryWakeup,
                )
                ProcessResult.Retryable
            }
        }
    }

    private suspend fun submit(row: WorkOrderOutboxEntity): Outcome {
        outboxRepository.markSending(row)

        val payload = runCatching {
            json.decodeFromString(OutboxPayload.serializer(), row.payloadJson)
        }.getOrElse { e ->
            return Outcome.Failed("Could not decode queued submission: ${e.message}")
        }

        val attachmentsDir = row.attachmentsDir?.let { File(it) }
        val outcome = try {
            when (payload.kind) {
                OutboxPayload.Kind.ForFlight -> {
                    val response = mobileApi.createWorkOrderForFlight(
                        flightId = row.flightId,
                        body = MobileWorkOrderWriteRequest(
                            clientMutationId = row.clientMutationId,
                            workOrder = payload.requireWorkOrder().toWire(attachmentsDir),
                            baseRowVersion = null,
                        ),
                    )
                    Outcome.Succeeded(response.workOrderId)
                }
                OutboxPayload.Kind.ScratchAdHoc -> {
                    val scratch = payload.scratchFlight
                        ?: return Outcome.Failed("Scratch row missing flight block.")
                    val response = mobileApi.createWorkOrderFromScratch(
                        body = MobileScratchWorkOrderRequest(
                            clientMutationId = row.clientMutationId,
                            clientFlightId = row.clientFlightId
                                ?: return Outcome.Failed("Scratch row missing clientFlightId"),
                            customerId = scratch.customerId,
                            flightNumber = scratch.flightNumber,
                            scheduledArrivalUtc = scratch.staIso,
                            scheduledDepartureUtc = scratch.stdIso,
                            aircraftTypeId = scratch.aircraftTypeId,
                            plannedServiceIds = scratch.plannedServiceIds,
                            workOrder = payload.requireWorkOrder().toWire(attachmentsDir),
                        ),
                    )
                    Outcome.Succeeded(response.workOrderId)
                }
                OutboxPayload.Kind.UpdateExisting -> {
                    val woId = row.serverWorkOrderId
                        ?: return Outcome.Failed("Update row missing server work order id.")
                    mobileApi.updateWorkOrder(
                        workOrderId = woId,
                        body = MobileWorkOrderWriteRequest(
                            clientMutationId = row.clientMutationId,
                            workOrder = payload.requireWorkOrder().toWire(attachmentsDir),
                            baseRowVersion = payload.baseRowVersion,
                        ),
                    )
                    Outcome.Succeeded(woId)
                }
                OutboxPayload.Kind.ReturnToRamp -> {
                    val woId = row.serverWorkOrderId
                        ?: return Outcome.Failed("Return-to-ramp row missing server work order id.")
                    val workOrder = payload.requireWorkOrder()
                    mobileApi.recordReturnToRamp(
                        workOrderId = woId,
                        body = MobileReturnToRampRequest(
                            clientMutationId = row.clientMutationId,
                            serviceLines = workOrder.serviceLines.map { it.toWire() },
                            tasks = workOrder.tasks.map { it.toWire(attachmentsDir) },
                        ),
                    )
                    Outcome.Succeeded(woId)
                }
                OutboxPayload.Kind.CancelFlight -> {
                    val cancel = payload.cancelFlight
                        ?: return Outcome.Failed("Cancel row missing cancellation details.")
                    val response = mobileApi.cancelFlight(
                        flightId = row.flightId,
                        body = MobileCancelFlightRequest(
                            clientMutationId = row.clientMutationId,
                            canceledAtUtc = cancel.canceledAtIso,
                            reason = cancel.reason,
                        ),
                    )
                    Outcome.Succeeded(response.workOrderId)
                }
            }
        } catch (e: ApiException) {
            return classifyHttp(e)
        } catch (e: MissingQueuedAttachmentException) {
            return Outcome.Failed(e.message ?: "A queued attachment is no longer available on this device.")
        } catch (e: Exception) {
            if (e is CancellationException) throw e
            return Outcome.Retry(e.message ?: e.javaClass.simpleName)
        }

        return outcome
    }

    private fun classifyHttp(e: ApiException): Outcome {
        val code = e.statusCode
        return when (outboxHttpDisposition(code)) {
            OutboxHttpDisposition.Conflict ->
                Outcome.Conflict(e.body.ifBlank { "Server rejected as duplicate (409)." })
            OutboxHttpDisposition.Failed ->
                Outcome.Failed(e.body.ifBlank { "Server rejected the submission (HTTP $code)." })
            OutboxHttpDisposition.Retry -> {
                val fallback = if (code == 401) {
                    "Auth refresh required (401)."
                } else {
                    "Temporary server response (HTTP $code)."
                }
                Outcome.Retry(e.body.ifBlank { fallback })
            }
        }
    }

    /** Work-order-shaped payloads always carry a body; only [OutboxPayload.Kind.CancelFlight] omits it. */
    private fun OutboxPayload.requireWorkOrder(): OutboxPayload.WorkOrderInput =
        workOrder ?: error("Payload of kind $kind is missing its work order body")

    private fun OutboxPayload.WorkOrderInput.toWire(attachmentsDir: File?) = WorkOrderWireRequest(
        type = type,
        actualFlightNumber = actualFlightNumber,
        aircraftTypeId = aircraftTypeId,
        aircraftTailNumber = aircraftTailNumber,
        actualArrivalUtc = ataIso,
        actualDepartureUtc = atdIso,
        canceledAtUtc = canceledAtIso,
        cancellationReason = cancellationReason,
        remarks = remarks,
        serviceLines = serviceLines.map { it.toWire() },
        tasks = tasks.map { it.toWire(attachmentsDir) },
        customerSignature = customerSignaturePngBase64?.let {
            WorkOrderSignatureInput(
                base64Content = it,
                fileName = "customer-signature.png",
                contentType = "image/png",
            )
        },
    )

    private fun OutboxPayload.ServiceLineInput.toWire() = WorkOrderServiceLineInput(
        serviceId = serviceId,
        performedByStaffMemberId = performedByStaffMemberId,
        fromUtc = fromIso,
        toUtc = toIso,
        description = description,
    )

    private fun OutboxPayload.TaskInput.toWire(attachmentsDir: File?) = WorkOrderTaskInput(
        id = id,
        taskType = taskType,
        description = description,
        fromUtc = fromIso,
        toUtc = toIso,
        employeeIds = employeeIds,
        tools = tools.map { WorkOrderTaskResourceInput(toolId = it.itemId, quantity = it.quantity) },
        materials = materials.map { WorkOrderTaskResourceInput(materialId = it.itemId, quantity = it.quantity) },
        generalSupports = generalSupports.map { WorkOrderTaskResourceInput(generalSupportId = it.itemId, quantity = it.quantity) },
        attachments = attachments.map { it.toWire(attachmentsDir) },
    )

    private fun OutboxPayload.AttachmentInput.toWire(attachmentsDir: File?): WorkOrderTaskAttachmentInput {
        val directory = attachmentsDir ?: throw MissingQueuedAttachmentException(
            "Queued attachment '$fileName' is missing its durable storage directory. " +
                "Keep this failed item for review or discard it and submit again.",
        )
        val file = File(directory, relativePath)
        if (!file.isFile) {
            throw MissingQueuedAttachmentException(
                "Queued attachment '$fileName' is no longer available on this device. " +
                    "Keep this failed item for review or discard it and submit again.",
            )
        }
        if (file.length() <= 0L || (sizeBytes > 0L && file.length() != sizeBytes)) {
            throw MissingQueuedAttachmentException(
                "Queued attachment '$fileName' is incomplete on this device. " +
                    "Keep this failed item for review or discard it and submit again.",
            )
        }
        val bytes = file.readBytes()
        return WorkOrderTaskAttachmentInput(
            kind = kind,
            base64Content = Base64.encodeToString(bytes, Base64.NO_WRAP),
            fileName = fileName,
            contentType = contentType,
        )
    }

    /** Capped exponential backoff so the radio doesn't spin on a flapping server. */
    private fun backoffMs(attempt: Int): Long {
        val base = 2_000L * (1L shl minOf(attempt, 6))
        return base.coerceAtMost(MAX_BACKOFF_MS)
    }

    /** Promotes a Failed/Conflict row back to pending (the Sync Center "Retry" affordance). */
    suspend fun requeueFailed(clientMutationId: String) {
        val row = outboxRepository.getById(clientMutationId) ?: return
        if (row.status == WorkOrderOutboxEntity.STATUS_FAILED ||
            row.status == WorkOrderOutboxEntity.STATUS_CONFLICT
        ) {
            outboxRepository.markPendingAfterRetry(row, error = null)
        }
    }

    companion object {
        private const val TAG = "OutboxWorker"
        private const val MAX_BACKOFF_MS: Long = 5L * 60L * 1_000L
        private const val MAX_BACKGROUND_ROWS_PER_RUN = 8
    }

    private sealed interface ForegroundStep {
        val refreshCache: Boolean

        data object Complete : ForegroundStep {
            override val refreshCache = false
        }

        data class Wait(val delayMs: Long) : ForegroundStep {
            override val refreshCache = false
        }

        data class Processed(override val refreshCache: Boolean) : ForegroundStep
    }

    private enum class ProcessResult { Succeeded, Terminal, Retryable }

    private data class BackgroundDrainState(
        val signedIn: Boolean,
        val sawRetryable: Boolean = false,
        val pendingRemaining: Boolean = false,
    )

    private sealed interface Outcome {
        data class Succeeded(val serverWorkOrderId: String) : Outcome
        data class Failed(val message: String) : Outcome
        data class Conflict(val message: String) : Outcome
        data class Retry(val message: String?) : Outcome
    }

    private class MissingQueuedAttachmentException(message: String) : Exception(message)
}

internal fun selectEligiblePending(
    pending: List<WorkOrderOutboxEntity>,
    retryAfterEpochMs: Map<String, Long>,
    nowEpochMs: Long,
): WorkOrderOutboxEntity? =
    pending.firstOrNull { (retryAfterEpochMs[it.clientMutationId] ?: 0L) <= nowEpochMs }

internal enum class PersistentDrainResult { Complete, Retry }

internal enum class OutboxHttpDisposition { Retry, Conflict, Failed }

/** Keeps timeouts and rate limits in the automatic offline-delivery path. */
internal fun outboxHttpDisposition(statusCode: Int): OutboxHttpDisposition = when {
    statusCode == 409 -> OutboxHttpDisposition.Conflict
    statusCode == 401 || statusCode == 408 || statusCode == 425 || statusCode == 429 ->
        OutboxHttpDisposition.Retry
    statusCode in 400..499 -> OutboxHttpDisposition.Failed
    else -> OutboxHttpDisposition.Retry
}

/** Pure WorkManager state mapping kept separate for focused retry/terminal-row tests. */
internal fun backgroundDrainDecision(
    signedIn: Boolean,
    sawRetryable: Boolean,
    pendingRemaining: Boolean,
): PersistentDrainResult = when {
    !signedIn -> PersistentDrainResult.Complete
    sawRetryable || pendingRemaining -> PersistentDrainResult.Retry
    else -> PersistentDrainResult.Complete
}
