package com.nags.operations.data.api

import com.nags.operations.data.FlightDiscoverySource
import com.nags.operations.data.FlightPage
import com.nags.operations.data.api.HttpClientFactory.bodyOrThrow
import io.ktor.client.HttpClient
import io.ktor.client.request.get
import io.ktor.client.request.parameter

class DiscoveryFlightsApi(private val client: HttpClient) {
    suspend fun offlinePage(page: Int, size: Int): FlightPage = client.get(
        HttpClientFactory.url("api/v1/operations/flights/mobile/search/offline")) {
        parameter("page", page); parameter("pageSize", size)
    }.bodyOrThrow()

    suspend fun page(source: FlightDiscoverySource, query: String, page: Int, size: Int): FlightPage = client.get(
        HttpClientFactory.url("api/v1/operations/flights/mobile/${source.key}")) {
        parameter(if (source == FlightDiscoverySource.Search) "flightNumber" else "search", query)
        parameter("page", page); parameter("pageSize", size)
    }.bodyOrThrow()
}
