package com.nags.operations.data.api

import com.nags.operations.data.ApiException
import com.nags.operations.data.SharedCatalog
import com.nags.operations.data.StaffCatalogItem
import com.nags.operations.data.StationStaffCatalogSnapshot
import com.nags.operations.data.TokenStore
import io.ktor.client.HttpClient
import io.ktor.client.request.get
import io.ktor.client.request.parameter
import io.ktor.client.statement.bodyAsText
import io.ktor.http.isSuccess
import kotlinx.serialization.SerializationException
import kotlinx.serialization.encodeToString

/** Uses the current API server's canonical, permission-checked MasterData options routes. */
class SharedCatalogsApi internal constructor(private val client: HttpClient) {
    constructor(tokenStore: TokenStore, client: HttpClient = HttpClientFactory.create(tokenStore)) : this(client)

    suspend fun catalog(kind: SharedCatalog): String {
        val response = client.get(HttpClientFactory.url(kind.endpointPath)) {
            if (kind == SharedCatalog.StationStaff) parameter("currentStationOnly", true)
        }
        val body = response.bodyAsText()
        if (!response.status.isSuccess()) throw ApiException(response.status.value, body)
        if (kind == SharedCatalog.StationStaff) {
            val stationId = response.headers["X-Staff-Station-Id"]?.trim()?.takeIf(String::isNotEmpty)
            val staff = HttpClientFactory.json.decodeFromString<List<StaffCatalogItem>>(body)
            if (staff.any { stationId == null || !it.stationId.equals(stationId, true) })
                throw SerializationException("Station staff response does not match its authenticated station scope.")
            return HttpClientFactory.json.encodeToString(StationStaffCatalogSnapshot("current-station", stationId, staff))
        }
        return body
    }
}
