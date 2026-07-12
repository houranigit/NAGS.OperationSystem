package com.nags.operations.data.api

import com.nags.operations.BuildConfig
import com.nags.operations.data.MobileTokensResponse
import com.nags.operations.data.RefreshRequest
import com.nags.operations.data.TokenStore
import io.ktor.client.HttpClient
import io.ktor.client.call.body
import io.ktor.client.engine.android.Android
import io.ktor.client.plugins.HttpTimeout
import io.ktor.client.plugins.auth.Auth
import io.ktor.client.plugins.auth.providers.BearerTokens
import io.ktor.client.plugins.auth.providers.bearer
import io.ktor.client.plugins.contentnegotiation.ContentNegotiation
import io.ktor.client.plugins.logging.LogLevel
import io.ktor.client.plugins.logging.Logging
import io.ktor.client.request.headers
import io.ktor.client.request.post
import io.ktor.client.request.setBody
import io.ktor.client.statement.HttpResponse
import io.ktor.client.statement.bodyAsText
import io.ktor.http.ContentType
import io.ktor.http.contentType
import io.ktor.http.isSuccess
import io.ktor.serialization.kotlinx.json.json
import kotlinx.serialization.json.Json

/**
 * Single source of truth for the JWT-aware Ktor [HttpClient]. The `Auth`
 * plugin attaches the bearer header on every request and automatically calls
 * `refreshTokens` whenever the server responds with 401, so feature layers
 * never have to know about token expiry. If the refresh call itself fails the
 * token store is cleared and the next screen re-routes to login.
 */
object HttpClientFactory {
    val baseUrl: String = BuildConfig.API_BASE_URL.trimEnd('/') + "/"

    val json = Json {
        ignoreUnknownKeys = true
        isLenient = true
        encodeDefaults = true
    }

    fun url(path: String): String = baseUrl + path.trimStart('/')

    fun create(tokenStore: TokenStore): HttpClient = HttpClient(Android) {
        expectSuccess = false
        install(HttpTimeout) {
            requestTimeoutMillis = 60_000
        }
        install(ContentNegotiation) {
            json(json)
        }
        install(Logging) {
            level = LogLevel.INFO
        }
        install(Auth) {
            bearer {
                loadTokens {
                    val access = tokenStore.getAccessToken() ?: return@loadTokens null
                    val refresh = tokenStore.getRefreshToken().orEmpty()
                    BearerTokens(access, refresh)
                }
                refreshTokens {
                    val refreshToken = tokenStore.getRefreshToken() ?: return@refreshTokens null
                    val refreshClient = HttpClient(Android) {
                        expectSuccess = false
                        install(ContentNegotiation) { json(json) }
                    }
                    try {
                        val response = refreshClient.post(url("api/v1/identity/auth/mobile/refresh")) {
                            contentType(ContentType.Application.Json)
                            headers { /* explicitly no Authorization header */ }
                            setBody(RefreshRequest(refreshToken))
                        }
                        if (!response.status.isSuccess()) {
                            tokenStore.clear()
                            null
                        } else {
                            val body = response.body<MobileTokensResponse>()
                            tokenStore.saveTokensAfterRefresh(
                                accessToken = body.accessToken,
                                refreshToken = body.refreshToken,
                                accessExpiresAt = body.accessTokenExpiresAtUtc,
                                refreshExpiresAt = body.refreshTokenExpiresAtUtc,
                            )
                            BearerTokens(body.accessToken, body.refreshToken)
                        }
                    } catch (_: Exception) {
                        // Network blip — keep the existing tokens so a retry can succeed.
                        null
                    } finally {
                        refreshClient.close()
                    }
                }
                // Login and refresh are anonymous calls — return false so they aren't
                // pre-decorated with a stale Authorization header from a prior session.
                sendWithoutRequest { request ->
                    val path = request.url.toString()
                    !path.contains("/auth/mobile/login") &&
                        !path.contains("/auth/mobile/refresh")
                }
            }
        }
    }

    suspend inline fun <reified T> HttpResponse.bodyOrThrow(): T {
        if (!status.isSuccess()) {
            throw com.nags.operations.data.ApiException(status.value, bodyAsText())
        }
        return body()
    }
}
