package com.nags.operations.ui.flights

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nags.operations.data.ApiException
import com.nags.operations.data.AuthenticatedUser
import com.nags.operations.data.AssignedEmployee
import com.nags.operations.data.FlightCategory
import com.nags.operations.data.isInFlightWindow
import com.nags.operations.data.matches
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.FlightListItem
import com.nags.operations.data.FlightPage
import com.nags.operations.data.FlightSyncStatus
import com.nags.operations.data.network.NetworkMonitor
import com.nags.operations.data.repo.AuthRepository
import com.nags.operations.data.repo.FlightsRepository
import com.nags.operations.data.userMessage
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import java.io.IOException
import java.time.Duration
import java.time.Instant
import java.time.temporal.ChronoUnit
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import io.ktor.client.plugins.HttpRequestTimeoutException
import kotlinx.serialization.SerializationException
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.emptyFlow
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class FlightsUiState(
    val category: FlightCategory = FlightCategory.MyFlights,
    val stationId: String? = null,
    val items: List<FlightListItem> = emptyList(),
    val isOnline: Boolean = true,
    val isUsingCachedList: Boolean = false,
    val search: String = "",
    val page: Int = 1,
    val totalCount: Long = 0,
    val hasMore: Boolean = false,
    val isLoading: Boolean = true,
    val isRefreshing: Boolean = false,
    val isLoadingMore: Boolean = false,
    val permissionDenied: Boolean = false,
    val error: String? = null,
    val loadMoreError: String? = null,
    val selectedFlightId: String? = null,
    val lastUnavailableFlightId: String? = null,
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
    private val fetchFlights: suspend (FlightCategory, Int, Int, String?) -> FlightPage,
    private val fetchFlight: suspend (String) -> FlightDetail,
    private val fetchInvitationOptions: suspend (String) -> List<AssignedEmployee>,
    private val sendInvitations: suspend (String, List<String>, String) -> Unit,
    private val coroutineScope: CoroutineScope? = null,
    private val online: StateFlow<Boolean> = MutableStateFlow(true),
    cacheChanges: Flow<Unit> = emptyFlow(),
    private val cacheStatus: Flow<FlightSyncStatus> = flowOf(FlightSyncStatus()),
    private val clearCachedFlights: suspend () -> Unit = {},
    private val observeCustomerLogo: (String) -> Flow<ByteArray?> = { flowOf(null) },
    private val pruneCachedFlights: suspend () -> Unit = {},
    private val now: () -> Instant = Instant::now,
    private val waitForExpiryCheck: suspend (Long) -> Unit = { delay(it) },
) : ViewModel() {
    constructor(repository: FlightsRepository, authRepository: AuthRepository, networkMonitor: NetworkMonitor) : this(
        fetchProfile = authRepository::profile,
        fetchFlights = repository::flights,
        fetchFlight = repository::flight,
        fetchInvitationOptions = repository::invitationOptions,
        sendInvitations = repository::invite,
        online = networkMonitor.isOnline,
        cacheChanges = repository.cacheChanges,
        cacheStatus = repository.syncStatus,
        clearCachedFlights = repository::clearLocal,
        observeCustomerLogo = repository::customerLogo,
        pruneCachedFlights = repository::pruneExpired,
    )

    private val scope: CoroutineScope get() = coroutineScope ?: viewModelScope
    private val _state = MutableStateFlow(FlightsUiState(isOnline = online.value))
    val state: StateFlow<FlightsUiState> = _state.asStateFlow()
    private var listJob: Job? = null
    private var detailJob: Job? = null
    private var invitationJob: Job? = null
    private var expiryJob: Job? = null

    init {
        scheduleExpiryChecks()
        scope.launch {
            online.collect { connected ->
                if (!connected && !_state.value.isInviting) {
                    invitationJob?.cancel()
                    _state.update { it.copy(isLoadingInvitations = false, invitationOptionsLoaded = false) }
                }
                _state.update { it.copy(isOnline = connected) }
            }
        }
        scope.launch {
            // A failed first sync may revoke a live list even before a successful snapshot exists.
            cacheStatus.map { it.permissionDenied }.distinctUntilChanged().collect { denied ->
                if (denied) clearVisibleFlights(permissionDenied = true)
            }
        }
        scope.launch {
            cacheChanges.collect {
                val cached = cacheStatus.first()
                if (cached.permissionDenied || cached.lastSuccessUtc == null) {
                    clearVisibleFlights(cached.permissionDenied || _state.value.permissionDenied)
                } else {
                    val current = _state.value
                    loadList()
                    current.selectedFlightId?.takeIf { !current.isInviting && !current.isLoadingInvitations && !current.isLoadingDetail }?.let { id ->
                        if (current.isInvitationOpen && online.value) refreshInvitations() else openFlight(id)
                    }
                }
            }
        }
    }

    private fun clearVisibleFlights(permissionDenied: Boolean) {
        listJob?.cancel()
        detailJob?.cancel()
        invitationJob?.cancel()
        _state.update {
            it.copy(items = emptyList(), stationId = null, totalCount = 0, hasMore = false,
                selectedFlightId = null, lastUnavailableFlightId = null, detail = null, detailError = null,
                isLoading = false, isRefreshing = false, isLoadingMore = false, isLoadingDetail = false,
                isInvitationOpen = false, isLoadingInvitations = false, isInviting = false,
                invitationOptions = emptyList(), availableInvitationIds = emptySet(), selectedInvitationIds = emptySet(),
                invitationOptionsLoaded = false, invitationError = null, invitationStale = false, invitationSent = false,
                isUsingCachedList = false, canInviteEmployees = false, permissionDenied = permissionDenied,
                error = if (permissionDenied) VIEW_PERMISSION_MESSAGE else null, loadMoreError = null)
        }
    }

    fun setCategory(category: FlightCategory) {
        if (category == _state.value.category) return
        _state.update {
            it.copy(category = category, items = emptyList(), page = 1, totalCount = 0, hasMore = false)
        }
        loadList()
    }

    fun setSearch(search: String) {
        if (search == _state.value.search) return
        _state.update {
            it.copy(search = search, items = emptyList(), totalCount = 0, hasMore = false)
        }
        loadList(debounce = true)
    }

    fun refresh() {
        expireVisibleFlights()
        loadList()
    }

    fun customerLogo(customerId: String): Flow<ByteArray?> = observeCustomerLogo(customerId)

    private fun loadList(debounce: Boolean = false) {
        listJob?.cancel()
        val search = _state.value.search
        val category = _state.value.category
        _state.update {
            it.copy(isLoading = it.items.isEmpty(), isRefreshing = it.items.isNotEmpty(),
                isLoadingMore = false, error = null, loadMoreError = null)
        }
        listJob = scope.launch {
            try {
                if (debounce) delay(300)
                if (!ensureViewPermission()) return@launch
                val result = fetchFlights(category, 1, PAGE_SIZE, search.trim().ifEmpty { null })
                currentCoroutineContext().ensureActive()
                val rows = result.items.filter { it.matches(category) && it.isInFlightWindow(now()) }
                val previousStation = _state.value.stationId ?: _state.value.detail?.stationId
                if (result.stationId != null && previousStation != null && result.stationId != previousStation) {
                    clearSelectedFlight()
                }
                _state.update {
                    it.copy(items = rows, stationId = result.stationId, page = result.page, totalCount = (result.totalCount - (result.items.size - rows.size)).coerceAtLeast(0),
                        hasMore = result.hasNextPage, isLoading = false, isRefreshing = false,
                        permissionDenied = false, error = null, isUsingCachedList = result.fromCache)
                }
                scheduleExpiryChecks()
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
        _state.update { it.copy(isLoadingMore = true, error = null, loadMoreError = null) }
        listJob = scope.launch {
            try {
                val result = fetchFlights(current.category, current.page + 1, PAGE_SIZE, current.search.trim().ifEmpty { null })
                currentCoroutineContext().ensureActive()
                if (result.stationId != null && current.stationId != null && result.stationId != current.stationId) {
                    clearSelectedFlight()
                    _state.update { it.copy(items = emptyList(), stationId = result.stationId,
                        page = 1, totalCount = 0, hasMore = false) }
                    loadList()
                    return@launch
                }
                val rows = result.items.filter { it.matches(current.category) && it.isInFlightWindow(now()) }
                _state.update {
                    it.copy(items = (it.items + rows).distinctBy { flight -> flight.id },
                        page = result.page, totalCount = result.totalCount,
                        hasMore = result.hasNextPage, isLoadingMore = false,
                        isUsingCachedList = it.isUsingCachedList || result.fromCache)
                }
                expireVisibleFlights()
                scheduleExpiryChecks()
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
            it.copy(selectedFlightId = id, lastUnavailableFlightId = null, detail = null,
                isLoadingDetail = true, detailError = null, invitationSent = false)
        }
        detailJob = scope.launch {
            try {
                if (!ensureViewPermission()) {
                    _state.update { it.copy(isLoadingDetail = false, detailError = VIEW_PERMISSION_MESSAGE) }
                    return@launch
                }
                val detail = fetchFlight(id)
                currentCoroutineContext().ensureActive()
                if (!detail.isInFlightWindow(now())) {
                    handleFlightAccessFailure(id, ApiException(404, FLIGHT_EXPIRED_MESSAGE), detailReadSucceeded = false)
                    return@launch
                }
                _state.update { it.copy(detail = detail, isLoadingDetail = false) }
                scheduleExpiryChecks()
            } catch (error: CancellationException) {
                throw error
            } catch (error: Exception) {
                if (!handleFlightAccessFailure(id, error, detailReadSucceeded = false)) {
                    _state.update { it.copy(isLoadingDetail = false, detailError = error.userMessage()) }
                }
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
        if (!current.canInviteEmployees || flight.isPerLanding || !online.value || flight.fromCache) return
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
        if (!online.value || current.isInviting || current.isLoadingInvitations ||
            (selected && staffMemberId !in current.availableInvitationIds)) return
        _state.update {
            it.copy(selectedInvitationIds = if (selected) it.selectedInvitationIds + staffMemberId
                else it.selectedInvitationIds - staffMemberId)
        }
    }

    fun refreshInvitations() {
        val id = _state.value.selectedFlightId ?: return
        if (_state.value.isInviting || !online.value || _state.value.detail?.fromCache == true) return
        invitationJob?.cancel()
        invitationJob = scope.launch { loadInvitationOptions(id) }
    }

    private suspend fun loadInvitationOptions(id: String) {
        _state.update { it.copy(isLoadingInvitations = true, invitationOptionsLoaded = false) }
        var detailReadSucceeded = false
        try {
            val flight = fetchFlight(id)
            currentCoroutineContext().ensureActive()
            if (!flight.isInFlightWindow(now())) throw ApiException(404, FLIGHT_EXPIRED_MESSAGE)
            detailReadSucceeded = true
            _state.update { it.copy(detail = flight) }
            if (flight.fromCache || !online.value) {
                _state.update { it.copy(isLoadingInvitations = false, invitationOptionsLoaded = false,
                    invitationError = INVITE_ONLINE_MESSAGE) }
                return
            }
            val options = fetchInvitationOptions(id)
            currentCoroutineContext().ensureActive()
            if (!flight.isInFlightWindow(now())) {
                handleFlightAccessFailure(id, ApiException(404, FLIGHT_EXPIRED_MESSAGE), detailReadSucceeded = false)
                return
            }
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
            if (!handleFlightAccessFailure(id, error, detailReadSucceeded)) {
                _state.update { it.copy(isLoadingInvitations = false, invitationError = error.userMessage()) }
            }
        }
    }

    /** A denied flight read evicts one visible flight; denied invitation access keeps authorized details. */
    private fun handleFlightAccessFailure(id: String, error: Exception, detailReadSucceeded: Boolean): Boolean {
        if (error !is ApiException || error.statusCode !in setOf(401, 403, 404)) return false
        if (error.statusCode == 401) {
            clearVisibleFlights(permissionDenied = false)
            return true
        }
        _state.update { current ->
            if (detailReadSucceeded) {
                if (current.selectedFlightId != id) current else current.copy(
                    canInviteEmployees = false, isLoadingInvitations = false, isInviting = false,
                    invitationOptionsLoaded = false, invitationOptions = emptyList(),
                    availableInvitationIds = emptySet(), selectedInvitationIds = emptySet(),
                    invitationError = error.userMessage(), invitationStale = false, invitationSent = false)
            } else {
                val rows = current.items.filterNot { it.id == id }
                val removed = current.items.size - rows.size
                val total = (current.totalCount - removed).coerceAtLeast(0)
                val withoutRow = current.copy(items = rows, totalCount = total, lastUnavailableFlightId = id,
                    hasMore = current.hasMore && total > 0)
                if (current.selectedFlightId != id) withoutRow else withoutRow.copy(
                    selectedFlightId = null, detail = null, isLoadingDetail = false,
                    detailError = error.userMessage(), error = error.userMessage(),
                    isInvitationOpen = false, isLoadingInvitations = false, isInviting = false,
                    invitationOptionsLoaded = false, invitationOptions = emptyList(),
                    availableInvitationIds = emptySet(), selectedInvitationIds = emptySet(),
                    invitationError = null, invitationStale = false, invitationSent = false)
            }
        }
        return true
    }

    fun inviteSelectedEmployees() {
        val current = _state.value
        val flight = current.detail ?: return
        if (!flight.isInFlightWindow(now())) {
            expireVisibleFlights()
            return
        }
        if (!online.value || flight.fromCache || !current.isInvitationOpen || current.isInviting || current.isLoadingInvitations ||
            !current.invitationOptionsLoaded || !current.canInviteEmployees || flight.isPerLanding ||
            current.selectedInvitationIds.isEmpty() ||
            !current.availableInvitationIds.containsAll(current.selectedInvitationIds)) return
        _state.update { it.copy(isInviting = true, invitationError = null, invitationStale = false) }
        invitationJob = scope.launch {
            try {
                sendInvitations(flight.id, current.selectedInvitationIds.toList(), flight.rowVersion)
                currentCoroutineContext().ensureActive()
                _state.update { it.copy(isInviting = false, isInvitationOpen = false) }
                openFlight(flight.id)
                _state.update { it.copy(invitationSent = true) }
                refresh()
            } catch (error: CancellationException) {
                throw error
            } catch (error: Exception) {
                if (handleFlightAccessFailure(flight.id, error, detailReadSucceeded = true)) return@launch
                _state.update { it.copy(isInviting = false, invitationError = error.userMessage(),
                    invitationStale = error is ApiException && error.statusCode == 409) }
                if (error is ApiException && error.statusCode == 409) loadInvitationOptions(flight.id)
            }
        }
    }

    /** Recheck the moving window even when no network or synchronization event arrives. */
    private fun scheduleExpiryChecks() {
        expiryJob?.cancel()
        expiryJob = scope.launch {
            while (true) {
                val current = _state.value
                val departures = current.items.map { it.scheduledDepartureUtc } +
                    listOfNotNull(current.detail?.scheduledDepartureUtc)
                val nextExpiry = departures.mapNotNull {
                    runCatching { Instant.parse(it).plus(12, ChronoUnit.HOURS) }.getOrNull()
                }.minOrNull()
                val delayMillis = nextExpiry?.let {
                    (Duration.between(now(), it).toMillis() + 1).coerceIn(1, 30_000)
                } ?: 30_000L
                waitForExpiryCheck(delayMillis)
                expireVisibleFlights()
                try {
                    pruneCachedFlights()
                } catch (cancelled: CancellationException) {
                    throw cancelled
                } catch (_: Exception) {
                    // Reads and the next tick retry persistence cleanup; expired UI rows stay removed.
                }
            }
        }
    }

    private fun expireVisibleFlights() {
        val at = now()
        val current = _state.value
        val rows = current.items.filter { it.isInFlightWindow(at) }
        val expiredDetail = current.detail?.isInFlightWindow(at) == false ||
            current.items.any { it.id == current.selectedFlightId && !it.isInFlightWindow(at) }
        if (rows.size == current.items.size && !expiredDetail) return
        if (expiredDetail) clearSelectedFlight()
        _state.update {
            it.copy(items = rows, totalCount = (it.totalCount - (current.items.size - rows.size)).coerceAtLeast(0))
        }
    }

    private fun clearSelectedFlight() {
        detailJob?.cancel()
        invitationJob?.cancel()
        _state.update {
            it.copy(selectedFlightId = null, detail = null, isLoadingDetail = false, detailError = null,
                isInvitationOpen = false, isLoadingInvitations = false, isInviting = false,
                invitationOptionsLoaded = false, invitationOptions = emptyList(),
                availableInvitationIds = emptySet(), selectedInvitationIds = emptySet(),
                invitationError = null, invitationStale = false, invitationSent = false)
        }
    }

    private suspend fun ensureViewPermission(): Boolean {
        if (!online.value) {
            _state.update { it.copy(canInviteEmployees = false) }
            return true // The repository exposes only this signed-in owner's last authorized cache.
        }
        val profile = try {
            fetchProfile()
        } catch (error: CancellationException) {
            throw error
        } catch (error: Exception) {
            if (error is ApiException && error.statusCode in setOf(401, 403)) {
                if (error.statusCode == 403) {
                    _state.update { it.copy(permissionDenied = true, error = VIEW_PERMISSION_MESSAGE) }
                }
                clearCachedFlights()
                throw error
            }
            if (error !is IOException && error !is HttpRequestTimeoutException && error !is SerializationException &&
                !(error is ApiException && (error.statusCode in 500..599 || error.statusCode in setOf(408, 429)))) throw error
            _state.update { it.copy(canInviteEmployees = false) }
            return true // Server flight reads still authorize; transient failure may use owned cache.
        }
        _state.update { it.copy(canInviteEmployees = INVITE_PERMISSION in profile.permissions) }
        if (VIEW_PERMISSION in profile.permissions) return true
        _state.update {
            it.copy(items = emptyList(), detail = null, canInviteEmployees = false,
                permissionDenied = true, totalCount = 0, hasMore = false,
                isLoading = false, isRefreshing = false,
                isLoadingMore = false, error = VIEW_PERMISSION_MESSAGE, loadMoreError = null)
        }
        clearCachedFlights()
        return false
    }

    private fun listFailed(error: Exception) {
        val forbidden = error is ApiException && error.statusCode == 403
        if (forbidden) {
            clearVisibleFlights(permissionDenied = true)
            return
        }
        _state.update {
            if (it.isLoadingMore) {
                it.copy(isLoadingMore = false, loadMoreError = error.userMessage())
            } else {
                it.copy(permissionDenied = false, isLoading = false, isRefreshing = false,
                    isLoadingMore = false, error = error.userMessage(), loadMoreError = null)
            }
        }
    }

    private companion object {
        const val FLIGHT_EXPIRED_MESSAGE = "This flight is outside the current 24-hour window."
        const val PAGE_SIZE = 20
        const val INVITE_PERMISSION = "operations.flights.invite"
        const val VIEW_PERMISSION = "operations.flights.view"
        const val VIEW_PERMISSION_MESSAGE = "Flights require an active staff account linked to a station and permission to view flights."
        const val INVITE_ONLINE_MESSAGE = "Connect to load current flight details and invite employees."
    }
}
