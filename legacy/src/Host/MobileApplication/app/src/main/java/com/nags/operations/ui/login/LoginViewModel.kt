package com.nags.operations.ui.login

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nags.operations.data.repo.AuthRepository
import com.nags.operations.data.userMessage
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

/**
 * Owns the login form state and the single `login()` side effect. After the
 * JWT pair is persisted by [AuthRepository] the view fires [onSuccess] which
 * the navigation graph uses to replace the login destination with the home
 * placeholder.
 */
class LoginViewModel(private val authRepository: AuthRepository) : ViewModel() {

    data class LoginUiState(
        val email: String = "",
        val password: String = "",
        val isLoading: Boolean = false,
        val error: String? = null,
    )

    private val _state = MutableStateFlow(LoginUiState())
    val state: StateFlow<LoginUiState> = _state.asStateFlow()

    fun setEmail(value: String) = _state.update { it.copy(email = value, error = null) }
    fun setPassword(value: String) = _state.update { it.copy(password = value, error = null) }
    fun clearError() = _state.update { it.copy(error = null) }

    fun login(onSuccess: () -> Unit) {
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true, error = null) }
            try {
                authRepository.login(_state.value.email.trim(), _state.value.password)
                onSuccess()
            } catch (e: Exception) {
                _state.update { it.copy(error = e.userMessage(), isLoading = false) }
                return@launch
            }
            _state.update { it.copy(isLoading = false) }
        }
    }
}
