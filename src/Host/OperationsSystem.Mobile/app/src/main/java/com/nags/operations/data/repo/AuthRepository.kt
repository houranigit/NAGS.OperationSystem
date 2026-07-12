package com.nags.operations.data.repo

import com.nags.operations.data.MobileTokensResponse
import com.nags.operations.data.TokenStore
import com.nags.operations.data.api.AuthApi

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
            api.logout(tokenStore.getRefreshToken())
        } catch (_: Exception) {
        }
        tokenStore.clear()
    }

    private suspend fun completeSignIn(tokens: MobileTokensResponse, email: String) {
        tokenStore.saveLogin(
            accessToken = tokens.accessToken,
            refreshToken = tokens.refreshToken,
            accessExpiresAt = tokens.accessTokenExpiresAtUtc,
            refreshExpiresAt = tokens.refreshTokenExpiresAtUtc,
            userId = "",
            displayName = email,
            email = email,
        )

        // Fill the real profile from /me now that the bearer token is stored.
        try {
            val me = api.me()
            tokenStore.saveLogin(
                accessToken = tokens.accessToken,
                refreshToken = tokens.refreshToken,
                accessExpiresAt = tokens.accessTokenExpiresAtUtc,
                refreshExpiresAt = tokens.refreshTokenExpiresAtUtc,
                userId = me.id,
                displayName = me.displayName,
                email = me.email,
            )
        } catch (_: Exception) {
            // Sync's /me refresh fills the profile later; signing in must not fail on this.
        }
    }
}
