package com.nags.operations.ui.flights

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nags.operations.data.ApiException
import com.nags.operations.data.AuthenticatedUser
import com.nags.operations.data.AssignedEmployee
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.FlightListItem
import com.nags.operations.data.FlightPage
import com.nags.operations.data.repo.AuthRepository
import com.nags.operations.data.repo.FlightsRepository
import com.nags.operations.data.userMessage
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class FlightsUiState(
    val items: List<FlightListItem> = emptyList(),
    val search: String = "",
    val page: Int = 1,
    val totalCount: Long = 0,
    val hasMore: Boolean = false,
    val isLoading: Boolean = true,
    val isRefreshing: Boolean = false,
    val isLoadingMore: Boolean = false,
    val permissionDenied: Boolean = false,
    val error: String? = null,
    val selectedFlightId: String? = null,
    val detail: FlightDetail? = null,
    val isLoadingDetail: Boolean = false,
    val detailError: String? = null,
    val canInviteEmployees: Boolean = false,
    val isInvitationOpen: Boolean = false,
    val invitationOptions: List<AssignedEmployee> = emptyList(),
    val availableInvitationIds: Set<String> = emptySet(),
    val selectedInvitationIds: Set<String> = emptySet(),
    val isLoadingInvitations: Boolean = false,
    val invitationOptionsLoaded: Boolean = false,
    val isInviting: Boolean = false,
    val invitationError: String? = null,
    val invitationStale: Boolean = false,
    val invitationSent: Boolean = false,
)

class FlightsViewModel internal constructor(
    private val fetchProfile: suspend () -> AuthenticatedUser,
    private val fetchFlights: suspend (Int, Int, String?) -> FlightPage,
    private val fetchFlight: suspend (String) -> FlightDetail,
    private val fetchInvitationOptions: suspend (String) -> List<AssignedEmployee>,
    private val sendInvitations: suspend (String, List<String>, String) -> Unit,
    private val coroutineScope: CoroutineScope? = null,
) : ViewModel() {
    constructor(repository: FlightsRepository, authRepository: AuthRepository) : this(
        fetchProfile = authRepository::profile,
        fetchFlights = repository::flights,
        fetchFlight = repository::flight,
        fetchInvitationOptions = repository::invitationOptions,
        sendInvitations = repository::invite,
    )

    private val scope: CoroutineScope get() = coroutineScope ?: viewModelScope
    private val _state = MutableStateFlow(FlightsUiState())
    val state: StateFlow<FlightsUiState> = _state.asStateFlow()
    private var listJob: Job? = null
    private var detailJob: Job? = null
    private var invitationJob: Job? = null

    fun setSearch(search: String) {
        if (search == _state.value.search) return
        _state.update {
            it.copy(search = search, items = emptyList(), totalCount = 0, hasMore = false)
        }
        loadList(debounce = true)
    }

    fun refresh() = loadList()

    private fun loadList(debounce: Boolean = false) {
        listJob?.cancel()
        val search = _state.value.search
        _state.update {
            it.copy(isLoading = it.items.isEmpty(), isRefreshing = it.items.isNotEmpty(),
                isLoadingMore = false, error = null)
        }
        listJob = scope.launch {
            try {
                if (debounce) delay(300)
                if (!ensureViewPermission()) return@launch
                val result = fetchFlights(1, PAGE_SIZE, search.trim().ifEmpty { null })
                _state.update {
                    it.copy(items = result.items, page = result.page, totalCount = result.totalCount,
                        hasMore = result.hasNextPage, isLoading = false, isRefreshing = false,
                        permissionDenied = false, error = null)
                }
            } catch (error: CancellationException) {
                throw error
            } catch (error: Exception) {
                listFailed(error)
            }
        }
    }

    fun loadMore() {
        val current = _state.value
        if (!current.hasMore || current.isLoading || current.isRefreshing || current.isLoadingMore) return
        _state.update { it.copy(isLoadingMore = true, error = null) }
        listJob = scope.launch {
            try {
                val result = fetchFlights(current.page + 1, PAGE_SIZE, current.search.trim().ifEmpty { null })
                _state.update {
                    it.copy(items = (it.items + result.items).distinctBy { flight -> flight.id },
                        page = result.page, totalCount = result.totalCount,
                        hasMore = result.hasNextPage, isLoadingMore = false)
                }
            } catch (error: CancellationException) {
                throw error
            } catch (error: Exception) {
                listFailed(error)
            }
        }
    }

    fun openFlight(id: String) {
        detailJob?.cancel()
        _state.update {
            it.copy(selectedFlightId = id, detail = null, isLoadingDetail = true, detailError = null, invitationSent = false)
        }
        detailJob = scope.launch {
            try {
                if (!ensureViewPermission()) {
                    _state.update { it.copy(isLoadingDetail = false, detailError = VIEW_PERMISSION_MESSAGE) }
                    return@launch
                }
                val detail = fetchFlight(id)
                _state.update { it.copy(detail = detail, isLoadingDetail = false) }
            } catch (error: CancellationException) {
                throw error
            } catch (error: Exception) {
                _state.update { it.copy(isLoadingDetail = false, detailError = error.userMessage()) }
            }
        }
    }

    fun closeFlight() {
        if (_state.value.isInviting) return
        invitationJob?.cancel()
        detailJob?.cancel()
        _state.update {
            it.copy(selectedFlightId = null, detail = null, isLoadingDetail = false, detailError = null,
                isInvitationOpen = false, isLoadingInvitations = false, invitationSent = false)
        }
    }

    fun openInvitations() {
        val current = _state.value
        val flight = current.detail ?: return
        if (!current.canInviteEmployees || flight.isPerLanding) return
        _state.update {
            it.copy(isInvitationOpen = true, selectedInvitationIds = emptySet(), invitationOptions = emptyList(),
                availableInvitationIds = emptySet(), invitationError = null, invitationStale = false,
                invitationSent = false)
        }
        refreshInvitations()
    }

    fun closeInvitations() {
        if (_state.value.isInviting) return
        invitationJob?.cancel()
        _state.update { it.copy(isInvitationOpen = false, isLoadingInvitations = false) }
    }

    fun selectInvitation(staffMemberId: String, selected: Boolean) {
        val current = _state.value
        if (current.isInviting || current.isLoadingInvitations ||
            (selected && staffMemberId !in current.availableInvitationIds)) return
        _state.update {
            it.copy(selectedInvitationIds = if (selected) it.selectedInvitationIds + staffMemberId
                else it.selectedInvitationIds - staffMemberId)
        }
    }

    fun refreshInvitations() {
        val id = _state.value.selectedFlightId ?: return
        if (_state.value.isInviting) return
        invitationJob?.cancel()
        invitationJob = scope.launch { loadInvitationOptions(id) }
    }

    private suspend fun loadInvitationOptions(id: String) {
        _state.update { it.copy(isLoadingInvitations = true, invitationOptionsLoaded = false) }
        try {
            val flight = fetchFlight(id)
            _state.update { it.copy(detail = flight) }
            val options = fetchInvitationOptions(id)
            val availableIds = options.map { it.staffMemberId }.toSet()
            _state.update {
                val previousSelected = it.invitationOptions.filter { option ->
                    option.staffMemberId in it.selectedInvitationIds && option.staffMemberId !in availableIds
                }
                it.copy(detail = flight, invitationOptions = options + previousSelected,
                    availableInvitationIds = availableIds, isLoadingInvitations = false,
                    invitationOptionsLoaded = true, invitationError = null)
            }
        } catch (error: CancellationException) {
            throw error
        } catch (error: Exception) {
            _state.update { it.copy(isLoadingInvitations = false, invitationError = error.userMessage()) }
        }
    }

    fun inviteSelectedEmployees() {
        val current = _state.value
        val flight = current.detail ?: return
        if (!current.isInvitationOpen || current.isInviting || current.isLoadingInvitations ||
            !current.invitationOptionsLoaded || !current.canInviteEmployees || flight.isPerLanding ||
            current.selectedInvitationIds.isEmpty() ||
            !current.availableInvitationIds.containsAll(current.selectedInvitationIds)) return
        _state.update { it.copy(isInviting = true, invitationError = null, invitationStale = false) }
        invitationJob = scope.launch {
            try {
                sendInvitations(flight.id, current.selectedInvitationIds.toList(), flight.rowVersion)
                _state.update { it.copy(isInviting = false, isInvitationOpen = false) }
                openFlight(flight.id)
                _state.update { it.copy(invitationSent = true) }
                refresh()
            } catch (error: CancellationException) {
                throw error
            } catch (error: Exception) {
                _state.update { it.copy(isInviting = false, invitationError = error.userMessage(),
                    invitationStale = error is ApiException && error.statusCode == 409) }
                if (error is ApiException && error.statusCode == 409) loadInvitationOptions(flight.id)
            }
        }
    }

    private suspend fun ensureViewPermission(): Boolean {
        val profile = fetchProfile()
        _state.update { it.copy(canInviteEmployees = INVITE_PERMISSION in profile.permissions) }
        if (VIEW_PERMISSION in profile.permissions) return true
        _state.update {
            it.copy(items = emptyList(), detail = null, permissionDenied = true, totalCount = 0,
                hasMore = false, isLoading = false, isRefreshing = false,
                isLoadingMore = false, error = VIEW_PERMISSION_MESSAGE)
        }
        return false
    }

    private fun listFailed(error: Exception) {
        _state.update {
            val forbidden = error is ApiException && error.statusCode == 403
            it.copy(items = if (forbidden) emptyList() else it.items,
                totalCount = if (forbidden) 0 else it.totalCount,
                hasMore = if (forbidden) false else it.hasMore,
                permissionDenied = forbidden, isLoading = false, isRefreshing = false,
                isLoadingMore = false, error = error.userMessage())
        }
    }

    private companion object {
        const val PAGE_SIZE = 20
        const val INVITE_PERMISSION = "operations.flights.invite"
        const val VIEW_PERMISSION = "operations.flights.view"
        const val VIEW_PERMISSION_MESSAGE = "Your account does not have permission to view flights."
    }
}
