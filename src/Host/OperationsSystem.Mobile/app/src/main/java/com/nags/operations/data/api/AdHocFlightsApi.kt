package com.nags.operations.data.api

import com.nags.operations.data.AdHocFlightContext
import com.nags.operations.data.AdHocFlightInput
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.api.HttpClientFactory.bodyOrThrow
import io.ktor.client.HttpClient
import io.ktor.client.request.get
import io.ktor.client.request.headers
import io.ktor.client.request.put
import io.ktor.client.request.setBody
import io.ktor.http.ContentType
import io.ktor.http.contentType
import java.util.UUID

class AdHocFlightsApi(private val client: HttpClient) {
    suspend fun context(): AdHocFlightContext = client.get(HttpClientFactory.url("api/v1/operations/flights/mobile/adhoc/context")).bodyOrThrow()
    suspend fun create(id: String, input: AdHocFlightInput): FlightDetail = client.put(
        HttpClientFactory.url("api/v1/operations/flights/mobile/adhoc/${UUID.fromString(id)}")) {
        contentType(ContentType.Application.Json); setBody(input)
    }.bodyOrThrow()
    suspend fun update(id: String, input: AdHocFlightInput, version: String): FlightDetail = client.put(
        HttpClientFactory.url("api/v1/operations/flights/mobile/adhoc/${UUID.fromString(id)}/details")) {
        contentType(ContentType.Application.Json)
        headers { append("If-Match", "\"$version\"") }
        setBody(input)
    }.bodyOrThrow()
}
