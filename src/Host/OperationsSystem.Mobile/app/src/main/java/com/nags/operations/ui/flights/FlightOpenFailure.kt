package com.nags.operations.ui.flights

import com.nags.operations.data.ApiException
import io.ktor.client.plugins.HttpRequestTimeoutException
import java.io.IOException

/** A failed notification link can be permanent even when refreshing the flight list succeeds. */
enum class FlightOpenFailure(val canRetry: Boolean) {
    Unavailable(false),
    Session(true),
    Connection(true),
    Temporary(true),
}

internal fun flightOpenFailure(error: Throwable): FlightOpenFailure = when (error) {
    is ApiException -> when (error.statusCode) {
        401 -> FlightOpenFailure.Session
        408, 429 -> FlightOpenFailure.Temporary
        in 400..499 -> FlightOpenFailure.Unavailable
        else -> FlightOpenFailure.Temporary
    }
    is IOException, is HttpRequestTimeoutException -> FlightOpenFailure.Connection
    else -> FlightOpenFailure.Temporary
}
