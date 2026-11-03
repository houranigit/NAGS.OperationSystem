package com.nags.operations.ui.account

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nags.operations.data.AuthenticatedUser
import com.nags.operations.data.network.NetworkMonitor
import com.nags.operations.data.repo.AuthRepository
import com.nags.operations.data.userMessage
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class AccountUiState(
    val profile: AuthenticatedUser? = null,
    val isOnline: Boolean = false,
    val isLoading: Boolean = false,
    val error: String? = null,
)

class AccountViewModel internal constructor(
    private val online: StateFlow<Boolean>,
    private val fetchProfile: suspend () -> AuthenticatedUser,
    private val coroutineScope: CoroutineScope? = null,
) : ViewModel() {
    constructor(repository: AuthRepository, networkMonitor: NetworkMonitor) : this(
        online = networkMonitor.isOnline,
        fetchProfile = repository::profile,
    )

    private val scope: CoroutineScope get() = coroutineScope ?: viewModelScope
    private val _state = MutableStateFlow(AccountUiState(isOnline = online.value))
    val state: StateFlow<AccountUiState> = _state.asStateFlow()

    init {
        scope.launch {
            online.collect { connected ->
                _state.update { it.copy(isOnline = connected) }
                if (connected) refresh()
            }
        }
    }

    fun refresh() {
        if (!online.value || _state.value.isLoading) return
        _state.update { it.copy(isLoading = true, error = null) }
        scope.launch {
            try {
                val profile = fetchProfile()
                _state.update { it.copy(profile = profile, isLoading = false) }
            } catch (error: CancellationException) {
                _state.update { it.copy(isLoading = false) }
                throw error
            } catch (error: Exception) {
                _state.update { it.copy(isLoading = false, error = error.userMessage()) }
            }
        }
    }
}
