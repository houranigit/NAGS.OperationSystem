package com.nags.operations.ui.flights

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nags.operations.data.ApiException
import com.nags.operations.data.AircraftTypeCatalogItem
import com.nags.operations.data.AuthenticatedUser
import com.nags.operations.data.AssignedEmployee
import com.nags.operations.data.FlightCategory
import com.nags.operations.data.isInFlightWindow
import com.nags.operations.data.matches
import com.nags.operations.data.parseFlightInstant
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.FlightListItem
import com.nags.operations.data.FlightPage
import com.nags.operations.data.FlightSyncStatus
import com.nags.operations.data.network.NetworkMonitor
import com.nags.operations.data.repo.AuthRepository
import com.nags.operations.data.repo.FlightsRepository
import com.nags.operations.data.repo.SharedCatalogsRepository
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
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

/** Only list data is retained; details and invitation state remain owned by the active screen. */
data class FlightTabState(
    val stationId: String?,
    val items: List<FlightListItem>,
    val isUsingCachedList: Boolean,
    val page: Int,
    val totalCount: Long,
    val hasMore: Boolean,
    val error: String?,
    val loadMoreError: String?,
    val needsRefresh: Boolean = false,
)

data class FlightsUiState(
    val category: FlightCategory = FlightCategory.MyFlights,
    val stationId: String? = null,
    val retainedTabs: Map<FlightCategory, FlightTabState> = emptyMap(),
    val hasLoadedList: Boolean = false,
    val items: List<FlightListItem> = emptyList(),
    val isOnline: Boolean = true,
    val isUsingCachedList: Boolean = false,
    val search: String = "",
    val isSearchOpen: Boolean = false,
    val page: Int = 1,
    val totalCount: Long = 0,
    val hasMore: Boolean = false,
    val isLoading: Boolean = true,
    val isRefreshing: Boolean = false,
    val showRefreshIndicator: Boolean = false,
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

/** Provides the loaded neighboring page while swiping without restoring its global screen state. */
fun FlightsUiState.forCategory(category: FlightCategory): FlightsUiState {
    if (this.category == category) return this
    return withTab(category, retainedTabs[category])
}

private fun FlightsUiState.withTab(category: FlightCategory, tab: FlightTabState?): FlightsUiState {
    val retained = tab?.takeIf { stationId == null || it.stationId == stationId }
    return copy(category = category, stationId = stationId ?: retained?.stationId,
        items = if (permissionDenied) emptyList() else retained?.items.orEmpty(),
        hasLoadedList = !permissionDenied && retained != null,
        isUsingCachedList = retained?.isUsingCachedList ?: false,
        page = retained?.page ?: 1, totalCount = if (permissionDenied) 0 else retained?.totalCount ?: 0,
        hasMore = !permissionDenied && (retained?.hasMore ?: false),
        isLoading = !permissionDenied && retained == null, isRefreshing = false, showRefreshIndicator = false, isLoadingMore = false,
        error = if (permissionDenied) error else retained?.error, loadMoreError = retained?.loadMoreError)
}

private fun FlightsUiState.tabSnapshot() = FlightTabState(stationId, items, isUsingCachedList,
    page, totalCount, hasMore, error, loadMoreError, retainedTabs[category]?.needsRefresh == true)

private fun FlightTabState.withoutFlight(id: String): FlightTabState {
    val rows = items.filterNot { it.id == id }
    val total = (totalCount - (items.size - rows.size)).coerceAtLeast(0)
    return copy(items = rows, totalCount = total, hasMore = hasMore && total > 0)
}

private fun FlightTabState.withinWindow(at: Instant): FlightTabState {
    val rows = items.filter { it.isInFlightWindow(at) }
    return if (rows.size == items.size) this else copy(items = rows,
        totalCount = (totalCount - (items.size - rows.size)).coerceAtLeast(0))
}

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
    private val waitForSearchDebounce: suspend () -> Unit = { delay(300) },
    val arrivalAircraftTypes: Flow<List<AircraftTypeCatalogItem>> = flowOf(emptyList()),
    val operationsCatalogs: Flow<FlightOperationsCatalogs> = flowOf(FlightOperationsCatalogs()),
    val arrivals: com.nags.operations.data.repo.FlightArrivalsRepository? = null,
) : ViewModel() {
    constructor(repository: FlightsRepository, authRepository: AuthRepository, networkMonitor: NetworkMonitor,
        sharedCatalogsRepository: SharedCatalogsRepository, arrivalRepository: com.nags.operations.data.repo.FlightArrivalsRepository) : this(
        arrivals = arrivalRepository,
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
        arrivalAircraftTypes = sharedCatalogsRepository.aircraftTypesFlow(),
        operationsCatalogs = combine(
            combine(sharedCatalogsRepository.servicesFlow(), sharedCatalogsRepository.ataChaptersFlow(),
                sharedCatalogsRepository.stationStaffFlow(), sharedCatalogsRepository.toolsFlow(), sharedCatalogsRepository.materialsFlow()) {
                    services, ataChapters, stationStaff, tools, materials ->
                FlightOperationsCatalogs(services, ataChapters, stationStaff, tools, materials)
            },
            sharedCatalogsRepository.generalSupportsFlow(),
        ) { catalogs, generalSupports -> catalogs.copy(generalSupports = generalSupports) }.distinctUntilChanged(),
    )

    suspend fun reviewArrival(id: String): FlightDetail {
        check(online.value) { "Connect to reload the flight before reviewing your arrival." }
        val detail = fetchFlight(id)
        check(!detail.fromCache) { "Connect to reload the latest flight." }
        _state.update { if (it.selectedFlightId == id) it.copy(detail = detail) else it }
        return detail
    }

    private val scope: CoroutineScope get() = coroutineScope ?: viewModelScope
    private val _state = MutableStateFlow(FlightsUiState(isOnline = online.value))
    val state: StateFlow<FlightsUiState> = _state.asStateFlow()
    private var listJob: Job? = null
    private var detailJob: Job? = null
    private var invitationJob: Job? = null
    private var expiryJob: Job? = null
    private var normalTabs: Map<FlightCategory, FlightTabState> = emptyMap()

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
                    invalidateRetainedTabs()
                    loadList()
                    current.selectedFlightId?.takeIf { !current.isInviting && !current.isLoadingInvitations && !current.isLoadingDetail }?.let { id ->
                        if (current.isInvitationOpen && online.value) refreshInvitations() else openFlight(id)
                    }
                }
            }
        }
    }

    private fun clearVisibleFlights(permissionDenied: Boolean) {
        normalTabs = emptyMap()
        listJob?.cancel()
        detailJob?.cancel()
        invitationJob?.cancel()
        _state.update {
            it.copy(items = emptyList(), stationId = null, retainedTabs = emptyMap(), hasLoadedList = false,
                totalCount = 0, hasMore = false,
                selectedFlightId = null, lastUnavailableFlightId = null, detail = null, detailError = null,
                isLoading = false, isRefreshing = false, showRefreshIndicator = false, isLoadingMore = false, isLoadingDetail = false,
                isInvitationOpen = false, isLoadingInvitations = false, isInviting = false,
                invitationOptions = emptyList(), availableInvitationIds = emptySet(), selectedInvitationIds = emptySet(),
                invitationOptionsLoaded = false, invitationError = null, invitationStale = false, invitationSent = false,
                isUsingCachedList = false, canInviteEmployees = false, permissionDenied = permissionDenied,
                error = if (permissionDenied) VIEW_PERMISSION_MESSAGE else null, loadMoreError = null)
        }
    }

    fun setCategory(category: FlightCategory) {
        if (category == _state.value.category) return
        listJob?.cancel()
        expireVisibleFlights()
        _state.update { current ->
            val tabs = if (current.hasLoadedList) current.retainedTabs + (current.category to current.tabSnapshot())
                else current.retainedTabs - current.category
            current.copy(retainedTabs = tabs).forCategory(category)
        }
        if (!_state.value.hasLoadedList || _state.value.retainedTabs[category]?.needsRefresh == true) loadList()
    }

    fun openSearch() {
        if (_state.value.isSearchOpen) return
        rememberNormalTabs()
        _state.update { it.copy(isSearchOpen = true) }
    }

    fun closeSearch() {
        if (!_state.value.isSearchOpen) return
        expireVisibleFlights()
        if (_state.value.search.isBlank()) {
            normalTabs = emptyMap()
            _state.update { it.copy(isSearchOpen = false, search = "") }
        } else {
            listJob?.cancel()
            restoreNormalTabs(searchOpen = false)
        }
    }

    fun setSearch(search: String) {
        if (!_state.value.isSearchOpen || search == _state.value.search) return
        if (_state.value.search.isBlank()) rememberNormalTabs()
        listJob?.cancel()
        if (search.isBlank()) {
            expireVisibleFlights()
            restoreNormalTabs(searchOpen = true, query = search)
            return
        }
        _state.update {
            it.copy(search = search, items = emptyList(), retainedTabs = emptyMap(), hasLoadedList = false,
                page = 1, totalCount = 0, hasMore = false, showRefreshIndicator = false)
        }
        loadList(debounce = true)
    }

    private fun rememberNormalTabs() {
        val current = _state.value
        if (current.search.isNotBlank()) return
        normalTabs = if (current.hasLoadedList) current.retainedTabs + (current.category to current.tabSnapshot())
            else current.retainedTabs - current.category
    }

    private fun restoreNormalTabs(searchOpen: Boolean, query: String = "") {
        val current = _state.value
        _state.update {
            it.copy(search = query, isSearchOpen = searchOpen, retainedTabs = normalTabs)
                .withTab(current.category, normalTabs[current.category])
        }
        if (!searchOpen) normalTabs = emptyMap()
        if (!_state.value.hasLoadedList || _state.value.retainedTabs[current.category]?.needsRefresh == true) loadList()
    }

    fun refresh() = refreshList(showIndicator = true)

    fun refreshSilently() = refreshList(showIndicator = false)

    private fun refreshList(showIndicator: Boolean) {
        expireVisibleFlights()
        invalidateRetainedTabs()
        loadList(showIndicator = showIndicator)
    }

    private fun invalidateRetainedTabs() {
        normalTabs = normalTabs.mapValues { (_, tab) -> tab.copy(needsRefresh = true) }
        _state.update { current ->
            val tabs = if (current.hasLoadedList) current.retainedTabs + (current.category to current.tabSnapshot())
                else current.retainedTabs
            current.copy(retainedTabs = tabs.mapValues { (_, tab) -> tab.copy(needsRefresh = true) })
        }
    }

    fun customerLogo(customerId: String): Flow<ByteArray?> = observeCustomerLogo(customerId)

    private fun loadList(debounce: Boolean = false, showIndicator: Boolean = false) {
        listJob?.cancel()
        val search = _state.value.search
        val category = _state.value.category
        val loadedDepth = if (_state.value.hasLoadedList) _state.value.page else 1
        _state.update {
            it.copy(isLoading = !it.hasLoadedList, isRefreshing = it.hasLoadedList,
                // An automatic update replacing a manual pull keeps that indicator until completion.
                showRefreshIndicator = it.hasLoadedList && (showIndicator || it.showRefreshIndicator),
                isLoadingMore = false, error = null, loadMoreError = null)
        }
        listJob = scope.launch {
            try {
                if (debounce) waitForSearchDebounce()
                if (!ensureViewPermission()) return@launch
                var depth = loadedDepth
                var result = fetchFlights(category, 1, PAGE_SIZE, search.trim().ifEmpty { null })
                currentCoroutineContext().ensureActive()
                if (acceptStation(result.stationId)) depth = 1
                if (result.page != 1) throw IOException("Flight pagination changed. Refresh the list.")
                var fromCache = result.fromCache
                val rows = result.items.toMutableList()
                while (result.hasNextPage && result.page < depth) {
                    val next = fetchFlights(category, result.page + 1, PAGE_SIZE, search.trim().ifEmpty { null })
                    currentCoroutineContext().ensureActive()
                    if (acceptStation(next.stationId)) {
                        // A station transfer cannot combine the old first page with a new station's next page.
                        result = fetchFlights(category, 1, PAGE_SIZE, search.trim().ifEmpty { null })
                        currentCoroutineContext().ensureActive()
                        acceptStation(result.stationId)
                        rows.clear()
                        rows.addAll(result.items)
                        fromCache = result.fromCache
                        depth = 1
                    } else {
                        if (next.page != result.page + 1) throw IOException("Flight pagination changed. Refresh the list.")
                        result = next
                        rows.addAll(result.items)
                        fromCache = fromCache || result.fromCache
                    }
                }
                val eligible = rows.filter { it.matches(category) && it.isInFlightWindow(now()) }
                val retained = eligible.distinctBy { it.id }
                _state.update {
                    it.copy(items = retained, stationId = result.stationId ?: it.stationId, hasLoadedList = true,
                        retainedTabs = it.retainedTabs - category,
                        page = result.page, totalCount = (result.totalCount - (rows.size - eligible.size)).coerceAtLeast(0),
                        hasMore = result.hasNextPage, isLoading = false, isRefreshing = false, showRefreshIndicator = false,
                        permissionDenied = false, error = null, isUsingCachedList = fromCache)
                }
                scheduleExpiryChecks()
            } catch (error: CancellationException) {
                throw error
            } catch (error: Exception) {
                currentCoroutineContext().ensureActive()
                listFailed(error)
            }
        }
    }

    private fun acceptStation(stationId: String?): Boolean {
        if (stationId == null) return false
        val previous = _state.value.stationId ?: _state.value.detail?.stationId
        val changed = previous != null && previous != stationId
        if (changed) {
            normalTabs = emptyMap()
            clearSelectedFlight()
        }
        _state.update {
            if (!changed) it.copy(stationId = stationId) else it.copy(stationId = stationId,
                items = emptyList(), retainedTabs = emptyMap(), hasLoadedList = false,
                page = 1, totalCount = 0, hasMore = false, isLoading = true, isRefreshing = false, showRefreshIndicator = false)
        }
        return changed
    }

    fun loadMore() {
        val current = _state.value
        if (!current.hasMore || current.isLoading || current.isRefreshing || current.isLoadingMore) return
        _state.update { it.copy(isLoadingMore = true, error = null, loadMoreError = null) }
        listJob = scope.launch {
            try {
                val result = fetchFlights(current.category, current.page + 1, PAGE_SIZE, current.search.trim().ifEmpty { null })
                currentCoroutineContext().ensureActive()
                if (acceptStation(result.stationId)) {
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
                currentCoroutineContext().ensureActive()
                listFailed(error)
            }
        }
    }

    fun openFlight(id: String) {
        detailJob?.cancel()
        _state.update { current ->
            // Refresh an open flight in place; a different flight still needs its initial loader.
            val retained = current.detail?.takeIf {
                current.selectedFlightId == id && it.id == id && it.isInFlightWindow(now())
            }
            current.copy(selectedFlightId = id, lastUnavailableFlightId = null, detail = retained,
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
        if (!detailReadSucceeded) normalTabs = normalTabs.mapValues { (_, tab) -> tab.withoutFlight(id) }
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
                val withoutRow = current.copy(items = rows, totalCount = total,
                    retainedTabs = current.retainedTabs.mapValues { (_, tab) -> tab.withoutFlight(id) }, lastUnavailableFlightId = id,
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
                refreshSilently()
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
                    current.retainedTabs.values.flatMap { tab -> tab.items.map { it.scheduledDepartureUtc } } +
                    normalTabs.values.flatMap { tab -> tab.items.map { it.scheduledDepartureUtc } } +
                    listOfNotNull(current.detail?.scheduledDepartureUtc)
                val nextExpiry = departures.mapNotNull {
                    runCatching { parseFlightInstant(it).plus(12, ChronoUnit.HOURS) }.getOrNull()
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
        val tabs = current.retainedTabs.mapValues { (_, tab) -> tab.withinWindow(at) }
        normalTabs = normalTabs.mapValues { (_, tab) -> tab.withinWindow(at) }
        val expiredDetail = current.detail?.isInFlightWindow(at) == false ||
            current.items.any { it.id == current.selectedFlightId && !it.isInFlightWindow(at) }
        if (rows.size == current.items.size && tabs == current.retainedTabs && !expiredDetail) return
        if (expiredDetail) clearSelectedFlight()
        _state.update {
            it.copy(items = rows, retainedTabs = tabs,
                totalCount = (it.totalCount - (current.items.size - rows.size)).coerceAtLeast(0))
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
                clearVisibleFlights(permissionDenied = error.statusCode == 403)
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
        clearVisibleFlights(permissionDenied = true)
        clearCachedFlights()
        return false
    }

    private fun listFailed(error: Exception) {
        if (error is ApiException && error.statusCode in setOf(401, 403)) {
            clearVisibleFlights(permissionDenied = error.statusCode == 403)
            return
        }
        _state.update {
            if (it.isLoadingMore) {
                it.copy(isLoadingMore = false, loadMoreError = error.userMessage())
            } else {
                it.copy(permissionDenied = false, isLoading = false, isRefreshing = false, showRefreshIndicator = false,
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
