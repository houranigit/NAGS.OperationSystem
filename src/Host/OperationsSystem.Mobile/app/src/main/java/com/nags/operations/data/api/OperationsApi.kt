package com.nags.operations.data.api

import com.nags.operations.data.ApiException
import com.nags.operations.data.AssignedEmployee
import com.nags.operations.data.FlightCategory
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.FlightPage
import com.nags.operations.data.InviteEmployeesRequest
import com.nags.operations.data.TokenStore
import com.nags.operations.data.CustomerLogoDownload
import io.ktor.http.HttpHeaders
import com.nags.operations.data.api.HttpClientFactory.bodyOrThrow
import io.ktor.client.HttpClient
import io.ktor.client.request.get
import io.ktor.client.request.headers
import io.ktor.client.request.parameter
import io.ktor.client.request.post
import io.ktor.client.request.put
import com.nags.operations.data.FlightArrivalSaved
import com.nags.operations.data.FlightDepartureSaved
import com.nags.operations.data.SaveFlightDepartureRequest
import com.nags.operations.data.SaveFlightArrivalRequest
import io.ktor.client.request.prepareGet
import io.ktor.client.request.setBody
import io.ktor.client.statement.bodyAsText
import io.ktor.client.statement.bodyAsChannel
import io.ktor.http.HttpStatusCode
import io.ktor.utils.io.readBuffer
import kotlinx.io.readByteArray
import java.io.IOException
import java.util.UUID
import io.ktor.http.ContentType
import io.ktor.http.contentType
import io.ktor.http.isSuccess

/** Station- and time-bounded mobile flight reads and add-only staff invitations. */
class OperationsApi internal constructor(private val client: HttpClient) {
    constructor(tokenStore: TokenStore, client: HttpClient = HttpClientFactory.create(tokenStore)) : this(client)

    /** The authenticated API proxies raster logo bytes; no storage URL or bearer is exposed to UI. */
    suspend fun customerLogo(customerId: String, expectedVersion: String? = null): CustomerLogoDownload? {
        val id = UUID.fromString(customerId)
        return client.prepareGet(HttpClientFactory.url("api/v1/operations/flights/mobile/customer-logos/$id")) {
            expectedVersion?.let { parameter("expectedVersion", it) }
        }.execute { response ->
            if (response.status == HttpStatusCode.NotFound) return@execute null
            if (!response.status.isSuccess()) throw ApiException(response.status.value, "Could not load customer logo.")
            val etag = response.headers[HttpHeaders.ETag]
            val version = etag?.takeIf { it.startsWith('"') && it.endsWith('"') }?.removeSurrounding("\"")
                ?.takeIf { it.isNotBlank() && it != "none" }
                ?: throw IOException("Customer logo has no valid version.")
            if (expectedVersion != null && version != expectedVersion) throw IOException("Customer logo version changed.")
            val bytes = response.bodyAsChannel().readBuffer(MAX_LOGO_BYTES + 1L).readByteArray()
            if (bytes.size > MAX_LOGO_BYTES) throw IOException("Customer logo exceeds the size limit.")
            bytes.takeIf { it.isNotEmpty() }?.let { CustomerLogoDownload(version, it) }
        }
    }

    suspend fun flights(page: Int = 1, pageSize: Int = 20, search: String? = null, category: FlightCategory? = null): FlightPage =
        client.get(HttpClientFactory.url("api/v1/operations/flights/mobile")) {
            parameter("page", page)
            parameter("pageSize", pageSize)
            category?.let { parameter("category", it.name) }
            search?.trim()?.takeIf(String::isNotEmpty)?.let { parameter("search", it) }
        }.bodyOrThrow()

    suspend fun flight(id: String): FlightDetail =
        client.get(HttpClientFactory.url("api/v1/operations/flights/mobile/$id")).bodyOrThrow()

    suspend fun invitationOptions(id: String): List<AssignedEmployee> =
        client.get(HttpClientFactory.url("api/v1/operations/flights/mobile/$id/invitation-options")).bodyOrThrow()

    suspend fun invite(id: String, staffMemberIds: List<String>, rowVersion: String) {
        val response = client.post(HttpClientFactory.url("api/v1/operations/flights/mobile/$id/invite")) {
            contentType(ContentType.Application.Json)
            headers { append("If-Match", "\"$rowVersion\"") }
            setBody(InviteEmployeesRequest(staffMemberIds))
        }
        if (!response.status.isSuccess()) {
            throw ApiException(response.status.value, response.bodyAsText())
        }
    }

    suspend fun departureSignature(flightId: String, submissionId: String, expectedUpdatedAt: String): ByteArray? =
        client.prepareGet(HttpClientFactory.url("api/v1/operations/flights/mobile/${UUID.fromString(flightId)}/departure-signatures/${UUID.fromString(submissionId)}")) {
            parameter("expectedUpdatedAt", expectedUpdatedAt)
        }.execute { response ->
            if (response.status == HttpStatusCode.NotFound) return@execute null
            if (!response.status.isSuccess()) throw ApiException(response.status.value, "Could not load the customer signature.")
            val bytes = response.bodyAsChannel().readBuffer(MAX_LOGO_BYTES + 1L).readByteArray()
            if (bytes.size > MAX_LOGO_BYTES) throw IOException("Customer signature exceeds the size limit.")
            bytes.takeIf { it.isNotEmpty() }
        }

    suspend fun saveArrival(id: String, input: SaveFlightArrivalRequest, rowVersion: String): FlightArrivalSaved =
        client.put(HttpClientFactory.url("api/v1/operations/flights/${UUID.fromString(id)}/arrival")) {
            contentType(ContentType.Application.Json)
            headers { append("If-Match", "\"$rowVersion\"") }
            setBody(input)
        }.bodyOrThrow()

    suspend fun saveDeparture(id: String, input: SaveFlightDepartureRequest, rowVersion: String): FlightDepartureSaved =
        client.put(HttpClientFactory.url("api/v1/operations/flights/${UUID.fromString(id)}/departure")) {
            contentType(ContentType.Application.Json)
            headers { append("If-Match", "\"$rowVersion\"") }
            setBody(input)
        }.bodyOrThrow()

    private companion object {
        const val MAX_LOGO_BYTES = 2 * 1024 * 1024
    }
}
