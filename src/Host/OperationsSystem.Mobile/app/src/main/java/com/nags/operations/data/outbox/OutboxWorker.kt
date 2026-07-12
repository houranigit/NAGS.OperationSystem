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
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.launch
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

    private val json = Json {
        ignoreUnknownKeys = true
        encodeDefaults = true
    }

    /** Idempotent. Repeated [start] calls after the first are no-ops. */
    fun start() {
        if (loop?.isActive == true) return
        loop = appScope.launch {
            combine(
                tokenStore.accessTokenFlow,
                networkMonitor.isOnline,
            ) { token, online -> token != null && online }
                .distinctUntilChanged()
                .collectLatest { canDrain ->
                    if (!canDrain) return@collectLatest

                    // Sweep rows a prior run left in STATUS_SENDING (process death, offline
                    // mid-POST) back to Pending so this drain picks them up.
                    runCatching { outboxRepository.recoverInterruptedSends() }
                        .onFailure { Log.w(TAG, "Failed to recover interrupted sends", it) }

                    outboxRepository.observeHasPending()
                        .collect { hasPending ->
                            if (hasPending) drainUntilEmpty()
                        }
                }
        }
    }

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
            // 401 normally never reaches here — the Ktor Auth plugin refreshes transparently.
            code == 401 -> Outcome.Retry("Auth refresh required (401).")
            code in 400..499 -> Outcome.Failed(e.body.ifBlank { "Server rejected the submission (HTTP $code)." })
            else -> Outcome.Retry(e.body.ifBlank { "Server error (HTTP $code)." })
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
        val bytes = if (attachmentsDir != null) {
            val file = File(attachmentsDir, relativePath)
            if (file.exists()) file.readBytes() else ByteArray(0)
        } else {
            ByteArray(0)
        }
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
    }

    private sealed interface Outcome {
        data class Succeeded(val serverWorkOrderId: String) : Outcome
        data class Failed(val message: String) : Outcome
        data class Conflict(val message: String) : Outcome
        data class Retry(val message: String?) : Outcome
    }
}
