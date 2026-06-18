package com.nags.operations.ui.sync

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nags.operations.data.db.AppDatabase
import com.nags.operations.data.db.entities.SyncStateEntity
import com.nags.operations.data.network.NetworkMonitor
import com.nags.operations.data.realtime.RealtimeChannel
import com.nags.operations.data.realtime.RealtimeChannelState
import com.nags.operations.data.sync.SyncCoordinator
import com.nags.operations.data.sync.SyncTable
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

/**
 * State holder for the Sync Center diagnostics screen. Combines three signals:
 *
 *  • Sync metadata rows (`sync_state` table) — when each table was last synced,
 *    how long it took, and any sticky error from the latest attempt.
 *  • Live row counts per table — one snapshot per refresh of the underlying
 *    table (we re-emit by listening to the table's reactive flow and projecting
 *    to a count).
 *  • The coordinator's `isSyncing` flag and the device's online flag.
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
            database.aogFlightDao().observeAll(),
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
            SyncTable.AogFlights.storageKey to lists[8].size,
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

    fun refreshNow() {
        viewModelScope.launch { coordinator.refreshAll() }
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
