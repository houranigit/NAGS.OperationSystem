package com.nags.operations.ui.login

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nags.operations.data.repo.AuthRepository
import com.nags.operations.data.repo.LoginOutcome
import com.nags.operations.data.userMessage
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

/**
 * Owns the login form state and the sign-in side effects. Supports the two-step MFA flow:
 * when the server answers with a challenge, the screen shows a code field and completes
 * sign-in via [confirmMfa]. After the JWT pair is persisted by [AuthRepository] the view
 * fires `onSuccess`, which the navigation graph uses to replace the login destination.
 */
class LoginViewModel(private val authRepository: AuthRepository) : ViewModel() {

    data class LoginUiState(
        val email: String = "",
        val password: String = "",
        val isLoading: Boolean = false,
        val error: String? = null,
        /** Non-null while the server is waiting for the second (TOTP) step. */
        val mfaToken: String? = null,
        val mfaCode: String = "",
    )

    private val _state = MutableStateFlow(LoginUiState())
    val state: StateFlow<LoginUiState> = _state.asStateFlow()

    fun setEmail(value: String) = _state.update { it.copy(email = value, error = null) }
    fun setPassword(value: String) = _state.update { it.copy(password = value, error = null) }
    fun setMfaCode(value: String) = _state.update { it.copy(mfaCode = value, error = null) }
    fun clearError() = _state.update { it.copy(error = null) }

    fun cancelMfa() = _state.update { it.copy(mfaToken = null, mfaCode = "", error = null) }

    fun login(onSuccess: () -> Unit) {
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true, error = null) }
            try {
                when (val outcome = authRepository.login(_state.value.email.trim(), _state.value.password)) {
                    is LoginOutcome.SignedIn -> {
                        onSuccess()
                        _state.update { it.copy(isLoading = false) }
                    }
                    is LoginOutcome.MfaRequired -> _state.update {
                        it.copy(isLoading = false, mfaToken = outcome.mfaToken, mfaCode = "")
                    }
                }
            } catch (e: Exception) {
                _state.update { it.copy(error = e.userMessage(), isLoading = false) }
            }
        }
    }

    fun confirmMfa(onSuccess: () -> Unit) {
        val snapshot = _state.value
        val token = snapshot.mfaToken ?: return
        if (snapshot.mfaCode.isBlank()) return
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true, error = null) }
            try {
                authRepository.loginMfa(token, snapshot.mfaCode.trim(), snapshot.email.trim())
                onSuccess()
                _state.update { it.copy(isLoading = false, mfaToken = null, mfaCode = "") }
            } catch (e: Exception) {
                _state.update { it.copy(error = e.userMessage(), isLoading = false) }
            }
        }
    }
}
