package com.nags.operations.data.repo

import com.nags.operations.data.LoginResponse
import com.nags.operations.data.TokenStore
import com.nags.operations.data.api.AuthApi

/**
 * Thin facade over [AuthApi]. The JWT pair is persisted here on the way in so
 * individual screens / view-models never have to know about token storage.
 */
class AuthRepository(
    private val api: AuthApi,
    private val tokenStore: TokenStore,
) {
    suspend fun login(emailOrUsername: String, password: String): LoginResponse {
        val r = api.login(emailOrUsername, password)
        tokenStore.saveLogin(
            accessToken = r.accessToken,
            refreshToken = r.refreshToken,
            accessExpiresAt = r.accessTokenExpiresAt,
            refreshExpiresAt = r.refreshTokenExpiresAt,
            userId = r.userId,
            displayName = r.username,
            email = r.email,
        )
        return r
    }

    suspend fun logout() {
        tokenStore.clear()
    }
}
