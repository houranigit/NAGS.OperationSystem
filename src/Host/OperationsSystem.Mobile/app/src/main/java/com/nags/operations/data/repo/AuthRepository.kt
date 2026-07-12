package com.nags.operations.data.repo

import com.nags.operations.data.MobileTokensResponse
import com.nags.operations.data.TokenStore
import com.nags.operations.data.api.AuthApi
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull

/** Outcome of the first login step: signed in, or an MFA challenge to complete. */
sealed interface LoginOutcome {
    data object SignedIn : LoginOutcome
    data class MfaRequired(val mfaToken: String) : LoginOutcome
}

/**
 * Thin facade over [AuthApi]. The JWT pair is persisted here on the way in so individual
 * screens / view-models never have to know about token storage. After tokens land, the
 * profile is filled from `GET /api/v1/identity/me`.
 */
class AuthRepository(
    private val api: AuthApi,
    private val tokenStore: TokenStore,
    private val onAccountSwitch: suspend (previousSubject: String, newSubject: String) -> Unit = { _, _ -> },
    private val onSessionPublished: suspend () -> Unit = {},
) {
    suspend fun login(email: String, password: String): LoginOutcome {
        val response = api.login(email, password)

        if (response.mfaRequired) {
            return LoginOutcome.MfaRequired(requireNotNull(response.mfaToken))
        }

        completeSignIn(
            MobileTokensResponse(
                accessToken = requireNotNull(response.accessToken),
                accessTokenExpiresAtUtc = requireNotNull(response.accessTokenExpiresAtUtc),
                refreshToken = requireNotNull(response.refreshToken),
                refreshTokenExpiresAtUtc = requireNotNull(response.refreshTokenExpiresAtUtc),
            ),
            email,
        )
        return LoginOutcome.SignedIn
    }

    suspend fun loginMfa(mfaToken: String, code: String, email: String): LoginOutcome {
        val tokens = api.loginMfa(mfaToken, code)
        completeSignIn(tokens, email)
        return LoginOutcome.SignedIn
    }

    suspend fun logout() {
        // Best-effort server-side session revocation; local sign-out always succeeds.
        try {
            withTimeoutOrNull(BEST_EFFORT_NETWORK_TIMEOUT_MS) {
                api.logout(tokenStore.getRefreshToken())
            }
        } catch (e: Exception) {
            if (e is CancellationException) throw e
        } finally {
            withContext(NonCancellable) {
                api.clearBearerTokenCache()
                tokenStore.clearTokens()
            }
        }
    }

    private suspend fun completeSignIn(tokens: MobileTokensResponse, email: String) {
        api.clearBearerTokenCache()
        tokenStore.saveLogin(
            accessToken = tokens.accessToken,
            refreshToken = tokens.refreshToken,
            accessExpiresAt = tokens.accessTokenExpiresAtUtc,
            refreshExpiresAt = tokens.refreshTokenExpiresAtUtc,
            userId = "",
            displayName = email,
            email = email,
            onAccountSwitch = onAccountSwitch,
        )

        // Fill the real profile from /me now that the bearer token is stored.
        try {
            withTimeoutOrNull(BEST_EFFORT_NETWORK_TIMEOUT_MS) { api.me() }
                ?.let { me ->
                    tokenStore.saveIdentityProfile(
                        userId = me.id,
                        displayName = me.displayName,
                        email = me.email,
                    )
                }
        } catch (e: Exception) {
            // A caller cancellation is not a profile-fetch failure.
            if (e is CancellationException) throw e
            // Sync's /me refresh fills the profile later; signing in must not fail on this.
        }
        onSessionPublished()
    }

    private companion object {
        const val BEST_EFFORT_NETWORK_TIMEOUT_MS = 3_000L
    }
}
