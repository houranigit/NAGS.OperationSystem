package com.nags.operations.data.api

import com.nags.operations.data.ApiException
import io.ktor.client.HttpClient
import io.ktor.client.engine.HttpClientEngineBase
import io.ktor.client.engine.HttpClientEngineConfig
import io.ktor.client.engine.callContext
import io.ktor.client.request.HttpRequestData
import io.ktor.client.request.HttpResponseData
import io.ktor.http.Headers
import io.ktor.http.HttpMethod
import io.ktor.http.HttpHeaders
import io.ktor.http.HttpProtocolVersion
import io.ktor.http.HttpStatusCode
import io.ktor.util.date.GMTDate
import io.ktor.utils.io.ByteReadChannel
import io.ktor.utils.io.InternalAPI
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test

class OperationsLogoApiTest {
    private val customerId = "1e2fd2b4-a4fc-4bcd-9943-4fa0f9f6d80c"

    @Test
    fun logoUsesTheFixedApiRouteAndReturnsBytesAtTheSizeBoundary() = runBlocking {
        val bytes = ByteArray(2 * 1024 * 1024) { 7 }
        val engine = LogoEngine(HttpStatusCode.OK, bytes)
        HttpClient(engine).use { client ->
            val result = OperationsApi(client).customerLogo(customerId, "v1")!!
            assertArrayEquals(bytes, result.bytes)
            assertEquals("v1", result.version)
            assertEquals("v1", engine.request?.url?.parameters?.get("expectedVersion"))
            assertEquals(HttpMethod.Get, engine.request?.method)
            assertEquals(HttpClientFactory.url("api/v1/operations/flights/customer-logos/$customerId"), engine.request?.url.toString()?.substringBefore('?'))
        }
    }

    @Test
    fun absentLogosReturnNullAndOversizedOrDeniedResponsesAreRejected() = runBlocking {
        for (status in listOf(HttpStatusCode.NotFound, HttpStatusCode.OK)) {
            HttpClient(LogoEngine(status, byteArrayOf())).use { client ->
                assertNull(OperationsApi(client).customerLogo(customerId))
            }
        }
        HttpClient(LogoEngine(HttpStatusCode.OK, ByteArray(2 * 1024 * 1024 + 1))).use { client ->
            assertTrue(runCatching { OperationsApi(client).customerLogo(customerId) }.exceptionOrNull() is java.io.IOException)
        }
        HttpClient(LogoEngine(HttpStatusCode.Forbidden, byteArrayOf(1))).use { client ->
            val error = runCatching { OperationsApi(client).customerLogo(customerId) }.exceptionOrNull()
            assertTrue(error is ApiException && error.statusCode == 403)
        }
    }

    @Test
    fun onlyStrongMatchingResponseVersionsCanBePersisted() = runBlocking {
        for (etag in listOf<String?>(null, "W/\"v1\"", "\"v2\"")) {
            HttpClient(LogoEngine(HttpStatusCode.OK, byteArrayOf(1), etag)).use { client ->
                assertTrue(runCatching { OperationsApi(client).customerLogo(customerId, "v1") }.exceptionOrNull() is java.io.IOException)
            }
        }
        HttpClient(LogoEngine(HttpStatusCode.PreconditionFailed, byteArrayOf())).use { client ->
            val error = runCatching { OperationsApi(client).customerLogo(customerId, "v1") }.exceptionOrNull()
            assertTrue(error is ApiException && error.statusCode == 412)
        }
    }

    @Test
    fun customerIdentifiersCannotRedirectTheAuthenticatedRequest() = runBlocking {
        val engine = LogoEngine(HttpStatusCode.OK, byteArrayOf(1))
        HttpClient(engine).use { client ->
            assertTrue(runCatching { OperationsApi(client).customerLogo("https://other.example/logo") }.isFailure)
            assertNull(engine.request)
        }
    }
}

/** Supplies response bytes through the existing Ktor engine contract without another test dependency. */
@OptIn(InternalAPI::class)
private class LogoEngine(private val status: HttpStatusCode, private val bytes: ByteArray, private val etag: String? = "\"v1\"") : HttpClientEngineBase("logo-test") {
    override val config = HttpClientEngineConfig()
    var request: HttpRequestData? = null

    override suspend fun execute(data: HttpRequestData): HttpResponseData {
        request = data
        // Use Ktor's call context (its response Job is a CompletableJob), as the real mock engine does.
        return HttpResponseData(status, GMTDate(), Headers.build { etag?.let { append(HttpHeaders.ETag, it) } }, HttpProtocolVersion.HTTP_1_1,
            ByteReadChannel(bytes), callContext())
    }
}
