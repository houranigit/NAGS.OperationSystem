package com.nags.operations.data.api

import com.nags.operations.data.AuthenticatedUser
import com.nags.operations.data.LoginMfaRequest
import com.nags.operations.data.LoginRequest
import com.nags.operations.data.MobileLoginResponse
import com.nags.operations.data.MobileLogoutRequest
import com.nags.operations.data.MobileTokensResponse
import com.nags.operations.data.TokenStore
import com.nags.operations.data.WorkOrderEmailPreferenceRequest
import com.nags.operations.data.ApiException
import com.nags.operations.data.api.HttpClientFactory.bodyOrThrow
import io.ktor.client.HttpClient
import io.ktor.client.plugins.auth.authProvider
import io.ktor.client.plugins.auth.providers.BearerAuthProvider
import io.ktor.client.request.get
import io.ktor.client.request.post
import io.ktor.client.request.put
import io.ktor.client.request.setBody
import io.ktor.client.statement.bodyAsText
import io.ktor.http.ContentType
import io.ktor.http.contentType
import io.ktor.http.isSuccess

/**
 * Thin wrapper over the Identity module's mobile auth endpoints
 * (`/api/v1/identity/auth/mobile/...`). Unlike the web portal, mobile receives the refresh
 * token in the JSON body and stores it in secure device storage.
 */
class AuthApi(
    private val tokenStore: TokenStore,
    private val client: HttpClient = HttpClientFactory.create(tokenStore),
) {
    private fun url(path: String) = HttpClientFactory.url(path)

    suspend fun login(email: String, password: String): MobileLoginResponse {
        // Login is excluded by sendWithoutRequest. Clearing Ktor's in-memory bearer cache here
        // ensures the first authenticated call after login reloads the newly published token.
        clearBearerTokenCache()
        val response = client.post(url("api/v1/identity/auth/mobile/login")) {
            contentType(ContentType.Application.Json)
            setBody(LoginRequest(email = email, password = password))
        }
        return response.bodyOrThrow()
    }

    suspend fun loginMfa(mfaToken: String, code: String): MobileTokensResponse {
        val response = client.post(url("api/v1/identity/auth/mobile/login/mfa")) {
            contentType(ContentType.Application.Json)
            setBody(LoginMfaRequest(mfaToken = mfaToken, code = code))
        }
        return response.bodyOrThrow()
    }

    /** Current user profile + permissions; used right after login to fill the token store. */
    suspend fun me(): AuthenticatedUser {
        val response = client.get(url("api/v1/identity/me"))
        return response.bodyOrThrow()
    }

    /** Saved on the signed-in account, shared with the portal; never queued offline. */
    suspend fun setWorkOrderEmailPreference(enabled: Boolean) {
        val response = client.put(url("api/v1/identity/me/work-order-email-preference")) {
            contentType(ContentType.Application.Json)
            setBody(WorkOrderEmailPreferenceRequest(enabled))
        }
        if (!response.status.isSuccess()) {
            throw ApiException(response.status.value, response.bodyAsText())
        }
    }

    /** Best-effort server-side session revocation; local state is cleared regardless. */
    suspend fun logout(refreshToken: String?) {
        client.post(url("api/v1/identity/auth/mobile/logout")) {
            contentType(ContentType.Application.Json)
            setBody(MobileLogoutRequest(refreshToken))
        }
    }

    fun clearBearerTokenCache() {
        client.authProvider<BearerAuthProvider>()?.clearToken()
    }
}
