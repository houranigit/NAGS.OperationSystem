package com.nags.operations.ui.sync

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nags.operations.data.db.AppDatabase
import com.nags.operations.data.db.entities.SyncStateEntity
import com.nags.operations.data.db.entities.WorkOrderOutboxEntity
import com.nags.operations.data.network.NetworkMonitor
import com.nags.operations.data.outbox.OutboxPayload
import com.nags.operations.data.outbox.OutboxWorker
import com.nags.operations.data.outbox.WorkOrderOutboxRepository
import com.nags.operations.data.outbox.decodeOutboxPayload
import com.nags.operations.data.realtime.RealtimeChannel
import com.nags.operations.data.realtime.RealtimeChannelState
import com.nags.operations.data.sync.SyncCoordinator
import com.nags.operations.data.sync.SyncTable
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import kotlinx.serialization.json.Json

/**
 * State holder for the Sync Center diagnostics and queued-work recovery screen. Combines:
 *
 *  • Sync metadata rows (`sync_state` table) — when each table was last synced,
 *    how long it took, and any sticky error from the latest attempt.
 *  • Live row counts per table — one snapshot per refresh of the underlying
 *    table (we re-emit by listening to the table's reactive flow and projecting
 *    to a count).
 *  • The coordinator's `isSyncing` flag and the device's online flag.
 *  • Durable outbox rows, projected with cached flight numbers and safe terminal actions.
 *
 * The combined [rows] flow is what the screen renders — one [SyncRowState] per
 * [SyncTable] in stable display order so the list never jumps around between
 * refreshes.
 */
class SyncCenterViewModel(
    database: AppDatabase,
    private val coordinator: SyncCoordinator,
    networkMonitor: NetworkMonitor,
    realtimeChannel: RealtimeChannel,
    private val outboxRepository: WorkOrderOutboxRepository,
    private val outboxWorker: OutboxWorker,
) : ViewModel() {

    /** Pulses every second so the "synced N s ago" labels stay current without manual refresh. */
    private val ticker: StateFlow<Long> = flow {
        while (true) {
            emit(System.currentTimeMillis())
            kotlinx.coroutines.delay(1_000)
        }
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), System.currentTimeMillis())

    /**
     * Per-table counts. We listen to each DAO's flow so the count reflects the
     * real table size at any moment (not just after a sync). The counts are
     * folded into a Map keyed by [SyncTable.storageKey] so the UI can look up
     * by enum constant.
     */
    private val counts: StateFlow<Map<String, Int>> = combine(
        listOf(
            database.serviceDao().observeAll(),
            database.toolDao().observeAll(),
            database.materialDao().observeAll(),
            database.generalSupportDao().observeAll(),
            database.customerDao().observeAll(),
            database.aircraftTypeDao().observeAll(),
            database.employeeDao().observeAll(),
            database.flightDao().observeAll(),
            database.perLandingFlightDao().observeAll(),
            database.adHocFlightDao().observeAll(),
        ),
    ) { lists ->
        mapOf(
            SyncTable.Services.storageKey to lists[0].size,
            SyncTable.Tools.storageKey to lists[1].size,
            SyncTable.Materials.storageKey to lists[2].size,
            SyncTable.GeneralSupports.storageKey to lists[3].size,
            SyncTable.Customers.storageKey to lists[4].size,
            SyncTable.AircraftTypes.storageKey to lists[5].size,
            SyncTable.Employees.storageKey to lists[6].size,
            SyncTable.Flights.storageKey to lists[7].size,
            SyncTable.PerLandingFlights.storageKey to lists[8].size,
            SyncTable.AdHocFlights.storageKey to lists[9].size,
        )
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyMap())

    private val syncMetadata: StateFlow<Map<String, SyncStateEntity>> =
        database.syncStateDao().observeAll()
            .let { source ->
                kotlinx.coroutines.flow.flow {
                    source.collect { rows -> emit(rows.associateBy { it.tableName }) }
                }
            }
            .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyMap())

    /** Snapshot of every table for the diagnostics list, in fixed display order. */
    val rows: StateFlow<List<SyncRowState>> = combine(
        counts,
        syncMetadata,
        ticker,
    ) { byKey, byTable, now ->
        SyncTable.entries.map { t ->
            val meta = byTable[t.storageKey]
            SyncRowState(
                table = t,
                rowCount = byKey[t.storageKey] ?: 0,
                lastSyncedAt = meta?.lastSyncedAt,
                lastDurationMs = meta?.lastDurationMs,
                lastError = meta?.lastError,
                ageMs = meta?.lastSyncedAt?.let { now - it },
            )
        }
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    val isSyncing: StateFlow<Boolean> = coordinator.isSyncing
    val isOnline: StateFlow<Boolean> = networkMonitor.isOnline

    /** SignalR session status — feeds the Sync Center "Live channel" pill. */
    val realtimeState: StateFlow<RealtimeChannelState> = realtimeChannel.state

    /**
     * Wall-clock of the most recent SignalR `change` envelope we applied.
     * The screen ages it against [ticker] so the operator can see "received
     * 12s ago" as a live signal that the push stream is healthy.
     */
    val lastRealtimeEventAt: StateFlow<Long?> = realtimeChannel.lastEventAt

    /** Re-exposed for the screen to age timestamps off the same once-per-second tick. */
    val nowTick: StateFlow<Long> = ticker

    /**
     * Resolve opaque flight ids to the number an on-site user recognizes. Scratch rows do not
     * exist in a flight cache yet, so [mapOutboxRow] falls back to the number in their payload.
     */
    private val flightNumberById: StateFlow<Map<String, String>> = combine(
        database.flightDao().observeAll(),
        database.perLandingFlightDao().observeAll(),
        database.adHocFlightDao().observeAll(),
    ) { myFlights, perLandingFlights, adHocFlights ->
        buildMap {
            myFlights.forEach { put(it.id, it.flightNumber) }
            perLandingFlights.forEach { put(it.id, it.flightNumber) }
            adHocFlights.forEach { put(it.id, it.flightNumber) }
        }
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyMap())

    /** Every durable queued mutation, including terminal rows that need user recovery. */
    val outboxRows: StateFlow<List<OutboxRowState>> = combine(
        outboxRepository.observeAll(),
        flightNumberById,
    ) { entities, flightNumbers ->
        entities.map { entity -> mapOutboxRow(entity, flightNumbers) }
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    private val _outboxActionState = MutableStateFlow(OutboxActionState())
    val outboxActionState: StateFlow<OutboxActionState> = _outboxActionState.asStateFlow()

    fun refreshNow() {
        viewModelScope.launch { coordinator.refreshAll() }
    }

    /**
     * Retries only a terminal ordinary failure. A conflict represents stale or colliding input;
     * replaying that same payload cannot resolve it and could overwrite newer portal work.
     */
    fun retryFailed(clientMutationId: String) {
        runOutboxAction(clientMutationId, OutboxAction.Retry) {
            val row = outboxRepository.getById(clientMutationId)
                ?: error("This queued item no longer exists.")
            check(row.status == WorkOrderOutboxEntity.STATUS_FAILED) {
                "Only failed items can be retried. Conflicts must be reviewed and submitted again."
            }
            outboxWorker.requeueFailed(clientMutationId)
        }
    }

    /**
     * Removes terminal work and its durable attachment directory. Pending/sending work deliberately
     * has no discard action because it may already be in an idempotent request to the server.
     */
    fun discardTerminal(clientMutationId: String) {
        runOutboxAction(clientMutationId, OutboxAction.Discard) {
            val row = outboxRepository.getById(clientMutationId)
                ?: error("This queued item no longer exists.")
            check(
                row.status == WorkOrderOutboxEntity.STATUS_FAILED ||
                    row.status == WorkOrderOutboxEntity.STATUS_CONFLICT,
            ) { "Only failed or conflicted items can be discarded." }
            outboxRepository.deleteAndCleanup(clientMutationId)
        }
    }

    fun clearOutboxActionError() {
        _outboxActionState.value = _outboxActionState.value.copy(errorMessage = null)
    }

    private fun runOutboxAction(
        clientMutationId: String,
        action: OutboxAction,
        block: suspend () -> Unit,
    ) {
        if (_outboxActionState.value.isWorking) return
        _outboxActionState.value = OutboxActionState(
            activeClientMutationId = clientMutationId,
            activeAction = action,
        )
        viewModelScope.launch {
            try {
                block()
                _outboxActionState.value = OutboxActionState()
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                _outboxActionState.value = OutboxActionState(
                    errorMessage = e.message ?: "The queued item could not be updated.",
                )
            }
        }
    }
}

/**
 * Flat row state for the diagnostics list. Field-for-field correspondence with
 * the visible card so the UI side stays "render the data" with no derivation.
 */
data class SyncRowState(
    val table: SyncTable,
    val rowCount: Int,
    val lastSyncedAt: Long?,
    val lastDurationMs: Long?,
    val lastError: String?,
    val ageMs: Long?,
)

data class OutboxRowState(
    val clientMutationId: String,
    val kindLabel: String,
    val flightLabel: String,
    val flightId: String,
    val status: OutboxStatus,
    val attempts: Int,
    val lastError: String?,
    val canRetry: Boolean,
    val canDiscard: Boolean,
)

enum class OutboxStatus(val label: String) {
    Pending("Pending"),
    Sending("Sending"),
    Failed("Failed"),
    Conflict("Conflict"),
    Accepted("Accepted"),
    Unknown("Unknown"),
}

enum class OutboxAction {
    Retry,
    Discard,
}

data class OutboxActionState(
    val activeClientMutationId: String? = null,
    val activeAction: OutboxAction? = null,
    val errorMessage: String? = null,
) {
    val isWorking: Boolean get() = activeClientMutationId != null
}

private val outboxJson = Json { ignoreUnknownKeys = true }

/** Pure entity-to-UI projection kept outside the ViewModel for focused JVM coverage. */
internal fun mapOutboxRow(
    entity: WorkOrderOutboxEntity,
    flightNumberById: Map<String, String>,
): OutboxRowState {
    val payload = runCatching {
        decodeOutboxPayload(outboxJson, entity.payloadJson)
    }.getOrNull()

    val kindLabel = when (payload?.kind) {
        OutboxPayload.Kind.ForFlight -> "Create work order"
        OutboxPayload.Kind.ScratchAdHoc -> "Create ad-hoc work order"
        OutboxPayload.Kind.UpdateExisting -> "Update work order"
        OutboxPayload.Kind.ReturnToRamp -> "Return to ramp"
        OutboxPayload.Kind.CancelFlight -> "Cancel flight"
        null -> "Queued operation"
    }
    val recognizedFlightNumber = flightNumberById[entity.flightId]
        ?.trim()
        ?.takeIf { it.isNotEmpty() }
        ?: payload?.scratchFlight?.flightNumber?.trim()?.takeIf { it.isNotEmpty() }
        ?: payload?.workOrder?.actualFlightNumber?.trim()?.takeIf { it.isNotEmpty() }
    val status = when (entity.status) {
        WorkOrderOutboxEntity.STATUS_PENDING -> OutboxStatus.Pending
        WorkOrderOutboxEntity.STATUS_SENDING -> OutboxStatus.Sending
        WorkOrderOutboxEntity.STATUS_FAILED -> OutboxStatus.Failed
        WorkOrderOutboxEntity.STATUS_CONFLICT -> OutboxStatus.Conflict
        WorkOrderOutboxEntity.STATUS_SUCCEEDED -> OutboxStatus.Accepted
        else -> OutboxStatus.Unknown
    }

    return OutboxRowState(
        clientMutationId = entity.clientMutationId,
        kindLabel = kindLabel,
        flightLabel = recognizedFlightNumber?.let { "Flight $it" }
            ?: "Flight ${shortIdentifier(entity.flightId)}",
        flightId = entity.flightId,
        status = status,
        attempts = entity.attempts,
        lastError = entity.lastError,
        canRetry = status == OutboxStatus.Failed,
        canDiscard = status == OutboxStatus.Failed || status == OutboxStatus.Conflict,
    )
}

private fun shortIdentifier(value: String): String =
    value.take(8).ifBlank { "unknown" }
