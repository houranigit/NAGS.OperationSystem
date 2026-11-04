package com.nags.operations.ui.sync

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nags.operations.data.CatalogSyncStatus
import com.nags.operations.data.SharedCatalog
import com.nags.operations.data.network.NetworkMonitor
import com.nags.operations.data.realtime.RealtimeChannelState
import com.nags.operations.data.repo.SharedCatalogsRepository
import com.nags.operations.ui.util.parseOffsetDateTime
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.flow.stateIn

/** Shared-catalog snapshots and live connection diagnostics for the existing Sync Center. */
class SyncCenterViewModel internal constructor(
    statuses: Flow<List<CatalogSyncStatus>>,
    val isSyncing: StateFlow<Boolean>,
    val isOnline: StateFlow<Boolean>,
    val realtimeState: StateFlow<RealtimeChannelState>,
    val lastRealtimeEventAt: StateFlow<Long?>,
    private val requestRefresh: () -> Unit,
    coroutineScope: CoroutineScope? = null,
    private val clock: () -> Long = System::currentTimeMillis,
) : ViewModel() {
    constructor(
        catalogs: SharedCatalogsRepository,
        networkMonitor: NetworkMonitor,
        realtimeState: StateFlow<RealtimeChannelState>,
        lastRealtimeEventAt: StateFlow<Long?>,
        refreshCatalogs: () -> Unit,
    ) : this(
        statuses = catalogs.observeStatus(),
        isSyncing = catalogs.isSyncing,
        isOnline = networkMonitor.isOnline,
        realtimeState = realtimeState,
        lastRealtimeEventAt = lastRealtimeEventAt,
        requestRefresh = refreshCatalogs,
    )

    private val scope = coroutineScope ?: viewModelScope
    val nowTick: StateFlow<Long> = flow {
        while (true) {
            emit(clock())
            delay(1_000L)
        }
    }.stateIn(scope, SharingStarted.WhileSubscribed(5_000L), clock())

    val rows: StateFlow<List<SyncRowState>> = combine(statuses, nowTick, ::catalogSyncRows)
        .stateIn(scope, SharingStarted.WhileSubscribed(5_000L), catalogSyncRows(emptyList(), clock()))

    fun refreshNow() {
        if (isOnline.value && !isSyncing.value) requestRefresh()
    }
}

data class SyncRowState(
    val table: SharedCatalog,
    val rowCount: Int,
    val lastSyncedAt: Long?,
    val lastDurationMs: Long?,
    val lastError: String?,
    val ageMs: Long?,
    val permissionDenied: Boolean,
)

/** Keep the former catalog card order; workflow caches are not part of shared catalogs. */
private val catalogDisplayOrder = listOf(
    SharedCatalog.AtaChapters,
    SharedCatalog.Services,
    SharedCatalog.Tools,
    SharedCatalog.Materials,
    SharedCatalog.GeneralSupports,
    SharedCatalog.Customers,
    SharedCatalog.AircraftTypes,
    SharedCatalog.StationStaff,
)

internal fun catalogSyncRows(statuses: List<CatalogSyncStatus>, now: Long): List<SyncRowState> {
    val byCatalog = statuses.associateBy { it.kind }
    return catalogDisplayOrder.map { kind ->
        val status = byCatalog[kind]
        val lastSyncedAt = status?.lastSuccessUtc?.let { value ->
            runCatching { parseOffsetDateTime(value).toInstant().toEpochMilli() }.getOrNull()
        }
        SyncRowState(
            table = kind,
            rowCount = status?.itemCount ?: 0,
            lastSyncedAt = lastSyncedAt,
            lastDurationMs = status?.lastDurationMs,
            lastError = status?.error,
            ageMs = lastSyncedAt?.let { (now - it).coerceAtLeast(0L) },
            permissionDenied = status?.permissionDenied ?: false,
        )
    }
}
