package com.nags.operations.ui.notifications

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nags.operations.data.notifications.NotificationDto
import com.nags.operations.data.repo.NotificationsRepository
import com.nags.operations.data.userMessage
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.Job

class NotificationsViewModel(
    private val repository: NotificationsRepository,
) : ViewModel() {
    data class UiState(
        val items: List<NotificationDto> = emptyList(),
        val unreadOnly: Boolean = false,
        val unreadCount: Int = 0,
        val isLoading: Boolean = true,
        val isRefreshing: Boolean = false,
        val isLoadingMore: Boolean = false,
        val hasMore: Boolean = false,
        val page: Int = 1,
        val error: String? = null,
    )

    private val _state = MutableStateFlow(UiState())
    val state: StateFlow<UiState> = _state.asStateFlow()

    init {
        viewModelScope.launch {
            repository.observeUnreadCount().collect { count ->
                _state.update { it.copy(unreadCount = count) }
            }
        }
        observeItems(unreadOnly = false)
        refresh()
    }

    fun setUnreadOnly(value: Boolean) {
        if (_state.value.unreadOnly == value) return
        _state.update { it.copy(unreadOnly = value) }
        observeItems(value)
    }

    fun refresh(userInitiated: Boolean = false) {
        viewModelScope.launch {
            _state.update {
                it.copy(
                    isLoading = !userInitiated && it.items.isEmpty(),
                    isRefreshing = userInitiated,
                    error = null,
                )
            }
            runCatching { repository.refresh() }
                .onSuccess { page ->
                    _state.update {
                        it.copy(
                            isLoading = false,
                            isRefreshing = false,
                            page = page.page,
                            hasMore = page.hasNextPage,
                        )
                    }
                }
                .onFailure { error ->
                    _state.update {
                        it.copy(
                            isLoading = false,
                            isRefreshing = false,
                            error = if (it.items.isEmpty()) error.userMessage() else null,
                        )
                    }
                }
        }
    }

    fun loadMore() {
        val snapshot = _state.value
        if (!snapshot.hasMore || snapshot.isLoadingMore) return
        viewModelScope.launch {
            _state.update { it.copy(isLoadingMore = true) }
            runCatching { repository.refresh(snapshot.page + 1) }
                .onSuccess { page ->
                    _state.update {
                        it.copy(
                            isLoadingMore = false,
                            page = page.page,
                            hasMore = page.hasNextPage,
                        )
                    }
                }
                .onFailure { _state.update { it.copy(isLoadingMore = false) } }
        }
    }

    fun open(notification: NotificationDto, onOpenFlight: (String, String) -> Unit) {
        val flightId = notification.payload["flightId"] ?: return
        if (!notification.isRead) {
            viewModelScope.launch { repository.markRead(notification.id) }
        }
        onOpenFlight(notification.id, flightId)
    }

    fun archive(notification: NotificationDto) {
        viewModelScope.launch { runCatching { repository.archive(notification.id) } }
    }

    fun markAllRead() {
        viewModelScope.launch { runCatching { repository.markAllRead() } }
    }

    fun archiveAll(onFinished: () -> Unit = {}) {
        viewModelScope.launch {
            runCatching { repository.archiveAll() }
            onFinished()
        }
    }

    private var itemsJob: Job? = null
    private fun observeItems(unreadOnly: Boolean) {
        itemsJob?.cancel()
        itemsJob = viewModelScope.launch {
            repository.observeInbox(unreadOnly).collect { items ->
                _state.update { it.copy(items = items) }
            }
        }
    }
}
