package com.nags.operations.data.api

import com.nags.operations.data.ApiException
import com.nags.operations.data.AssignedEmployee
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.FlightPage
import com.nags.operations.data.InviteEmployeesRequest
import com.nags.operations.data.TokenStore
import com.nags.operations.data.api.HttpClientFactory.bodyOrThrow
import io.ktor.client.HttpClient
import io.ktor.client.request.get
import io.ktor.client.request.headers
import io.ktor.client.request.parameter
import io.ktor.client.request.post
import io.ktor.client.request.setBody
import io.ktor.client.statement.bodyAsText
import io.ktor.http.ContentType
import io.ktor.http.contentType
import io.ktor.http.isSuccess

/** Scoped flight reads and add-only staff invitations shared with the web portal. */
class OperationsApi(
    tokenStore: TokenStore,
    private val client: HttpClient = HttpClientFactory.create(tokenStore),
) {
    suspend fun flights(page: Int = 1, pageSize: Int = 20, search: String? = null): FlightPage =
        client.get(HttpClientFactory.url("api/v1/operations/flights")) {
            parameter("page", page)
            parameter("pageSize", pageSize)
            search?.trim()?.takeIf(String::isNotEmpty)?.let { parameter("search", it) }
        }.bodyOrThrow()

    suspend fun flight(id: String): FlightDetail =
        client.get(HttpClientFactory.url("api/v1/operations/flights/$id")).bodyOrThrow()

    suspend fun invitationOptions(id: String): List<AssignedEmployee> =
        client.get(HttpClientFactory.url("api/v1/operations/flights/$id/invitation-options")).bodyOrThrow()

    suspend fun invite(id: String, staffMemberIds: List<String>, rowVersion: String) {
        val response = client.post(HttpClientFactory.url("api/v1/operations/flights/$id/invite")) {
            contentType(ContentType.Application.Json)
            headers { append("If-Match", "\"$rowVersion\"") }
            setBody(InviteEmployeesRequest(staffMemberIds))
        }
        if (!response.status.isSuccess()) {
            throw ApiException(response.status.value, response.bodyAsText())
        }
    }
}
