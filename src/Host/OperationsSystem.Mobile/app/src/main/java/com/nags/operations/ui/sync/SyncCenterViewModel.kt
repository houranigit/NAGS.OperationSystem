package com.nags.operations.ui.sync

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nags.operations.data.CatalogSyncStatus
import com.nags.operations.data.FlightSyncStatus
import com.nags.operations.data.SharedCatalog
import com.nags.operations.data.network.NetworkMonitor
import com.nags.operations.data.realtime.RealtimeChannelState
import com.nags.operations.data.repo.SharedCatalogsRepository
import com.nags.operations.data.repo.FlightsRepository
import com.nags.operations.data.sync.SyncTarget
import com.nags.operations.ui.util.parseOffsetDateTime
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.flow.stateIn

/** Read-only flight/catalog cache snapshots and shared live connection diagnostics. */
class SyncCenterViewModel internal constructor(
    statuses: Flow<List<CatalogSyncStatus>>,
    val isSyncing: StateFlow<Boolean>,
    val isOnline: StateFlow<Boolean>,
    val realtimeState: StateFlow<RealtimeChannelState>,
    val lastRealtimeEventAt: StateFlow<Long?>,
    private val requestRefresh: () -> Unit,
    coroutineScope: CoroutineScope? = null,
    private val clock: () -> Long = System::currentTimeMillis,
    flightStatuses: Flow<FlightSyncStatus> = flowOf(FlightSyncStatus()),
    private val arrivals: com.nags.operations.data.repo.FlightArrivalsRepository? = null,
    private val departures: com.nags.operations.data.repo.FlightDeparturesRepository? = null,
    private val cancellations: com.nags.operations.data.repo.FlightCancellationsRepository? = null,
) : ViewModel() {
    constructor(
        catalogs: SharedCatalogsRepository,
        flights: FlightsRepository,
        networkMonitor: NetworkMonitor,
        realtimeState: StateFlow<RealtimeChannelState>,
        lastRealtimeEventAt: StateFlow<Long?>,
        refreshCatalogs: () -> Unit,
        syncing: StateFlow<Boolean>,
        arrivalRepository: com.nags.operations.data.repo.FlightArrivalsRepository,
        departureRepository: com.nags.operations.data.repo.FlightDeparturesRepository,
        cancellationRepository: com.nags.operations.data.repo.FlightCancellationsRepository? = null,
    ) : this(
        statuses = catalogs.observeStatus(),
        isSyncing = syncing,
        isOnline = networkMonitor.isOnline,
        realtimeState = realtimeState,
        lastRealtimeEventAt = lastRealtimeEventAt,
        requestRefresh = refreshCatalogs,
        flightStatuses = flights.syncStatus,
        arrivals = arrivalRepository,
        departures = departureRepository,
        cancellations = cancellationRepository,
    )

    private val scope = coroutineScope ?: viewModelScope
    val nowTick: StateFlow<Long> = flow {
        while (true) {
            emit(clock())
            delay(1_000L)
        }
    }.stateIn(scope, SharingStarted.WhileSubscribed(5_000L), clock())

    val rows: StateFlow<List<SyncRowState>> = combine(statuses, flightStatuses, nowTick, ::mobileSyncRows)
        .stateIn(scope, SharingStarted.WhileSubscribed(5_000L), mobileSyncRows(emptyList(), FlightSyncStatus(), clock()))

    val pendingArrivals = (arrivals?.changes ?: flowOf(emptyList()))
        .stateIn(scope, SharingStarted.WhileSubscribed(5_000L), emptyList())
    val arrivalActionError = kotlinx.coroutines.flow.MutableStateFlow<String?>(null)
    val pendingDepartures = (departures?.changes ?: flowOf(emptyList()))
        .stateIn(scope, SharingStarted.WhileSubscribed(5_000L), emptyList())
    val pendingCancellations = (cancellations?.changes ?: flowOf(emptyList()))
        .stateIn(scope, SharingStarted.WhileSubscribed(5_000L), emptyList())
    val cancellationActionFailed = kotlinx.coroutines.flow.MutableStateFlow(false)
    fun retryCancellation(id: String) = cancellationAction { cancellations?.retry(id) }
    fun discardCancellation(id: String) = cancellationAction { cancellations?.discard(id) }
    private fun cancellationAction(action: suspend () -> Unit) = scope.launch {
        try { cancellationActionFailed.value = false; action() }
        catch (cancelled: kotlinx.coroutines.CancellationException) { throw cancelled }
        catch (_: Exception) { cancellationActionFailed.value = true }
    }
    val departureActionFailed = kotlinx.coroutines.flow.MutableStateFlow(false)

    fun retryDeparture(id: String) = departureAction { departures?.retry(id) }
    fun discardDeparture(id: String) = departureAction { departures?.discard(id) }
    private fun departureAction(action: suspend () -> Unit) = scope.launch {
        try { departureActionFailed.value = false; action() }
        catch (cancelled: kotlinx.coroutines.CancellationException) { throw cancelled }
        catch (_: Exception) { departureActionFailed.value = true }
    }

    fun retryArrival(id: String) = arrivalAction { arrivals?.retry(id) }
    fun discardArrival(id: String) = arrivalAction { arrivals?.discard(id) }
    private fun arrivalAction(action: suspend () -> Unit) = scope.launch {
        try { arrivalActionError.value = null; action() }
        catch (cancelled: kotlinx.coroutines.CancellationException) { throw cancelled }
        catch (_: Exception) { arrivalActionError.value = "Could not update the saved arrival. Please try again." }
    }

    fun refreshNow() {
        if (isOnline.value && !isSyncing.value) requestRefresh()
    }
}

data class SyncRowState(
    val target: SyncTarget,
    val rowCount: Int,
    val lastSyncedAt: Long?,
    val lastDurationMs: Long?,
    val lastError: String?,
    val ageMs: Long?,
    val permissionDenied: Boolean,
)

/** Preserve the reference catalog order after the basic Flights card. */
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
            target = SyncTarget.Catalog(kind),
            rowCount = status?.itemCount ?: 0,
            lastSyncedAt = lastSyncedAt,
            lastDurationMs = status?.lastDurationMs,
            lastError = status?.error,
            ageMs = lastSyncedAt?.let { (now - it).coerceAtLeast(0L) },
            permissionDenied = status?.permissionDenied ?: false,
        )
    }
}

internal fun mobileSyncRows(statuses: List<CatalogSyncStatus>, flights: FlightSyncStatus, now: Long): List<SyncRowState> {
    val syncedAt = flights.lastSuccessUtc?.let { value ->
        runCatching { parseOffsetDateTime(value).toInstant().toEpochMilli() }.getOrNull()
    }
    val flightRow = SyncRowState(
        target = SyncTarget.Flights,
        rowCount = flights.itemCount,
        lastSyncedAt = syncedAt,
        lastDurationMs = flights.lastDurationMs,
        lastError = flights.error,
        ageMs = syncedAt?.let { (now - it).coerceAtLeast(0L) },
        permissionDenied = flights.permissionDenied,
    )
    return listOf(flightRow) + catalogSyncRows(statuses, now)
}
