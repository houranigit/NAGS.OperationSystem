package com.nags.operations.data.api

import com.nags.operations.data.LoginRequest
import com.nags.operations.data.LoginResponse
import com.nags.operations.data.TokenStore
import com.nags.operations.data.api.HttpClientFactory.bodyOrThrow
import io.ktor.client.HttpClient
import io.ktor.client.request.post
import io.ktor.client.request.setBody
import io.ktor.http.ContentType
import io.ktor.http.contentType

/**
 * Thin wrapper over the Identity module's auth endpoints. Each method maps 1:1
 * to a route exposed by `Identity.Presentation/Endpoints/AuthEndpoints.cs`.
 */
class AuthApi(
    private val tokenStore: TokenStore,
    private val client: HttpClient = HttpClientFactory.create(tokenStore),
) {
    private fun url(path: String) = HttpClientFactory.url(path)

    suspend fun login(
        emailOrUsername: String,
        password: String,
        userAgent: String? = null,
    ): LoginResponse {
        // Drop any stale token before sending the login call so the Bearer plugin's
        // `sendWithoutRequest` filter doesn't pre-fetch a refresh on the way in.
        tokenStore.clear()
        val response = client.post(url("api/identity/auth/login")) {
            contentType(ContentType.Application.Json)
            setBody(
                LoginRequest(
                    emailOrUsername = emailOrUsername,
                    password = password,
                    userAgent = userAgent,
                )
            )
        }
        return response.bodyOrThrow()
    }
}
