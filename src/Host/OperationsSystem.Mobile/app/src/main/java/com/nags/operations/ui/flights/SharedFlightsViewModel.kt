package com.nags.operations.ui.flights

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nags.operations.data.ApiException
import com.nags.operations.data.FlightShareDirection
import com.nags.operations.data.FlightSharePage
import com.nags.operations.data.FlightSharePost
import com.nags.operations.data.isInFlightWindow
import com.nags.operations.data.network.NetworkMonitor
import com.nags.operations.data.repo.SharedFlightsRepository
import com.nags.operations.data.userMessage
import java.time.Instant
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.emptyFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

internal data class SharedFlightsUiState(
    val items: List<FlightSharePost> = emptyList(),
    val stationId: String? = null,
    val ownerSessionKey: String? = null,
    val page: Int = 1,
    val totalCount: Long = 0,
    val hasMore: Boolean = false,
    val loaded: Boolean = false,
    val loading: Boolean = false,
    val refreshing: Boolean = false,
    val loadingMore: Boolean = false,
    val fromCache: Boolean = false,
    val online: Boolean = true,
    val error: String? = null,
    val moreError: String? = null,
)

/** Post identity is separate from flight identity: sharing twice produces two feed entries. */
internal class SharedFlightsViewModel(
    private val fetch: suspend (FlightShareDirection, String, Int, Int) -> FlightSharePage,
    private val cached: suspend (FlightShareDirection, String, Int, Int) -> FlightSharePage?,
    changes: Flow<Unit> = emptyFlow(),
    online: StateFlow<Boolean> = MutableStateFlow(true),
    scope: CoroutineScope? = null,
    private val now: () -> Instant = { Instant.now() },
    private val scopeCurrent: suspend (String, String?) -> Boolean = { _, _ -> true },
    private val reconcile: suspend (List<FlightSharePost>, String?) -> List<FlightSharePost> = { rows, _ -> rows },
) : ViewModel() {
    constructor(repository: SharedFlightsRepository, network: NetworkMonitor) : this(
        repository::page, repository::cachedPage, repository.changes, network.isOnline,
        scopeCurrent = repository::isCurrentScope, reconcile = repository::reconcileVisible)

    private val work = scope ?: viewModelScope
    private val _state = MutableStateFlow(SharedFlightsUiState(online = online.value))
    val state = _state.asStateFlow()
    private var request: Job? = null
    private var revision = 0L

    init {
        work.launch {
            online.collect { connected ->
                val wasOnline = _state.value.online
                _state.update { it.copy(online = connected) }
                if (connected && !wasOnline) refresh(false)
            }
        }
        work.launch { changes.collect { refreshCached() } }
    }

    fun refresh(showIndicator: Boolean = true) = load(1, showIndicator)
    fun loadMore() {
        val current = _state.value
        if (request?.isActive == true || !current.hasMore || current.loading || current.refreshing || current.loadingMore) return
        load(current.page + 1, false)
    }

    private fun load(number: Int, showIndicator: Boolean) {
        request?.cancel()
        val stamp = ++revision
        _state.update { it.copy(loading = number == 1 && !it.loaded,
            refreshing = number == 1 && it.loaded && showIndicator, loadingMore = number > 1,
            error = null, moreError = null) }
        request = work.launch {
            try {
                val result = fetch(FlightShareDirection.All, "", number, PAGE_SIZE)
                currentCoroutineContext().ensureActive()
                if (stamp != revision) return@launch
                accept(result, append = number > 1)
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (failure: Exception) {
                currentCoroutineContext().ensureActive()
                if (stamp != revision) return@launch
                val denied = failure is ApiException && failure.statusCode in setOf(401, 403)
                _state.update { it.copy(items = if (denied) emptyList() else it.items,
                    loading = false, refreshing = false, loadingMore = false,
                    hasMore = if (denied) false else it.hasMore,
                    error = if (number == 1) failure.userMessage() else it.error,
                    moreError = if (number > 1) failure.userMessage() else null) }
            }
        }
    }

    /** Sync invalidation reads the complete local manifest; it never loops back into network publishing. */
    private suspend fun refreshCached() {
        val target = _state.value
        val stamp = revision
        try {
            val pages = (1..target.page).map { cached(FlightShareDirection.All, "", it, PAGE_SIZE) }
            if (stamp != revision || _state.value.loadingMore) return
            if (pages.any { it == null }) {
                val stillAuthorized = target.stationId?.let { scopeCurrent(it, target.ownerSessionKey) } == true
                val retained = if (stillAuthorized) reconcile(target.items, target.ownerSessionKey) else emptyList()
                if (stamp != revision) return
                _state.update { it.copy(items = retained,
                    totalCount = if (stillAuthorized) (it.totalCount - (target.items.size - retained.size)).coerceAtLeast(0) else 0,
                    hasMore = stillAuthorized && it.hasMore) }
            } else {
                val last = pages.last()!!
                accept(last.copy(items = pages.filterNotNull().flatMap { it.items }), false, keepPage = true)
            }
        } catch (cancelled: CancellationException) { throw cancelled }
        catch (_: Exception) { expire() }
    }

    fun expire() {
        val at = now()
        _state.update { current ->
            val retained = current.items.filter { it.flight.isInFlightWindow(at) }
            current.copy(items = retained, totalCount = (current.totalCount - (current.items.size - retained.size)).coerceAtLeast(0))
        }
    }

    private fun accept(result: FlightSharePage, append: Boolean, keepPage: Boolean = false) {
        val at = now()
        _state.update { current ->
            val sameStation = current.stationId == null || current.stationId == result.stationId
            val previous = if (append && sameStation) current.items else emptyList()
            current.copy(items = (previous + result.items).distinctBy { it.id }.filter { it.flight.isInFlightWindow(at) },
                stationId = result.stationId, ownerSessionKey = result.ownerSessionKey, page = if (keepPage) current.page else result.page,
                totalCount = result.totalCount, hasMore = result.hasNextPage, loaded = true,
                loading = false, refreshing = false, loadingMore = false, fromCache = result.fromCache,
                error = null, moreError = null)
        }
    }

    private companion object { const val PAGE_SIZE = 20 }
}
