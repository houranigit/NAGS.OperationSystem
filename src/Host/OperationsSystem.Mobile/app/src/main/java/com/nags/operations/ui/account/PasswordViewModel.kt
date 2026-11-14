package com.nags.operations.ui.account

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
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

internal data class PasswordUiState(
    val current: String = "", val replacement: String = "", val confirmation: String = "",
    val isOnline: Boolean = false, val isSaving: Boolean = false, val attempted: Boolean = false,
    val changed: Boolean = false, val error: String? = null,
)

internal enum class PasswordProblem { CurrentRequired, Policy, Confirmation }
internal fun passwordProblems(state: PasswordUiState): Set<PasswordProblem> = buildSet {
    if (state.current.isEmpty()) add(PasswordProblem.CurrentRequired)
    val value = state.replacement
    if (value.length !in 8..128 || value.none(Char::isUpperCase) || value.none(Char::isLowerCase) ||
        value.none(Char::isDigit) || value.none { !it.isLetterOrDigit() }) add(PasswordProblem.Policy)
    if (state.confirmation != value || state.confirmation.isEmpty()) add(PasswordProblem.Confirmation)
}

/** Passwords live only in memory; an offline or failed request never changes device work or credentials. */
internal class PasswordViewModel(
    val online: StateFlow<Boolean>, private val change: suspend (String, String) -> Unit,
    private val coroutineScope: CoroutineScope? = null,
) : ViewModel() {
    constructor(repository: AuthRepository, network: NetworkMonitor) : this(network.isOnline, repository::changePassword)
    private val scope get() = coroutineScope ?: viewModelScope
    private val _state = MutableStateFlow(PasswordUiState(isOnline = online.value))
    val state = _state.asStateFlow()
    init { scope.launch { online.collect { value -> _state.update { it.copy(isOnline = value) } } } }
    fun current(value: String) { if (!_state.value.isSaving) _state.update { it.copy(current = value, error = null) } }
    fun replacement(value: String) { if (!_state.value.isSaving) _state.update { it.copy(replacement = value, error = null) } }
    fun confirmation(value: String) { if (!_state.value.isSaving) _state.update { it.copy(confirmation = value, error = null) } }
    fun save() {
        val input = _state.value
        if (input.isSaving || input.changed) return
        _state.update { it.copy(attempted = true) }
        if (!online.value || passwordProblems(input).isNotEmpty()) return
        _state.update { it.copy(isSaving = true, error = null) }
        scope.launch {
            try {
                change(input.current, input.replacement)
                _state.value = PasswordUiState(isOnline = online.value, changed = true)
            } catch (cancelled: CancellationException) {
                _state.update { it.copy(isSaving = false) }; throw cancelled
            } catch (failure: Exception) {
                _state.update { it.copy(isSaving = false, error = failure.userMessage()) }
            }
        }
    }
}
