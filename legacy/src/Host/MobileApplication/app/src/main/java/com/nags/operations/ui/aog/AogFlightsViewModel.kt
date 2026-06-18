package com.nags.operations.ui.aog

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

class AogFlightsViewModel(
    private val repository: FlightsRepository,
    draftsRepository: WorkOrderDraftsRepository,
    private val outboxRepository: WorkOrderOutboxRepository,
    private val coordinator: SyncCoordinator,
    networkMonitor: NetworkMonitor,
) : ViewModel() {

    data class UiState(
        val items: List<MobileFlightSummaryDto> = emptyList(),
        val isLoading: Boolean = false,
        val isRefreshing: Boolean = false,
        val error: String? = null,
        val search: String = "",
        val statusFilter: FlightStatusKind? = null,
        val isOnline: Boolean = true,
        val isSyncing: Boolean = false,
        val draftIdByFlightId: Map<String, String> = emptyMap(),
        /** Optimistic outbox chip per flight id; see [MyFlightsViewModel] for shape. */
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
                repository.aogFlightsFlow(),
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
                        .firstOrNull { it.table == SyncTable.AogFlights }?.message
                SyncReport.AlreadyRunning -> null
            }
            _state.update {
                it.copy(
                    isLoading = false,
                    isRefreshing = false,
                    error = if (allItems.isEmpty() && tableErr != null) tableErr else null,
                )
            }
        }
    }

    /** Queues a flight cancellation (or updates an existing cancel WO); see [MyFlightsViewModel.cancelFlight]. */
    fun cancelFlight(flightId: String, canceledAtIso: String) {
        val flight = allItems.firstOrNull { it.id == flightId }
        val cancelWo = flight?.myWorkOrder?.takeIf { it.isCanceled }
        viewModelScope.launch {
            if (cancelWo != null) {
                outboxRepository.enqueueCancellationUpdate(
                    flightId = flightId,
                    flightKind = WorkOrderOutboxEntity.FLIGHT_KIND_AOG,
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
                    flightKind = WorkOrderOutboxEntity.FLIGHT_KIND_AOG,
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
