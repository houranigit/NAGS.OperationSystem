package com.nags.operations.ui.account

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nags.operations.data.notifications.NotificationPreferenceKey
import com.nags.operations.data.notifications.NotificationPreferences
import com.nags.operations.data.repo.NotificationPreferencesRepository
import com.nags.operations.data.userMessage
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class NotificationSettingsUiState(
    val preferences: NotificationPreferences? = null,
    val isOnline: Boolean = false,
    val isLoading: Boolean = false,
    val savingKey: NotificationPreferenceKey? = null,
    val error: String? = null,
) {
    val canEdit: Boolean get() = isOnline && preferences != null && !isLoading && savingKey == null
}

class NotificationSettingsViewModel internal constructor(
    private val online: StateFlow<Boolean>,
    private val cached: suspend () -> NotificationPreferences?,
    private val fetch: suspend () -> NotificationPreferences,
    private val patch: suspend (NotificationPreferenceKey, Boolean) -> NotificationPreferences,
    private val coroutineScope: CoroutineScope? = null,
) : ViewModel() {
    constructor(repository: NotificationPreferencesRepository, online: StateFlow<Boolean>) :
        this(online, repository::cached, repository::refresh, repository::update)

    private val scope: CoroutineScope get() = coroutineScope ?: viewModelScope
    private val _state = MutableStateFlow(NotificationSettingsUiState(isOnline = online.value))
    val state = _state.asStateFlow()

    init {
        scope.launch {
            _state.update { it.copy(preferences = cached()) }
            online.collect { connected ->
                _state.update { it.copy(isOnline = connected) }
                if (connected) refresh()
            }
        }
    }

    fun refresh() {
        if (!online.value || _state.value.isLoading || _state.value.savingKey != null) return
        _state.update { it.copy(isLoading = true, error = null) }
        scope.launch {
            try {
                val values = fetch()
                _state.update { it.copy(preferences = values, isLoading = false) }
            } catch (cancelled: CancellationException) {
                _state.update { it.copy(isLoading = false) }
                throw cancelled
            } catch (failure: Exception) {
                _state.update { it.copy(isLoading = false, error = failure.userMessage()) }
            }
        }
    }

    fun setPreference(key: NotificationPreferenceKey, enabled: Boolean) {
        if (!_state.value.canEdit || _state.value.preferences?.value(key) == enabled) return
        _state.update { it.copy(savingKey = key, error = null) }
        scope.launch {
            try {
                val values = patch(key, enabled)
                _state.update { it.copy(preferences = values, savingKey = null) }
            } catch (cancelled: CancellationException) {
                _state.update { it.copy(savingKey = null) }
                throw cancelled
            } catch (failure: Exception) {
                _state.update { it.copy(savingKey = null, error = failure.userMessage()) }
            }
        }
    }
}
