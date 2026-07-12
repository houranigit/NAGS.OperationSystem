package com.nags.operations.ui.adhoc

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nags.operations.data.FlightStatusKind
import com.nags.operations.data.MobileFlightDto
import com.nags.operations.data.db.entities.WorkOrderOutboxEntity
import com.nags.operations.data.network.NetworkMonitor
import com.nags.operations.data.outbox.PendingAdHocFlight
import com.nags.operations.data.outbox.WorkOrderOutboxRepository
import com.nags.operations.data.repo.FlightsRepository
import com.nags.operations.data.repo.WorkOrderDraftsRepository
import com.nags.operations.data.sync.PendingDisplayItem
import com.nags.operations.data.sync.SyncCoordinator
import com.nags.operations.data.sync.SyncOutcome
import com.nags.operations.data.sync.SyncReport
import com.nags.operations.data.sync.SyncTable
import com.nags.operations.data.toSummary
import com.nags.operations.ui.flights.cancelFlightInternal
import com.nags.operations.ui.flights.isOpenFlight
import com.nags.operations.ui.flights.matchesSearch
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

class AdHocFlightsViewModel(
    private val repository: FlightsRepository,
    draftsRepository: WorkOrderDraftsRepository,
    private val outboxRepository: WorkOrderOutboxRepository,
    private val coordinator: SyncCoordinator,
    networkMonitor: NetworkMonitor,
) : ViewModel() {

    data class UiState(
        val items: List<MobileFlightDto> = emptyList(),
        val isLoading: Boolean = false,
        val isRefreshing: Boolean = false,
        val error: String? = null,
        val search: String = "",
        val statusFilter: FlightStatusKind? = null,
        val isOnline: Boolean = true,
        val isSyncing: Boolean = false,
        val draftIdByFlightId: Map<String, String> = emptyMap(),
        /**
         * Optimistic outbox chip per flight id — keyed on both real server flight ids and the
         * synthetic clientFlightId used for ad-hoc-scratch rows that aren't on the server yet.
         */
        val pendingByFlightId: Map<String, PendingDisplayItem> = emptyMap(),
    )

    private val _state = MutableStateFlow(UiState())
    val state: StateFlow<UiState> = _state.asStateFlow()

    private var allItems: List<MobileFlightDto> = emptyList()
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
                repository.adHocFlightsFlow(),
                draftsRepository.observeFlightIdToLatestDraftId(),
                outboxRepository.observePendingByFlightId(),
                outboxRepository.observePendingAdHocScratch(),
            ) { entities, draftMap, pendingMap, scratchPending ->
                AdHocSources(entities.map { it.toSummary() }, draftMap, pendingMap, scratchPending)
            }.collect { src ->
                // Synthetic scratch rows ride at the top of the list so the user sees their
                // brand-new ad-hoc flight immediately — without poisoning the server-truth
                // cache. They disappear when the SignalR echo lands and the outbox row is
                // deleted by SyncCoordinator.
                val synthetic = src.scratchPending.map { it.toSyntheticSummary() }
                allItems = synthetic + src.entities
                val mergedPending = HashMap(src.pendingMap)
                src.scratchPending.forEach { p ->
                    mergedPending[p.clientFlightId] = PendingDisplayItem(
                        id = p.clientMutationId,
                        flightId = p.clientFlightId,
                        status = p.status,
                    )
                }
                _state.update { cur ->
                    cur.copy(
                        items = applyFilters(allItems, cur),
                        draftIdByFlightId = src.draftMap,
                        pendingByFlightId = mergedPending,
                        error = null,
                    )
                }
            }
        }
    }

    private data class AdHocSources(
        val entities: List<MobileFlightDto>,
        val draftMap: Map<String, String>,
        val pendingMap: Map<String, PendingDisplayItem>,
        val scratchPending: List<PendingAdHocFlight>,
    )

    /**
     * Render the queued ad-hoc-scratch row as a server-shaped flight so the same FlightCard
     * composable can paint it. Status is forced to Scheduled so the open-flights filter keeps
     * the row visible; the pending chip on the card communicates the offline state.
     */
    private fun PendingAdHocFlight.toSyntheticSummary(): MobileFlightDto =
        MobileFlightDto(
            id = clientFlightId,
            flightNumber = flightNumber.ifBlank { "—" },
            originalFlightNumber = flightNumber.ifBlank { "—" },
            customerId = "",
            customerIataCode = customerIataCode.takeIf { it.isNotBlank() },
            customerName = customerName,
            stationId = "",
            stationIata = stationCode,
            operationTypeId = "",
            operationTypeName = "Ad Hoc",
            aircraftTypeId = null,
            aircraftTypeModel = aircraftModel,
            scheduledArrivalUtc = sta,
            scheduledDepartureUtc = std,
            status = FlightStatusKind.Scheduled.wire,
            isPerLanding = false,
            isAdHoc = true,
            plannedServices = emptyList(),
            assignedEmployees = emptyList(),
            myWorkOrder = null,
            otherWorkOrdersExist = false,
            updatedAtUtc = null,
            rowVersion = "",
        )

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
                        .firstOrNull { it.table == SyncTable.AdHocFlights }?.message
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

    /** Queues a flight cancellation (or updates an existing cancellation WO). */
    fun cancelFlight(
        flightId: String,
        canceledAtIso: String,
        reason: String,
        onFinished: (success: Boolean, message: String?) -> Unit,
    ) {
        cancelFlightInternal(
            allItems, outboxRepository, viewModelScope,
            flightId, canceledAtIso, reason, WorkOrderOutboxEntity.FLIGHT_KIND_AD_HOC,
            onFinished,
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

    private fun applyFilters(
        source: List<MobileFlightDto>,
        state: UiState,
    ): List<MobileFlightDto> {
        if (source.isEmpty()) return source
        val q = state.search.trim()
        return source.asSequence()
            .filter { it.isOpenFlight() }
            .filter { f -> state.statusFilter?.let { it.wire == f.status } ?: true }
            .filter { it.matchesSearch(q) }
            .toList()
    }
}
