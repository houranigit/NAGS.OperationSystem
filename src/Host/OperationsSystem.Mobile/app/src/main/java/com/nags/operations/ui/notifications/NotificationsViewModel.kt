package com.nags.operations.ui.notifications

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nags.operations.data.notifications.CategoryReadAcknowledgement
import com.nags.operations.data.notifications.CategoryReadResult
import com.nags.operations.data.notifications.NotificationDto
import com.nags.operations.data.notifications.NotificationOpenRequest
import com.nags.operations.data.notifications.NotificationPageDto
import com.nags.operations.data.notifications.flightId
import com.nags.operations.data.repo.FlightsRepository
import com.nags.operations.data.repo.NotificationsRepository
import com.nags.operations.data.userMessage
import java.time.Instant
import java.time.OffsetDateTime
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.flow.emptyFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

class NotificationsViewModel internal constructor(
    private val inbox: () -> Flow<List<NotificationDto>>,
    private val fetchPage: suspend (Int) -> NotificationPageDto,
    private val acknowledgeInbox: suspend () -> CategoryReadResult?,
    private val markReadOnOpen: (String) -> Unit,
    private val archiveNotification: suspend (String) -> Unit,
    private val archiveInbox: suspend () -> Unit,
    readAcknowledgements: Flow<CategoryReadAcknowledgement> = emptyFlow(),
    private val coroutineScope: CoroutineScope? = null,
    localFlightIds: Flow<Set<String>> = flowOf(emptySet()),
) : ViewModel() {
    constructor(repository: NotificationsRepository, flights: FlightsRepository) : this(
        inbox = { repository.observeInbox(unreadOnly = false) },
        fetchPage = { page -> repository.refresh(page = page) },
        acknowledgeInbox = repository::acknowledgeInbox,
        markReadOnOpen = repository::markReadOnOpen,
        archiveNotification = repository::archive,
        archiveInbox = repository::archiveAll,
        readAcknowledgements = repository.observeReadAcknowledgements(),
        localFlightIds = flights.syncedFlightIds,
    )

    data class UiState(
        val items: List<NotificationDto> = emptyList(),
        val newIds: Set<String> = emptySet(),
        val visitId: Long = 0,
        val isLoading: Boolean = true,
        val isRefreshing: Boolean = false,
        val isLoadingMore: Boolean = false,
        val isFromCache: Boolean = false,
        val hasMore: Boolean = false,
        val page: Int = 1,
        val error: String? = null,
        val loadMoreError: String? = null,
        val syncedFlightIds: Set<String> = emptySet(),
        val archivingIds: Set<String> = emptySet(),
        val isArchivingAll: Boolean = false,
        val actionError: String? = null,
    )

    private val _state = MutableStateFlow(UiState())
    val state: StateFlow<UiState> = _state.asStateFlow()
    private val scope: CoroutineScope get() = coroutineScope ?: viewModelScope
    private var itemsJob: Job? = null
    private var refreshJob: Job? = null
    private var loadMoreJob: Job? = null
    private var acknowledgeJob: Job? = null
    private var requestVersion = 0L
    private var observerVersion = 0L
    private var visible = false
    private var hasAcknowledgedVisit = false
    private var acknowledgedThrough: Instant? = null
    private val acknowledgedIds = mutableSetOf<String>()
    private val visitReadStamps = mutableSetOf<Instant>()
    private val visitLocalReadStamps = mutableSetOf<Instant>()

    init {
        observeItems()
        scope.launch {
            readAcknowledgements.collect { receipt ->
                val local = parseStamp(receipt.localReadAtUtc)
                val server = parseStamp(receipt.serverReadAtUtc)
                if (visible && local != null &&
                    local in visitLocalReadStamps && server != null) {
                    visitReadStamps.add(server)
                    _state.update { it.copy(newIds = it.newIds + newlyReadIds(it.items)) }
                }
            }
        }
        scope.launch {
            localFlightIds.collect { ids ->
                _state.update { it.copy(syncedFlightIds = ids.mapTo(mutableSetOf()) { id -> id.lowercase() }) }
            }
        }
    }

    fun beginVisit() {
        if (visible) return
        visible = true
        resetVisit()
        refresh()
    }

    fun endVisit() {
        visible = false
        acknowledgeJob?.cancel()
        acknowledgeJob = null
    }

    private fun resetVisit() {
        acknowledgeJob?.cancel()
        acknowledgeJob = null
        hasAcknowledgedVisit = false
        acknowledgedThrough = null
        acknowledgedIds.clear()
        visitReadStamps.clear()
        visitLocalReadStamps.clear()
        _state.update { it.copy(visitId = it.visitId + 1, actionError = null,
            newIds = it.items.filterNot(NotificationDto::isRead).mapTo(mutableSetOf()) { row -> row.id }) }
    }

    /** Called after the inbox has rendered its loaded rows. */
    fun acknowledgeLoadedInbox() {
        val snapshot = _state.value
        if (!visible || snapshot.items.isEmpty() || acknowledgeJob?.isActive == true) return
        val cutoff = snapshot.items.mapNotNull { parseStamp(it.createdAtUtc) }.maxOrNull()
        val previousCutoff = acknowledgedThrough
        val hasNewerRows = cutoff != null && (previousCutoff == null || cutoff > previousCutoff)
        val unreadIds = snapshot.items.filterNot(NotificationDto::isRead).mapTo(mutableSetOf()) { it.id }
        if (hasAcknowledgedVisit && !hasNewerRows && unreadIds.all(acknowledgedIds::contains)) return
        _state.update { it.copy(newIds = it.newIds + unreadIds) }
        val visit = snapshot.visitId
        acknowledgeJob = scope.launch {
            var acknowledged = false
            try {
                val result = acknowledgeInbox() ?: return@launch
                if (!isCurrentVisit(visit)) return@launch
                hasAcknowledgedVisit = true
                acknowledgedIds.addAll(snapshot.items.map { it.id })
                val through = parseStamp(result.throughCreatedAtUtc) ?: cutoff
                acknowledgedThrough = listOfNotNull(acknowledgedThrough, through).maxOrNull()
                parseStamp(result.localReadAtUtc)?.let {
                    visitReadStamps.add(it)
                    visitLocalReadStamps.add(it)
                }
                parseStamp(result.serverReadAtUtc)?.let(visitReadStamps::add)
                _state.update { it.copy(newIds = it.newIds + newlyReadIds(it.items)) }
                acknowledged = true
            } catch (error: CancellationException) {
                throw error
            } catch (_: Exception) {
                // A failed local write can be retried by a later render or refresh.
            } finally {
                if (_state.value.visitId == visit) acknowledgeJob = null
            }
            // New rows can arrive while the repository completes its durable acknowledgment.
            if (acknowledged && isCurrentVisit(visit)) acknowledgeLoadedInbox()
        }
    }

    fun refresh(userInitiated: Boolean = false) {
        val version = ++requestVersion
        refreshJob?.cancel()
        loadMoreJob?.cancel()
        _state.update {
            it.copy(isLoading = !userInitiated && it.items.isEmpty(), isRefreshing = userInitiated,
                isLoadingMore = false, error = null, loadMoreError = null)
        }
        refreshJob = scope.launch {
            try {
                val page = fetchPage(1)
                if (!isCurrentRequest(version)) return@launch
                _state.update {
                    it.copy(isLoading = false, isRefreshing = false, page = page.page,
                        hasMore = page.hasNextPage, isFromCache = page.isFromCache)
                }
            } catch (error: CancellationException) {
                throw error
            } catch (error: Exception) {
                if (!isCurrentRequest(version)) return@launch
                _state.update { it.copy(isLoading = false, isRefreshing = false, error = error.userMessage()) }
            }
        }
    }

    fun loadMore() {
        val snapshot = _state.value
        if (!snapshot.hasMore || snapshot.isLoading || snapshot.isRefreshing || snapshot.isLoadingMore || !visible) return
        val version = requestVersion
        _state.update { it.copy(isLoadingMore = true, loadMoreError = null) }
        loadMoreJob = scope.launch {
            try {
                val page = fetchPage(snapshot.page + 1)
                if (!isCurrentRequest(version)) return@launch
                _state.update { it.copy(isLoadingMore = false, page = page.page, hasMore = page.hasNextPage, isFromCache = page.isFromCache) }
            } catch (error: CancellationException) {
                throw error
            } catch (error: Exception) {
                if (isCurrentRequest(version)) {
                    _state.update { it.copy(isLoadingMore = false, loadMoreError = error.userMessage()) }
                }
            }
        }
    }

    fun open(notification: NotificationDto, onOpenFlight: (NotificationOpenRequest) -> Unit,
        onOpenSchedule: (NotificationOpenRequest) -> Unit = {}) {
        if (!notification.isRead) markReadOnOpen(notification.id)
        if (!canOpenNotification(notification, _state.value.syncedFlightIds)) return
        onOpenFlight(NotificationOpenRequest(notificationId = notification.id, flightId = notification.flightId(),
            kind = notification.kind, scheduledArrivalUtc = notification.payload["scheduledArrivalUtc"],
            leadTimeMinutes = notification.payload["leadTimeMinutes"]?.toIntOrNull()))
    }

    fun archive(notification: NotificationDto, onFailure: () -> Unit = {}) {
        if (notification.id in _state.value.archivingIds) return
        _state.update { it.copy(archivingIds = it.archivingIds + notification.id, actionError = null) }
        scope.launch {
            try { archiveNotification(notification.id) }
            catch (error: CancellationException) { throw error }
            catch (error: Exception) {
                _state.update { it.copy(actionError = error.userMessage()) }
                onFailure()
            } finally { _state.update { it.copy(archivingIds = it.archivingIds - notification.id) } }
        }
    }

    fun archiveAll() {
        if (_state.value.isArchivingAll) return
        _state.update { it.copy(isArchivingAll = true, actionError = null) }
        scope.launch {
            try { archiveInbox(); refresh() }
            catch (error: CancellationException) { throw error }
            catch (error: Exception) { _state.update { it.copy(actionError = error.userMessage()) } }
            finally { _state.update { it.copy(isArchivingAll = false) } }
        }
    }

    private fun isCurrentRequest(version: Long): Boolean = requestVersion == version

    private fun isCurrentVisit(visit: Long): Boolean = visible && _state.value.visitId == visit

    private fun newlyReadIds(items: List<NotificationDto>): Set<String> = items.filter {
        !it.isRead || parseStamp(it.readAtUtc)?.let(visitReadStamps::contains) == true
    }.mapTo(mutableSetOf()) { it.id }

    private fun observeItems() {
        val version = ++observerVersion
        itemsJob?.cancel()
        itemsJob = scope.launch {
            inbox().collect { items ->
                if (observerVersion == version) {
                    _state.update { it.copy(items = items, newIds = it.newIds + if (visible) newlyReadIds(items) else emptySet()) }
                }
            }
        }
    }
}

/** Page changes also trigger prefetch when a response overlaps already-cached history. */
internal fun nextNotificationPageToLoad(state: NotificationsViewModel.UiState, lastVisibleIndex: Int,
    renderedRowCount: Int = state.items.size): Int? {
    if (!state.hasMore || state.isFromCache || state.isLoading || state.isRefreshing || state.isLoadingMore ||
        state.error != null || state.loadMoreError != null) return null
    return state.page.takeIf { state.items.isEmpty() || lastVisibleIndex >= renderedRowCount - 4 }
}

private fun parseStamp(value: String?): Instant? = value?.let { runCatching { OffsetDateTime.parse(it).toInstant() }.getOrNull() }
