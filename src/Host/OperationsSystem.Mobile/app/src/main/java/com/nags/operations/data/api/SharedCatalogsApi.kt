package com.nags.operations.data.api

import com.nags.operations.data.ApiException
import com.nags.operations.data.SharedCatalog
import com.nags.operations.data.TokenStore
import io.ktor.client.HttpClient
import io.ktor.client.request.get
import io.ktor.client.statement.bodyAsText
import io.ktor.http.isSuccess

/** Uses the current API server's canonical, permission-checked MasterData options routes. */
class SharedCatalogsApi(
    tokenStore: TokenStore,
    private val client: HttpClient = HttpClientFactory.create(tokenStore),
) {
    suspend fun catalog(kind: SharedCatalog): String {
        val response = client.get(HttpClientFactory.url(kind.endpointPath))
        val body = response.bodyAsText()
        if (!response.status.isSuccess()) throw ApiException(response.status.value, body)
        return body
    }
}
