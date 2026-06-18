package com.nags.operations.data.outbox

import android.util.Base64
import android.util.Log
import com.nags.operations.data.ApiException
import com.nags.operations.data.TokenStore
import com.nags.operations.data.api.MobileApi
import com.nags.operations.data.api.MobileCancelFlightRequest
import com.nags.operations.data.api.MobileCreateFromScratchRequest
import com.nags.operations.data.api.MobileCreateWorkOrderRequest
import com.nags.operations.data.api.MobileUpdateWorkOrderRequest
import com.nags.operations.data.api.MobileReturnToRampRequest
import com.nags.operations.data.api.MobileTaskAttachmentInput
import com.nags.operations.data.api.MobileWorkOrderServiceLineInput
import com.nags.operations.data.api.MobileWorkOrderTaskInput
import com.nags.operations.data.db.entities.WorkOrderOutboxEntity
import com.nags.operations.data.network.NetworkMonitor
import com.nags.operations.data.sync.SyncCoordinator
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.launch
import kotlinx.serialization.json.Json
import java.io.File

/**
 * Drains the offline work-order outbox into the v2 mobile API.
 *
 * Mirrors the discipline of [com.nags.operations.data.sync.SyncScheduler]:
 *
 *  • Combine `(signedIn, online, hasPending)` so the worker is only alive
 *    while there's something to do and the network can carry it.
 *  • Process one row at a time, FIFO, so a stuck submission can never
 *    starve the rest of the queue out of order.
 *  • Per-row attempt-driven exponential backoff — a flaky server doesn't
 *    spin the radio.
 *  • The four terminal status transitions ([WorkOrderOutboxEntity.STATUS_SUCCEEDED],
 *    [WorkOrderOutboxEntity.STATUS_FAILED], [WorkOrderOutboxEntity.STATUS_CONFLICT])
 *    and one retryable transition (back to pending) are explicit so the
 *    Sync Center always knows exactly why a row is sitting in the queue.
 *
 * The worker does **not** write to `flights_*` directly — the server's SignalR
 * echo flows through [SyncCoordinator.applyChange] like every other change,
 * which keeps the single-writer rule intact. We only kick a `refreshAll` after
 * a successful submission as a belt-and-braces in case the SignalR transport
 * is temporarily down.
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

    private val json = Json {
        ignoreUnknownKeys = true
        encodeDefaults = true
    }

    /** Idempotent. Repeated [start] calls after the first are no-ops. */
    fun start() {
        if (loop?.isActive == true) return
        loop = appScope.launch {
            // Cancel only when sign-in or connectivity transitions away from
            // "can drain". Including `hasPending` in this gate is a footgun:
            // the very act of marking a row Sending flips it to false, which
            // would `collectLatest`-cancel our own in-flight POST and leave
            // the row stuck as "Syncing…" on the flight card. `hasPending`
            // belongs in the inner wake-up loop, not in the cancellation key.
            combine(
                tokenStore.accessTokenFlow,
                networkMonitor.isOnline,
            ) { token, online -> token != null && online }
                .distinctUntilChanged()
                .collectLatest { canDrain ->
                    if (!canDrain) return@collectLatest

                    // Sweep up any rows that an earlier worker run left in
                    // STATUS_SENDING (process death, offline-while-posting,
                    // user signed out mid-send). Flip them back to Pending
                    // so the next drain picks them up.
                    runCatching { outboxRepository.recoverInterruptedSends() }
                        .onFailure { Log.w(TAG, "Failed to recover interrupted sends", it) }

                    // hasPending is a wake-up signal, not a gate: `collect` is
                    // sequential, so the next emission won't fire until the
                    // current drain returns. drainUntilEmpty already loops
                    // internally until the queue is empty, so we only need to
                    // (re)trigger it on transitions into "there is something
                    // to do".
                    outboxRepository.observeHasPending()
                        .collect { hasPending ->
                            if (hasPending) drainUntilEmpty()
                        }
                }
        }
    }

    /**
     * Pops rows one at a time until either the queue is empty or a row gets
     * marked terminal / retryable. When we re-queue a row after a transient
     * failure we wait the backoff window and try again from the top so newer
     * rows don't starve.
     */
    private suspend fun drainUntilEmpty() {
        while (true) {
            val row = outboxRepository.nextPending() ?: return
            val outcome = runCatching { submit(row) }
                .getOrElse { e ->
                    if (e is CancellationException) throw e
                    Log.w(TAG, "Outbox submission threw unexpectedly", e)
                    Outcome.Retry("Unexpected error: ${e.message ?: e.javaClass.simpleName}")
                }

            when (outcome) {
                is Outcome.Succeeded -> {
                    outboxRepository.markSucceeded(row, outcome.serverWorkOrderId)
                    // Belt-and-braces: if SignalR is down, refreshAll picks up the new row.
                    // The realtime channel will normally apply the echo within a second or two.
                    runCatching { syncCoordinator.refreshAll() }
                }
                is Outcome.Failed -> outboxRepository.markFailed(row, outcome.message)
                is Outcome.Conflict -> outboxRepository.markConflict(row, outcome.message)
                is Outcome.Retry -> {
                    outboxRepository.markPendingAfterRetry(row, outcome.message)
                    delay(backoffMs(row.attempts + 1))
                }
            }
        }
    }

    private suspend fun submit(row: WorkOrderOutboxEntity): Outcome {
        outboxRepository.markSending(row)

        val payload = runCatching {
            json.decodeFromString(OutboxPayload.serializer(), row.payloadJson)
        }.getOrElse { e ->
            // Truly broken — the row was written by an older version we can't decode.
            // Mark Failed so the user sees it in Sync Center rather than letting the
            // worker loop forever on a row we'll never understand.
            return Outcome.Failed("Could not decode queued submission: ${e.message}")
        }

        val attachmentsDir = row.attachmentsDir?.let { File(it) }
        val outcome = try {
            when (payload.kind) {
                OutboxPayload.Kind.ForFlight -> {
                    val response = mobileApi.createWorkOrderForFlight(
                        flightId = row.flightId,
                        body = payload.toForFlightRequest(row.clientMutationId, attachmentsDir),
                    )
                    Outcome.Succeeded(response.id)
                }
                OutboxPayload.Kind.ScratchAdHoc -> {
                    val response = mobileApi.createWorkOrderFromScratch(
                        body = payload.toScratchRequest(
                            clientMutationId = row.clientMutationId,
                            clientFlightId = row.clientFlightId
                                ?: return Outcome.Failed("Scratch row missing clientFlightId"),
                            attachmentsDir = attachmentsDir,
                        ),
                    )
                    Outcome.Succeeded(response.id)
                }
                OutboxPayload.Kind.UpdateExisting -> {
                    val woId = row.serverWorkOrderId
                        ?: return Outcome.Failed("Update row missing server work order id.")
                    mobileApi.updateWorkOrder(
                        workOrderId = woId,
                        body = payload.toUpdateRequest(row.clientMutationId, attachmentsDir),
                    )
                    Outcome.Succeeded(woId)
                }
                OutboxPayload.Kind.ReturnToRamp -> {
                    val woId = row.serverWorkOrderId
                        ?: return Outcome.Failed("Return-to-ramp row missing server work order id.")
                    mobileApi.recordReturnToRamp(
                        workOrderId = woId,
                        body = payload.toReturnToRampRequest(row.clientMutationId, attachmentsDir),
                    )
                    Outcome.Succeeded(woId)
                }
                OutboxPayload.Kind.CancelFlight -> {
                    val cancel = payload.cancelFlight
                        ?: return Outcome.Failed("Cancel row missing cancellation time.")
                    val response = mobileApi.cancelFlight(
                        flightId = row.flightId,
                        body = MobileCancelFlightRequest(
                            canceledAt = cancel.canceledAtIso,
                            clientMutationId = row.clientMutationId,
                        ),
                    )
                    Outcome.Succeeded(response.id)
                }
            }
        } catch (e: ApiException) {
            return classifyHttp(e)
        } catch (e: Exception) {
            if (e is CancellationException) throw e
            return Outcome.Retry(e.message ?: e.javaClass.simpleName)
        }

        return outcome
    }

    private fun classifyHttp(e: ApiException): Outcome {
        val code = e.statusCode
        return when {
            code in 200..299 -> Outcome.Retry("Unexpected response: HTTP $code")
            code == 409 -> Outcome.Conflict(e.body.ifBlank { "Server rejected as duplicate (409)." })
            // 401 normally never reaches here — the Ktor Auth plugin refreshes on 401 transparently.
            // If we do see it, the refresh path already cleared the tokens; retrying is the right
            // move because (signedIn = false) will pause the worker until the user re-authenticates.
            code == 401 -> Outcome.Retry("Auth refresh required (401).")
            code in 400..499 -> Outcome.Failed(e.body.ifBlank { "Server rejected the submission (HTTP $code)." })
            else -> Outcome.Retry(e.body.ifBlank { "Server error (HTTP $code)." })
        }
    }

    private fun OutboxPayload.toUpdateRequest(
        clientMutationId: String,
        attachmentsDir: File?,
    ): MobileUpdateWorkOrderRequest {
        val workOrder = requireWorkOrder()
        return MobileUpdateWorkOrderRequest(
            flightNumber = workOrder.flightNumber,
            aircraftTypeId = workOrder.aircraftTypeId,
            aircraftTailNumber = workOrder.aircraftTailNumber,
            ata = workOrder.ata,
            atd = workOrder.atd,
            remarks = workOrder.remarks,
            serviceLines = workOrder.serviceLines.map { it.toWire() },
            tasks = workOrder.tasks.map { it.toWire(attachmentsDir) },
            isCanceled = workOrder.isCanceled,
            cancellationAt = workOrder.cancellationAt,
            customerSignaturePng = workOrder.customerSignaturePngBase64,
            clientMutationId = clientMutationId,
        )
    }

    private fun OutboxPayload.toReturnToRampRequest(
        clientMutationId: String,
        attachmentsDir: File?,
    ): MobileReturnToRampRequest {
        val workOrder = requireWorkOrder()
        return MobileReturnToRampRequest(
            serviceLines = workOrder.serviceLines.map { it.toWire() },
            tasks = workOrder.tasks.map { it.toWire(attachmentsDir) },
            customerSignaturePng = null,
            clientMutationId = clientMutationId,
        )
    }

    private fun OutboxPayload.toForFlightRequest(
        clientMutationId: String,
        attachmentsDir: File?,
    ): MobileCreateWorkOrderRequest {
        val workOrder = requireWorkOrder()
        return MobileCreateWorkOrderRequest(
            flightNumber = workOrder.flightNumber,
            aircraftTypeId = workOrder.aircraftTypeId,
            aircraftTailNumber = workOrder.aircraftTailNumber,
            ata = workOrder.ata,
            atd = workOrder.atd,
            remarks = workOrder.remarks,
            serviceLines = workOrder.serviceLines.map { it.toWire() },
            tasks = workOrder.tasks.map { it.toWire(attachmentsDir) },
            customerSignaturePng = workOrder.customerSignaturePngBase64,
            clientMutationId = clientMutationId,
        )
    }

    private fun OutboxPayload.toScratchRequest(
        clientMutationId: String,
        clientFlightId: String,
        attachmentsDir: File?,
    ): MobileCreateFromScratchRequest {
        val workOrder = requireWorkOrder()
        val scratch = scratchFlight
            ?: error("ScratchAdHoc payload missing scratchFlight block")
        return MobileCreateFromScratchRequest(
            customerId = scratch.customerId,
            flightNumber = workOrder.flightNumber,
            aircraftTypeId = workOrder.aircraftTypeId,
            aircraftTailNumber = workOrder.aircraftTailNumber,
            sta = scratch.sta,
            std = scratch.std,
            isCanceled = scratch.isCanceled,
            cancellationAt = scratch.cancellationAt,
            ata = workOrder.ata,
            atd = workOrder.atd,
            remarks = workOrder.remarks,
            serviceLines = workOrder.serviceLines.map { it.toWire() },
            tasks = workOrder.tasks.map { it.toWire(attachmentsDir) },
            customerSignaturePng = workOrder.customerSignaturePngBase64,
            clientMutationId = clientMutationId,
            clientFlightId = clientFlightId,
        )
    }

    /** Work-order-shaped payloads always carry a body; only [OutboxPayload.Kind.CancelFlight] omits it. */
    private fun OutboxPayload.requireWorkOrder(): OutboxPayload.WorkOrderInput =
        workOrder ?: error("Payload of kind $kind is missing its work order body")

    private fun OutboxPayload.ServiceLineInput.toWire() = MobileWorkOrderServiceLineInput(
        serviceId = serviceId,
        employeeId = employeeId,
        from = fromIso,
        to = toIso,
        description = description,
    )

    private fun OutboxPayload.TaskInput.toWire(attachmentsDir: File?) = MobileWorkOrderTaskInput(
        taskType = taskType,
        description = description,
        from = fromIso,
        to = toIso,
        employeeIds = employeeIds,
        toolIds = toolIds,
        materialIds = materialIds,
        generalSupportIds = generalSupportIds,
        attachments = attachments.map { it.toWire(attachmentsDir) },
    )

    private fun OutboxPayload.AttachmentInput.toWire(attachmentsDir: File?): MobileTaskAttachmentInput {
        val bytes = if (attachmentsDir != null) {
            val file = File(attachmentsDir, relativePath)
            if (file.exists()) file.readBytes() else ByteArray(0)
        } else {
            ByteArray(0)
        }
        return MobileTaskAttachmentInput(
            kind = kind,
            contentType = contentType,
            fileName = fileName,
            base64 = Base64.encodeToString(bytes, Base64.NO_WRAP),
            capturedAt = capturedAtIso,
        )
    }

    /** Capped exponential backoff so the radio doesn't spin on a flapping server. */
    private fun backoffMs(attempt: Int): Long {
        val base = 2_000L * (1L shl minOf(attempt, 6))
        return base.coerceAtMost(MAX_BACKOFF_MS)
    }

    /**
     * Manual trigger for the future "Retry failed" affordance in Sync Center —
     * promotes the named row back to pending so the next gate flip drains it.
     * Defensive: if the gate is already open the row will be picked up on the
     * next iteration of [drainUntilEmpty].
     */
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
    }

    private sealed interface Outcome {
        data class Succeeded(val serverWorkOrderId: String) : Outcome
        data class Failed(val message: String) : Outcome
        data class Conflict(val message: String) : Outcome
        data class Retry(val message: String?) : Outcome
    }
}
