package com.nags.operations.ui.flights

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nags.operations.data.FlightStatusKind
import com.nags.operations.data.MobileFlightDto
import com.nags.operations.data.MobileFlightWindowPhase
import com.nags.operations.data.WorkOrderTypeKind
import com.nags.operations.data.ApiException
import com.nags.operations.data.asInformationOnlyMobileDetail
import com.nags.operations.data.evaluateMobileWindow
import com.nags.operations.data.isLocallyWithinMobileWindow
import com.nags.operations.data.db.entities.WorkOrderOutboxEntity
import com.nags.operations.data.notifications.NotificationOpenRequest
import com.nags.operations.data.userMessage
import com.nags.operations.data.network.NetworkMonitor
import com.nags.operations.data.outbox.WorkOrderOutboxRepository
import com.nags.operations.data.repo.FlightsRepository
import com.nags.operations.data.repo.WorkOrderDraftsRepository
import com.nags.operations.data.sync.PendingDisplayItem
import com.nags.operations.data.sync.SyncCoordinator
import com.nags.operations.data.sync.SyncOutcome
import com.nags.operations.data.sync.SyncReport
import com.nags.operations.data.sync.SyncTable
import com.nags.operations.data.toSummary
import kotlinx.coroutines.Job
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.delay
import java.time.Duration
import java.time.Instant

class MyFlightsViewModel(
    private val repository: FlightsRepository,
    draftsRepository: WorkOrderDraftsRepository,
    private val outboxRepository: WorkOrderOutboxRepository,
    private val coordinator: SyncCoordinator,
    private val networkMonitor: NetworkMonitor,
) : ViewModel() {

    enum class QuickFilter {
        /** No work order yet and not canceled — needs attention. */
        Pending,
    }

    data class UiState(
        val items: List<MobileFlightDto> = emptyList(),
        val isLoading: Boolean = false,
        val isRefreshing: Boolean = false,
        val error: String? = null,
        val search: String = "",
        val statusFilter: FlightStatusKind? = FlightStatusKind.Scheduled,
        val quickFilter: QuickFilter? = null,
        val isOnline: Boolean = true,
        val isSyncing: Boolean = false,
        /** Local Room draft keyed by flight id (latest-only per flight). */
        val draftIdByFlightId: Map<String, String> = emptyMap(),
        /** Optimistic outbox state keyed by flight id; null entry = no chip. */
        val pendingByFlightId: Map<String, PendingDisplayItem> = emptyMap(),
        val requestedFlight: MobileFlightDto? = null,
        /** Exact notification request whose by-id fetch produced [requestedFlight]/error. */
        val requestedFlightRequest: NotificationOpenRequest? = null,
        val requestedFlightError: String? = null,
        val isOpeningRequestedFlight: Boolean = false,
    )

    private val _state = MutableStateFlow(UiState())
    val state: StateFlow<UiState> = _state.asStateFlow()

    private var allItems: List<MobileFlightDto> = emptyList()
    private var refreshJob: Job? = null
    private var requestedFlightJob: Job? = null
    private var windowBoundaryJob: Job? = null
    private var activeRequestedFlightRequest: NotificationOpenRequest? = null

    init {
        viewModelScope.launch {
            networkMonitor.isOnline.collect { online ->
                _state.update { it.copy(isOnline = online) }
            }
        }
        viewModelScope.launch {
            coordinator.isSyncing.collect { syncing ->
                _state.update { it.copy(isSyncing = syncing) }
            }
        }
        viewModelScope.launch {
            combine(
                repository.myFlightsFlow(),
                draftsRepository.observeFlightIdToLatestDraftId(),
                outboxRepository.observePendingByFlightId(),
            ) { entities, draftMap, pendingMap ->
                Triple(entities, draftMap, pendingMap)
            }.collect { (entities, draftMap, pendingMap) ->
                allItems = entities.map { it.toSummary() }
                _state.update { cur ->
                    cur.copy(
                        items = applyFilters(allItems, cur),
                        draftIdByFlightId = draftMap,
                        pendingByFlightId = pendingMap,
                        error = null,
                    )
                }
                scheduleNextWindowBoundary()
            }
        }
    }

    fun refresh(userInitiated: Boolean = false) {
        refreshJob?.cancel()
        refreshJob = viewModelScope.launch {
            _state.update {
                it.copy(
                    isLoading = !userInitiated && allItems.isEmpty(),
                    isRefreshing = userInitiated,
                    error = null,
                )
            }
            val report = coordinator.refreshAll()
            val tableErr = when (report) {
                is SyncReport.Completed ->
                    report.outcomes.filterIsInstance<SyncOutcome.Failure>()
                        .firstOrNull { it.table == SyncTable.Flights }?.message
                SyncReport.AlreadyRunning -> null
            }
            _state.update { cur ->
                cur.copy(
                    isLoading = false,
                    isRefreshing = false,
                    error = if (allItems.isEmpty() && tableErr != null) tableErr else null,
                )
            }
        }
    }

    fun openRequestedFlight(request: NotificationOpenRequest, force: Boolean = false) {
        val flightId = request.flightId ?: return
        if (!force && activeRequestedFlightRequest == request &&
            (requestedFlightJob?.isActive == true ||
                (_state.value.requestedFlightRequest == request &&
                    (_state.value.requestedFlight != null || _state.value.requestedFlightError != null)))
        ) return

        activeRequestedFlightRequest = request
        requestedFlightJob?.cancel()
        requestedFlightJob = viewModelScope.launch {
            _state.update {
                it.copy(
                    requestedFlight = null,
                    requestedFlightRequest = request,
                    isOpeningRequestedFlight = true,
                    requestedFlightError = null,
                )
            }
            // Notification details are always network-first. A cached row cannot carry authority:
            // its STA may be stale and Room intentionally does not persist the server decision.
            val cached = allItems.firstOrNull { it.id.equals(flightId, ignoreCase = true) }
            try {
                val flight = coordinator.refreshMyFlight(flightId)
                if (activeRequestedFlightRequest != request) return@launch
                _state.update {
                    it.copy(
                        requestedFlight = flight,
                        requestedFlightRequest = request,
                        requestedFlightError = null,
                        isOpeningRequestedFlight = false,
                    )
                }
            } catch (error: CancellationException) {
                throw error
            } catch (error: Exception) {
                if (activeRequestedFlightRequest != request) return@launch
                val fallback = cached
                    ?.takeIf {
                        shouldUseInformationalFlightFallback(
                            error = error,
                        )
                    }
                    ?.asInformationOnlyMobileDetail()
                _state.update {
                    it.copy(
                        requestedFlight = fallback,
                        requestedFlightRequest = request,
                        requestedFlightError = if (fallback == null) error.userMessage() else null,
                        isOpeningRequestedFlight = false,
                    )
                }
            }
        }
    }

    /** Server-revalidate an information-only notification sheet at the leading boundary. */
    suspend fun revalidateRequestedFlight(flightId: String): MobileFlightDto =
        coordinator.refreshMyFlight(flightId)

    fun consumeRequestedFlight(request: NotificationOpenRequest) {
        if (_state.value.requestedFlightRequest != request) return
        if (activeRequestedFlightRequest == request) activeRequestedFlightRequest = null
        _state.update {
            it.copy(
                requestedFlight = null,
                requestedFlightRequest = null,
                requestedFlightError = null,
                isOpeningRequestedFlight = false,
            )
        }
    }

    /**
     * Queues a flight cancellation into the offline outbox; the worker delivers it when
     * connectivity returns. When the flight already carries an editable cancellation work
     * order, this updates that work order's cancellation details instead of filing a new one.
     */
    fun cancelFlight(
        flightId: String,
        canceledAtIso: String,
        reason: String,
        onFinished: (success: Boolean, message: String?) -> Unit,
    ) {
        cancelFlightInternal(
            allItems, outboxRepository, viewModelScope,
            flightId, canceledAtIso, reason, WorkOrderOutboxEntity.FLIGHT_KIND_MY, onFinished,
        )
    }

    fun setSearch(query: String) {
        _state.update {
            val next = it.copy(search = query)
            next.copy(items = applyFilters(allItems, next))
        }
    }

    fun setStatusFilter(filter: FlightStatusKind?) {
        _state.update {
            val next = it.copy(statusFilter = filter)
            next.copy(items = applyFilters(allItems, next))
        }
    }

    fun setQuickFilter(filter: QuickFilter?) {
        _state.update {
            val next = it.copy(quickFilter = filter)
            next.copy(items = applyFilters(allItems, next))
        }
    }

    private fun applyFilters(
        source: List<MobileFlightDto>,
        state: UiState,
    ): List<MobileFlightDto> {
        if (source.isEmpty()) return source
        val q = state.search.trim()
        val now = Instant.now()
        return source.asSequence()
            // Snapshot and realtime paths have different endpoints; fail closed locally so an
            // out-of-window by-id upsert can never flash on the list.
            .filter { it.isLocallyWithinMobileWindow(now) }
            .filter { it.isOpenFlight() }
            .filter { f -> state.statusFilter?.let { it.wire == f.status } ?: true }
            .filter { f ->
                when (state.quickFilter) {
                    QuickFilter.Pending -> f.myWorkOrder == null &&
                        f.status != FlightStatusKind.Canceled.wire
                    null -> true
                }
            }
            .filter { it.matchesSearch(q) }
            .toList()
    }

    /** Re-evaluate Room rows exactly when the next inclusive STA boundary changes membership. */
    private fun scheduleNextWindowBoundary() {
        windowBoundaryJob?.cancel()
        val now = Instant.now()
        val nextBoundary = allItems.asSequence()
            .mapNotNull { flight ->
                val window = flight.evaluateMobileWindow(now)
                when (window.phase) {
                    MobileFlightWindowPhase.Before -> window.startsAt
                    // The trailing boundary is inclusive; leave the list one millisecond later.
                    MobileFlightWindowPhase.Within -> window.endsAt?.plusMillis(1)
                    MobileFlightWindowPhase.After,
                    MobileFlightWindowPhase.Unknown -> null
                }
            }
            .filter { it.isAfter(now) }
            .minOrNull()
            ?: return

        windowBoundaryJob = viewModelScope.launch {
            delay(Duration.between(Instant.now(), nextBoundary).toMillis().coerceAtLeast(1))
            windowBoundaryJob = null
            _state.update { current ->
                current.copy(items = applyFilters(allItems, current))
            }
            scheduleNextWindowBoundary()
        }
    }
}

/** HTTP authorization/not-found responses must never resurrect cached flight details. */
internal fun shouldUseInformationalFlightFallback(error: Throwable): Boolean {
    if (error is ApiException) {
        return error.statusCode == 408 || error.statusCode == 429 || error.statusCode >= 500
    }
    // Transport failures are eligible even before ConnectivityManager publishes its offline edge.
    return true
}

/** Scheduled + InProgress only — settled and merged flights leave the actionable lists. */
internal fun MobileFlightDto.isOpenFlight(): Boolean =
    status == FlightStatusKind.Scheduled.wire || status == FlightStatusKind.InProgress.wire

internal fun MobileFlightDto.matchesSearch(query: String): Boolean {
    if (query.isBlank()) return true
    return listOf(
        flightNumber,
        customerName,
        customerIataCode.orEmpty(),
        stationIata,
        operationTypeName,
        aircraftTypeModel.orEmpty(),
    ).any { it.contains(query, ignoreCase = true) }
}

/**
 * Shared cancel-flight enqueue used by all three list ViewModels: updates the caller's
 * existing editable cancellation work order when present, otherwise files a new cancellation.
 */
internal fun cancelFlightInternal(
    allItems: List<MobileFlightDto>,
    outboxRepository: WorkOrderOutboxRepository,
    scope: kotlinx.coroutines.CoroutineScope,
    flightId: String,
    canceledAtIso: String,
    reason: String,
    flightKind: Int,
    onFinished: (success: Boolean, message: String?) -> Unit,
) {
    val flight = allItems.firstOrNull { it.id == flightId }
    val cancelWo = flight?.myWorkOrder?.takeIf {
        WorkOrderTypeKind.fromWire(it.type) == WorkOrderTypeKind.Cancellation
    }
    scope.launch {
        if (cancelWo != null && cancelWo.rowVersion.isBlank()) {
            onFinished(
                false,
                "This cancellation is missing its base revision. Refresh the flight and try again.",
            )
            return@launch
        }
        try {
            if (cancelWo != null) {
                outboxRepository.enqueueCancellationUpdate(
                    flightId = flightId,
                    flightKind = flightKind,
                    workOrderId = cancelWo.id,
                    baseRowVersion = cancelWo.rowVersion,
                    canceledAtIso = canceledAtIso,
                    reason = reason,
                    remarks = cancelWo.remarks,
                )
            } else {
                outboxRepository.enqueueCancel(
                    flightId = flightId,
                    flightKind = flightKind,
                    canceledAtIso = canceledAtIso,
                    reason = reason,
                )
            }
        } catch (error: Exception) {
            if (error is CancellationException) throw error
            onFinished(
                false,
                "Could not save the cancellation on this device. Please try again.",
            )
            return@launch
        }
        onFinished(true, null)
    }
}
