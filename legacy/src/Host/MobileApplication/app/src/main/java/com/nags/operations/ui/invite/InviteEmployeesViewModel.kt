package com.nags.operations.ui.invite

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nags.operations.data.ApiException
import com.nags.operations.data.TokenStore
import com.nags.operations.data.api.MobileApi
import com.nags.operations.data.db.entities.EmployeeEntity
import com.nags.operations.data.db.entities.FlightAssignedEmployeeSummary
import com.nags.operations.data.db.entities.FlightEntity
import com.nags.operations.data.network.NetworkMonitor
import com.nags.operations.data.repo.EmployeesRepository
import com.nags.operations.data.repo.FlightsRepository
import com.nags.operations.data.sync.SyncCoordinator
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

/**
 * Backs the invite-teammates screen for a single flight. Reads two cached sources offline-first:
 *  - the flight row's [FlightEntity.assignedEmployees] (the "already assigned" section), and
 *  - the station roster [EmployeesRepository] (the selectable "other employees" section,
 *    minus the already-assigned crew and the signed-in user).
 *
 * The invite action itself is online-only: it POSTs the whole selected id list in one request,
 * then re-fetches the flight so the inviter's cache reflects the new roster immediately (the
 * server's realtime broadcast targets the invitees, not the inviter).
 */
class InviteEmployeesViewModel(
    private val flightId: String,
    private val flightsRepository: FlightsRepository,
    employeesRepository: EmployeesRepository,
    private val mobileApi: MobileApi,
    private val syncCoordinator: SyncCoordinator,
    networkMonitor: NetworkMonitor,
    private val tokenStore: TokenStore,
) : ViewModel() {

    data class AssignedRow(
        val employeeId: String,
        val fullName: String,
        val manpowerTypeName: String,
    )

    data class CandidateRow(
        val employeeId: String,
        val fullName: String,
        val manpowerTypeName: String,
        val stationCode: String,
    )

    data class UiState(
        val isLoading: Boolean = true,
        val isOnline: Boolean = true,
        val isSubmitting: Boolean = false,
        val flightNumber: String = "",
        val customerIataCode: String = "",
        val stationCode: String = "",
        val sta: String = "",
        val std: String = "",
        val assigned: List<AssignedRow> = emptyList(),
        val candidates: List<CandidateRow> = emptyList(),
        val search: String = "",
        val selectedIds: Set<String> = emptySet(),
        val error: String? = null,
        val inviteSucceeded: Boolean = false,
    )

    private val _state = MutableStateFlow(UiState())
    val state: StateFlow<UiState> = _state.asStateFlow()

    private var selfEmployeeId: String? = null
    private var allCandidates: List<CandidateRow> = emptyList()

    init {
        viewModelScope.launch {
            selfEmployeeId = tokenStore.getEmployeeId()
        }
        viewModelScope.launch {
            networkMonitor.isOnline.collect { online ->
                _state.update { it.copy(isOnline = online) }
            }
        }
        viewModelScope.launch {
            combine(
                flightsRepository.myFlightFlow(flightId),
                employeesRepository.observe(),
            ) { flight, employees -> flight to employees }
                .collect { (flight, employees) ->
                    rebuild(flight, employees)
                }
        }
    }

    private fun rebuild(flight: FlightEntity?, employees: List<EmployeeEntity>) {
        val assigned = flight?.assignedEmployees.orEmpty()
        val assignedIds = assigned.map { it.employeeId }.toSet()
        val excluded = assignedIds + setOfNotNull(selfEmployeeId)

        allCandidates = employees
            .filter { it.employeeId !in excluded }
            .map { it.toCandidateRow() }

        _state.update { cur ->
            cur.copy(
                isLoading = false,
                flightNumber = flight?.flightNumber.orEmpty(),
                customerIataCode = flight?.customerIataCode.orEmpty(),
                stationCode = flight?.stationCode.orEmpty(),
                sta = flight?.sta.orEmpty(),
                std = flight?.std.orEmpty(),
                assigned = assigned.map { it.toAssignedRow() },
                candidates = applySearch(allCandidates, cur.search),
                // Drop any selection that is no longer a valid candidate (e.g. just got assigned).
                selectedIds = cur.selectedIds.intersect(allCandidates.map { it.employeeId }.toSet()),
            )
        }
    }

    fun setSearch(query: String) {
        _state.update { it.copy(search = query, candidates = applySearch(allCandidates, query)) }
    }

    fun toggleSelection(employeeId: String) {
        _state.update {
            val next = it.selectedIds.toMutableSet()
            if (!next.add(employeeId)) next.remove(employeeId)
            it.copy(selectedIds = next)
        }
    }

    fun clearError() = _state.update { it.copy(error = null) }

    fun invite() {
        val snapshot = _state.value
        if (snapshot.isSubmitting) return
        if (!snapshot.isOnline) {
            _state.update { it.copy(error = "You're offline. Reconnect to invite teammates.") }
            return
        }
        val ids = snapshot.selectedIds.toList()
        if (ids.isEmpty()) return

        viewModelScope.launch {
            _state.update { it.copy(isSubmitting = true, error = null) }
            try {
                mobileApi.inviteToFlight(flightId, ids)
                // Pull the canonical row so the assigned list updates immediately for the inviter.
                runCatching { syncCoordinator.refreshMyFlight(flightId) }
                _state.update { it.copy(isSubmitting = false, selectedIds = emptySet(), inviteSucceeded = true) }
            } catch (e: ApiException) {
                _state.update {
                    it.copy(
                        isSubmitting = false,
                        error = "Couldn't send invites (error ${e.statusCode}). Please try again.",
                    )
                }
            } catch (e: Exception) {
                _state.update {
                    it.copy(isSubmitting = false, error = "Couldn't send invites. Please try again.")
                }
            }
        }
    }

    private fun applySearch(source: List<CandidateRow>, query: String): List<CandidateRow> {
        val q = query.trim()
        if (q.isBlank()) return source
        return source.filter {
            it.fullName.contains(q, ignoreCase = true) ||
                it.manpowerTypeName.contains(q, ignoreCase = true)
        }
    }

    private fun FlightAssignedEmployeeSummary.toAssignedRow() =
        AssignedRow(employeeId = employeeId, fullName = fullName, manpowerTypeName = manpowerTypeName)

    private fun EmployeeEntity.toCandidateRow() =
        CandidateRow(
            employeeId = employeeId,
            fullName = fullName,
            manpowerTypeName = manpowerTypeName,
            stationCode = stationCode,
        )
}
