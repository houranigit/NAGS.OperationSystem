package com.nags.operations.data

/** Typed wrapper around any non-2xx response so [userMessage] can recover the body. */
class ApiException(val statusCode: Int, val body: String) :
    Exception("HTTP $statusCode: $body")
