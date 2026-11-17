package com.nags.operations.data.api

import com.nags.operations.data.*
import com.nags.operations.data.api.HttpClientFactory.bodyOrThrow
import io.ktor.client.HttpClient
import io.ktor.client.request.*
import io.ktor.http.contentType
import io.ktor.http.ContentType
import java.util.UUID

class SharedFlightsApi(private val client: HttpClient) {
    suspend fun page(direction: FlightShareDirection, search: String, page: Int, pageSize: Int): FlightSharePage =
        client.get(HttpClientFactory.url("api/v1/operations/flights/mobile/shares")) {
            parameter("direction", direction.key)
            parameter("search", search)
            parameter("page", page)
            parameter("pageSize", pageSize)
        }.bodyOrThrow()

    suspend fun publish(flightId: String, shareId: String, input: PublishFlightShareRequest): FlightSharePost =
        client.put(HttpClientFactory.url("api/v1/operations/flights/mobile/${UUID.fromString(flightId)}/shares/${UUID.fromString(shareId)}")) {
            contentType(ContentType.Application.Json)
            setBody(input)
        }.bodyOrThrow()
}
