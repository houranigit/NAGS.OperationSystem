package com.nags.operations.ui.account

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nags.operations.data.AuthenticatedUser
import com.nags.operations.data.network.NetworkMonitor
import com.nags.operations.data.repo.AuthRepository
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

enum class AccountError { LoadFailed, SaveUnconfirmed }

data class AccountUiState(
    val profile: AuthenticatedUser? = null,
    val isOnline: Boolean = false,
    val isLoading: Boolean = false,
    val isSaving: Boolean = false,
    val saved: Boolean = false,
    val error: AccountError? = null,
) {
    // A missing/failed profile must never appear as a confirmed opt-out or allow an opt-in.
    val canEditPreference: Boolean
        get() = profile != null && isOnline && !isLoading && !isSaving && error == null
}

class AccountViewModel internal constructor(
    private val online: StateFlow<Boolean>,
    private val fetchProfile: suspend () -> AuthenticatedUser,
    private val savePreference: suspend (Boolean) -> Unit,
    private val coroutineScope: CoroutineScope? = null,
) : ViewModel() {
    constructor(repository: AuthRepository, networkMonitor: NetworkMonitor) : this(
        online = networkMonitor.isOnline,
        fetchProfile = repository::profile,
        savePreference = repository::setWorkOrderEmailPreference,
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

    /** Called again when the profile resumes, so portal edits appear without a new login. */
    fun refresh() {
        val current = _state.value
        if (!online.value || current.isLoading || current.isSaving) return
        _state.update { it.copy(isLoading = true, error = null, saved = false) }
        scope.launch {
            try {
                val profile = fetchProfile()
                _state.update { it.copy(profile = profile, isLoading = false) }
            } catch (e: CancellationException) {
                _state.update { it.copy(isLoading = false) }
                throw e
            } catch (_: Exception) {
                _state.update { it.copy(isLoading = false, error = AccountError.LoadFailed) }
            }
        }
    }

    fun setReceiveWorkOrderSubmissionEmails(enabled: Boolean) {
        val current = _state.value
        if (!online.value || !current.canEditPreference ||
            current.profile?.receiveWorkOrderSubmissionEmails == enabled
        ) return
        _state.update { it.copy(isSaving = true, saved = false, error = null) }
        scope.launch {
            try {
                savePreference(enabled)
                // Publish the new value only after the server confirms the write.
                _state.update {
                    it.copy(
                        profile = it.profile?.copy(receiveWorkOrderSubmissionEmails = enabled),
                        isSaving = false,
                        saved = true,
                    )
                }
            } catch (e: CancellationException) {
                _state.update { it.copy(isSaving = false, error = AccountError.SaveUnconfirmed) }
                throw e
            } catch (_: Exception) {
                // A lost response may hide a successful write. Require a fresh read to reconcile.
                _state.update { it.copy(isSaving = false, error = AccountError.SaveUnconfirmed) }
            }
        }
    }
}
