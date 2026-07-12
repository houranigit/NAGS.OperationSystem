package com.nags.operations.data

import io.ktor.client.network.sockets.ConnectTimeoutException
import io.ktor.client.network.sockets.SocketTimeoutException
import io.ktor.client.plugins.HttpRequestTimeoutException
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.io.IOException
import java.net.UnknownHostException

private val problemJson = Json { ignoreUnknownKeys = true }

/**
 * Convert any throwable raised by the data layer into a single short sentence
 * we can show to the user. Hides JSON shapes, status codes, and stack traces.
 */
fun Throwable.userMessage(): String = when (this) {
    is ApiException -> userMessage()
    is UnknownHostException ->
        "You appear to be offline. Check your connection and try again."
    is HttpRequestTimeoutException,
    is ConnectTimeoutException,
    is SocketTimeoutException ->
        "The request took too long. Please try again."
    is IOException -> "Network error — please try again."
    else -> message?.takeIf { it.isNotBlank() && !looksLikeRawHttp(it) }
        ?: "Something went wrong. Please try again."
}

fun ApiException.userMessage(): String {
    val raw = body.trim()
    if (raw.isEmpty()) return defaultHttpMessage()
    if (!raw.startsWith("{")) return raw.ifBlank { defaultHttpMessage() }
    return try {
        val o = problemJson.parseToJsonElement(raw).jsonObject
        val detail = o["detail"]?.jsonPrimitive?.contentOrNull
        val title = o["title"]?.jsonPrimitive?.contentOrNull
        val errors = o["errors"]?.jsonObject
        val firstErrors = errors?.values
            ?.firstOrNull()
            ?.jsonArray
            ?.joinToString("\n") { it.jsonPrimitive.contentOrNull.orEmpty() }
            ?.takeIf { it.isNotBlank() }
        when {
            !detail.isNullOrBlank() -> detail
            !firstErrors.isNullOrBlank() -> firstErrors
            !title.isNullOrBlank() && !title.equals("Bad Request", ignoreCase = true) -> title
            else -> defaultHttpMessage()
        }
    } catch (_: Exception) {
        defaultHttpMessage()
    }
}

private fun ApiException.defaultHttpMessage(): String =
    when (statusCode) {
        400 -> "The request could not be processed. Please review and try again."
        401 -> "Incorrect email or password."
        403 -> "You do not have permission to perform this action."
        404 -> "We couldn't find what you're looking for."
        in 500..599 -> "The server is having trouble. Please try again in a moment."
        else -> "Something went wrong (HTTP $statusCode)."
    }

private fun looksLikeRawHttp(message: String): Boolean =
    message.startsWith("HTTP ", ignoreCase = true) &&
        message.contains(": ") &&
        (message.contains("{") || message.contains("\""))
