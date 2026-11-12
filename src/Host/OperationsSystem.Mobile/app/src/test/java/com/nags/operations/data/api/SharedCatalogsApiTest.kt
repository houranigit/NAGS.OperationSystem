package com.nags.operations.data.api

import com.nags.operations.data.SharedCatalog
import com.nags.operations.data.StationStaffCatalogSnapshot
import io.ktor.client.HttpClient
import io.ktor.client.engine.HttpClientEngineBase
import io.ktor.client.engine.HttpClientEngineConfig
import io.ktor.client.engine.callContext
import io.ktor.client.request.HttpRequestData
import io.ktor.client.request.HttpResponseData
import io.ktor.http.Headers
import io.ktor.http.HttpProtocolVersion
import io.ktor.http.HttpStatusCode
import io.ktor.util.date.GMTDate
import io.ktor.utils.io.ByteReadChannel
import io.ktor.utils.io.InternalAPI
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.SerializationException
import kotlinx.serialization.json.Json
import org.junit.Assert.*
import org.junit.Test

class SharedCatalogsApiTest {
    private val staff = """[{"id":"staff","fullName":"Employee","employeeId":"E1","stationId":"station-a","stationCode":"AAA","manpowerTypeName":"Engineer"}]"""

    @Test fun staffSyncRequestsOnlyTheCurrentStationAndKeepsAuthoritativeScopeForTheCache() = runBlocking {
        val engine = StaffOptionsEngine(staff, "station-a")
        HttpClient(engine).use { client ->
            val api = SharedCatalogsApi(client)
            val snapshot = Json.decodeFromString<StationStaffCatalogSnapshot>(api.catalog(SharedCatalog.StationStaff))
            assertEquals("true", engine.requests.single().url.parameters["currentStationOnly"])
            assertEquals("/api/v1/masterdata/staff-members/options", engine.requests.single().url.encodedPath)
            assertEquals("station-a", snapshot.stationId)
            assertEquals("station-a", snapshot.items.single().stationId)
            api.catalog(SharedCatalog.Customers)
            assertNull(engine.requests.last().url.parameters["currentStationOnly"])
        }
    }

    @Test fun nonemptyStaffResponsesRequireMatchingAuthenticatedStationMetadata() = runBlocking {
        for (station in listOf<String?>(null, "other-station")) {
            HttpClient(StaffOptionsEngine(staff, station)).use { client ->
                assertTrue(runCatching { SharedCatalogsApi(client).catalog(SharedCatalog.StationStaff) }
                    .exceptionOrNull() is SerializationException)
            }
        }
    }

    @Test fun anAccountWithoutALinkedStationCachesAnEmptyScopedStaffList() = runBlocking {
        HttpClient(StaffOptionsEngine("[]", null)).use { client ->
            val snapshot = Json.decodeFromString<StationStaffCatalogSnapshot>(SharedCatalogsApi(client).catalog(SharedCatalog.StationStaff))
            assertNull(snapshot.stationId)
            assertTrue(snapshot.items.isEmpty())
        }
    }
}

@OptIn(InternalAPI::class)
private class StaffOptionsEngine(private val body: String, private val stationId: String?) : HttpClientEngineBase("staff-options-test") {
    override val config = HttpClientEngineConfig()
    val requests = mutableListOf<HttpRequestData>()
    override suspend fun execute(data: HttpRequestData): HttpResponseData {
        requests += data
        return HttpResponseData(HttpStatusCode.OK, GMTDate(), Headers.build {
            stationId?.let { append("X-Staff-Station-Id", it) }
        }, HttpProtocolVersion.HTTP_1_1, ByteReadChannel(body.toByteArray()), callContext())
    }
}
