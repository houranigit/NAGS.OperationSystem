package com.nags.operations.ui.sync

import com.nags.operations.data.userMessage

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
    private val services: com.nags.operations.data.repo.FlightServicesRepository? = null,
    private val tasks: com.nags.operations.data.repo.FlightTasksRepository? = null,
    private val adHoc: com.nags.operations.data.repo.AdHocFlightsRepository? = null,
    offlineSearchStatuses: Flow<FlightSyncStatus> = flowOf(FlightSyncStatus()),
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
        serviceRepository: com.nags.operations.data.repo.FlightServicesRepository? = null,
        taskRepository: com.nags.operations.data.repo.FlightTasksRepository? = null,
        adHocRepository: com.nags.operations.data.repo.AdHocFlightsRepository? = null,
        offlineSearchStatuses: Flow<FlightSyncStatus> = flowOf(FlightSyncStatus()),
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
        services = serviceRepository,
        tasks = taskRepository, adHoc = adHocRepository, offlineSearchStatuses = offlineSearchStatuses,
    )

    private val scope = coroutineScope ?: viewModelScope
    val nowTick: StateFlow<Long> = flow {
        while (true) {
            emit(clock())
            delay(1_000L)
        }
    }.stateIn(scope, SharingStarted.WhileSubscribed(5_000L), clock())

    // This reports searchable membership, not fully downloaded media readiness.
    val offlineSearchStatus = offlineSearchStatuses.stateIn(scope, SharingStarted.WhileSubscribed(5_000L), FlightSyncStatus())

    val rows: StateFlow<List<SyncRowState>> = combine(statuses, flightStatuses, nowTick, ::mobileSyncRows)
        .stateIn(scope, SharingStarted.WhileSubscribed(5_000L), mobileSyncRows(emptyList(), FlightSyncStatus(), clock()))

    val pendingAdHoc = (adHoc?.pending ?: flowOf(emptyList()))
        .stateIn(scope, SharingStarted.WhileSubscribed(5_000L), emptyList())
    val adHocActionError = kotlinx.coroutines.flow.MutableStateFlow<String?>(null)
    fun retryAdHoc(id: String) { scope.launch {
        try { adHoc?.retry(id); adHocActionError.value = null }
        catch (cancelled: kotlinx.coroutines.CancellationException) { throw cancelled }
        catch (failure: Exception) { adHocActionError.value = failure.userMessage() }
    } }
    fun discardAdHoc(id: String) { scope.launch {
        try { adHoc?.discard(id); adHocActionError.value = null }
        catch (cancelled: kotlinx.coroutines.CancellationException) { throw cancelled }
        catch (failure: Exception) { adHocActionError.value = failure.userMessage() }
    } }

    val pendingArrivals = (arrivals?.changes ?: flowOf(emptyList()))
        .stateIn(scope, SharingStarted.WhileSubscribed(5_000L), emptyList())
    val arrivalActionError = kotlinx.coroutines.flow.MutableStateFlow<String?>(null)
    val pendingDepartures = (departures?.changes ?: flowOf(emptyList()))
        .stateIn(scope, SharingStarted.WhileSubscribed(5_000L), emptyList())
    val pendingCancellations = (cancellations?.changes ?: flowOf(emptyList()))
        .stateIn(scope, SharingStarted.WhileSubscribed(5_000L), emptyList())
    val pendingServices = (services?.changes ?: flowOf(emptyList()))
        .stateIn(scope, SharingStarted.WhileSubscribed(5_000L), emptyList())
    val serviceActionFailed = kotlinx.coroutines.flow.MutableStateFlow(false)
    fun retryService(flightId: String, recordId: String) = serviceAction { services?.retry(flightId, recordId) }
    fun discardService(flightId: String, recordId: String) = serviceAction { services?.discard(flightId, recordId); requestRefresh() }
    private fun serviceAction(action: suspend () -> Unit) = scope.launch {
        try { serviceActionFailed.value = false; action() }
        catch (cancelled: kotlinx.coroutines.CancellationException) { throw cancelled }
        catch (_: Exception) { serviceActionFailed.value = true }
    }
    val pendingTasks = (tasks?.changes ?: flowOf(emptyList()))
        .stateIn(scope, SharingStarted.WhileSubscribed(5_000L), emptyList())
    val taskActionFailed = kotlinx.coroutines.flow.MutableStateFlow(false)
    fun retryTask(flightId: String, recordId: String) = taskAction { tasks?.retry(flightId, recordId) }
    fun discardTask(flightId: String, recordId: String) = taskAction { tasks?.discard(flightId, recordId); requestRefresh() }
    private fun taskAction(action: suspend () -> Unit) = scope.launch {
        try { taskActionFailed.value = false; action() }
        catch (cancelled: kotlinx.coroutines.CancellationException) { throw cancelled }
        catch (_: Exception) { taskActionFailed.value = true }
    }
    val cancellationActionFailed = kotlinx.coroutines.flow.MutableStateFlow(false)
    fun retryCancellation(id: String) = cancellationAction { cancellations?.retry(id) }
    fun discardCancellation(id: String) = cancellationAction { cancellations?.discard(id); requestRefresh() }
    private fun cancellationAction(action: suspend () -> Unit) = scope.launch {
        try { cancellationActionFailed.value = false; action() }
        catch (cancelled: kotlinx.coroutines.CancellationException) { throw cancelled }
        catch (_: Exception) { cancellationActionFailed.value = true }
    }
    val departureActionFailed = kotlinx.coroutines.flow.MutableStateFlow(false)

    fun retryDeparture(id: String) = departureAction { departures?.retry(id) }
    fun discardDeparture(id: String) = departureAction { departures?.discard(id); requestRefresh() }
    private fun departureAction(action: suspend () -> Unit) = scope.launch {
        try { departureActionFailed.value = false; action() }
        catch (cancelled: kotlinx.coroutines.CancellationException) { throw cancelled }
        catch (_: Exception) { departureActionFailed.value = true }
    }

    fun retryArrival(id: String) = arrivalAction { arrivals?.retry(id) }
    fun discardArrival(id: String) = arrivalAction { arrivals?.discard(id); requestRefresh() }
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

/** Old cache metadata alone cannot establish search coverage; the global flight row still gates media. */
internal fun offlineDataPrepared(rows: List<SyncRowState>, offlineSearch: FlightSyncStatus): Boolean =
    offlineSearch.lastSuccessUtc != null && offlineSearch.error == null && !offlineSearch.permissionDenied &&
        rows.isNotEmpty() && rows.all { it.lastSyncedAt != null && it.lastError == null && !it.permissionDenied }

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
