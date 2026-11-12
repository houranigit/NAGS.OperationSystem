package com.nags.operations.data.repo

import com.nags.operations.data.*
import com.nags.operations.data.db.entities.FlightDeparturePendingEntity
import java.nio.file.Files
import java.util.Base64
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.async
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import org.junit.After
import org.junit.Assert.*
import org.junit.Test

class DepartureSignaturePreviewsTest {
    private val folder = Files.createTempDirectory("departure-previews").toFile()
    private var owner: String? = "api|account"
    private var online = true
    private var reads = 0
    private var pendingReads = 0
    private val png = Base64.getDecoder().decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aPsYAAAAASUVORK5CYII=")
    private val employee = AssignedEmployee("staff", "Employee", "E1")
    private val submission = FlightDepartureSubmission("departure", employee, "2026-11-09T13:00:00Z", employee,
        isAccepted = true, createdAtUtc = "2026-11-09T12:00:00Z", signatureVersion = "opaque-signature-v1")
    private val flight = FlightDetail("flight", "SV100", "customer", "SV", "Airline", "station", "AAA", "Station", "operation", "Transit",
        scheduledArrivalUtc = "2026-11-09T12:00:00Z", scheduledDepartureUtc = "2026-11-09T13:00:00Z",
        isPerLanding = false, createdAtUtc = "2026-11-09T12:00:00Z", rowVersion = "one", currentStaffMemberId = "staff",
        departureSubmissions = listOf(submission), stationTimeZoneId = "Etc/UTC")
    private var fetch: suspend (String, String, String) -> ByteArray? = { _, _, _ -> png }
    private var pendingBytes: ByteArray? = png
    private fun store() = DepartureSignaturePreviews(folder, { owner }, { online }, { flight, id, version ->
        reads++; fetch(flight, id, version)
    }, { _, _ -> pendingReads++; pendingBytes })

    @After fun tearDown() { folder.deleteRecursively() }

    @Test fun downloadedAndAcknowledgedImagesSurviveRestartAndSignatureReuseOffline() = runBlocking {
        assertArrayEquals(png, store().load(flight, null))
        online = false
        val corrected = flight.copy(departureSubmissions = listOf(submission.copy(updatedAtUtc = "2026-11-09T14:00:00Z")))
        assertArrayEquals(png, store().load(corrected, null))
        assertEquals(1, reads)
        val replaced = corrected.copy(departureSubmissions = listOf(submission.copy(signatureVersion = "v2")))
        assertNull(store().load(replaced, null))
        store().recordAcknowledged(flight.id, FlightDepartureSaved(replaced.departureSubmissions.single(), "two"), png)
        assertArrayEquals(png, store().load(replaced, null))
        assertEquals(1, reads)
    }

    @Test fun privatePendingSignatureTakesPrecedenceAndNeverReadsAnotherOwnersQueue() = runBlocking {
        val pending = FlightDeparturePendingEntity(owner!!, flight.id, flight.flightNumber, "staff", "change", "{}", "one", "[]", "now", signatureFile = "private.png")
        online = false
        assertArrayEquals(png, store().load(flight, pending))
        assertEquals(1, pendingReads)
        assertEquals(0, reads)
        owner = "api|other-account"
        assertNull(store().load(flight, pending))
        assertEquals(1, pendingReads)
    }

    @Test fun targetedPendingCorrectionLoadsTheExactOriginalSignatureInsteadOfOwnDeparture() = runBlocking {
        val original = submission.copy(id = "foreign-departure", submittedByEmployee = employee.copy(staffMemberId = "other"),
            signatureVersion = "foreign-signature")
        val detail = flight.copy(departureSubmissions = listOf(submission, original))
        val pending = FlightDeparturePendingEntity(owner!!, flight.id, flight.flightNumber, "staff", "change",
            Json.encodeToString(SaveFlightDepartureRequest(original.atd, targetSubmissionId = original.id)),
            "one", Json.encodeToString(listOf(original)), "now")
        val requested = mutableListOf<String>()
        fetch = { _, id, _ -> requested += id; png }
        assertArrayEquals(png, store().load(detail, pending))
        assertEquals(listOf(original.id), requested)
        online = false
        assertArrayEquals(png, store().load(detail.copy(departureSubmissions = listOf(submission)), pending))
        assertEquals(1, reads)
        assertEquals(0, pendingReads)
    }

    @Test fun ownerAndApiIsolationAndClearRejectAnInFlightOldResponse() = runBlocking {
        val preview = store()
        assertArrayEquals(png, preview.load(flight, null))
        online = false
        for (other in listOf("api|other-account", "other-api|account")) {
            owner = other
            assertNull(preview.load(flight, null))
        }
        owner = "api|account"
        preview.clear()
        online = true
        val started = CompletableDeferred<Unit>()
        val response = CompletableDeferred<ByteArray?>()
        fetch = { _, _, _ -> started.complete(Unit); response.await() }
        val late = async(start = CoroutineStart.UNDISPATCHED) { preview.load(flight, null) }
        started.await()
        preview.clear()
        response.complete(png)
        assertNull(late.await())
        assertFalse(folder.exists())
    }

    @Test fun changedOwnerDuringDownloadCannotPublishOrCacheTheResponse() = runBlocking {
        val started = CompletableDeferred<Unit>()
        val response = CompletableDeferred<ByteArray?>()
        fetch = { _, _, _ -> started.complete(Unit); response.await() }
        val preview = store()
        val late = async { preview.load(flight, null) }
        started.await()
        owner = "api|other-account"
        response.complete(png)
        assertNull(late.await())
        assertFalse(folder.walkTopDown().any { it.isFile })
    }

    @Test fun aCorruptCachedPreviewIsRepairedWhenOnline() = runBlocking {
        val preview = store()
        preview.load(flight, null)
        folder.walkTopDown().single { it.isFile }.writeBytes(byteArrayOf(1))
        assertArrayEquals(png, preview.load(flight, null))
        assertEquals(2, reads)
    }
}
