package com.nags.operations.ui.flights

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nags.operations.data.FlightStatusKind
import com.nags.operations.data.MobileFlightSummaryDto
import com.nags.operations.data.db.entities.WorkOrderOutboxEntity
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
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

class MyFlightsViewModel(
    private val repository: FlightsRepository,
    draftsRepository: WorkOrderDraftsRepository,
    private val outboxRepository: WorkOrderOutboxRepository,
    private val coordinator: SyncCoordinator,
    networkMonitor: NetworkMonitor,
) : ViewModel() {

    enum class QuickFilter {
        /** No work order yet and not canceled — needs attention. */
        Pending,
    }

    data class UiState(
        val items: List<MobileFlightSummaryDto> = emptyList(),
        val isLoading: Boolean = false,
        val isRefreshing: Boolean = false,
        val error: String? = null,
        val search: String = "",
        val statusFilter: FlightStatusKind? = null,
        val quickFilter: QuickFilter? = null,
        val isOnline: Boolean = true,
        val isSyncing: Boolean = false,
        /** Local Room draft keyed by flight id (latest-only per flight). */
        val draftIdByFlightId: Map<String, String> = emptyMap(),
        /**
         * Optimistic outbox state keyed by flight id. The screen passes the
         * matching entry to [com.nags.operations.ui.components.FlightCard]'s
         * `pending` slot — null means no chip.
         */
        val pendingByFlightId: Map<String, PendingDisplayItem> = emptyMap(),
    )

    private val _state = MutableStateFlow(UiState())
    val state: StateFlow<UiState> = _state.asStateFlow()

    private var allItems: List<MobileFlightSummaryDto> = emptyList()
    private var refreshJob: Job? = null

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

    /**
     * Queues a flight cancellation into the offline outbox; the worker delivers it when
     * connectivity returns. When the flight already carries an under-review cancel work
     * order, this updates that work order's cancellation time instead of filing a new one.
     * The pending chip and the eventual status change flow back through the normal sync path.
     */
    fun cancelFlight(flightId: String, canceledAtIso: String) {
        val flight = allItems.firstOrNull { it.id == flightId }
        val cancelWo = flight?.myWorkOrder?.takeIf { it.isCanceled }
        viewModelScope.launch {
            if (cancelWo != null) {
                outboxRepository.enqueueCancellationUpdate(
                    flightId = flightId,
                    flightKind = WorkOrderOutboxEntity.FLIGHT_KIND_MY,
                    workOrderId = cancelWo.id,
                    flightNumber = flight.flightNumber,
                    aircraftTypeId = cancelWo.aircraftTypeId,
                    aircraftTailNumber = cancelWo.aircraftTailNumber,
                    remarks = cancelWo.remarks,
                    canceledAtIso = canceledAtIso,
                )
            } else {
                outboxRepository.enqueueCancel(
                    flightId = flightId,
                    flightKind = WorkOrderOutboxEntity.FLIGHT_KIND_MY,
                    canceledAtIso = canceledAtIso,
                )
            }
        }
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
        source: List<MobileFlightSummaryDto>,
        state: UiState,
    ): List<MobileFlightSummaryDto> {
        if (source.isEmpty()) return source
        val q = state.search.trim()
        return source.asSequence()
            .filter { f ->
                f.status == FlightStatusKind.Scheduled.value ||
                    f.status == FlightStatusKind.InProgress.value
            }
            .filter { f ->
                state.statusFilter?.let { it.value == f.status } ?: true
            }
            .filter { f ->
                when (state.quickFilter) {
                    QuickFilter.Pending -> f.myWorkOrder == null &&
                        f.status != FlightStatusKind.Canceled.value
                    null -> true
                }
            }
            .filter { f ->
                if (q.isBlank()) true
                else listOf(
                    f.flightNumber,
                    f.customerName,
                    f.customerIataCode,
                    f.stationCode,
                    f.operationTypeCode,
                    f.aircraftModel.orEmpty(),
                ).any { it.contains(q, ignoreCase = true) }
            }
            .toList()
    }
}
