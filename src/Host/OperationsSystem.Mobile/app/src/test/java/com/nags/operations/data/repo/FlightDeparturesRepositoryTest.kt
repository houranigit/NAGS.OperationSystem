package com.nags.operations.data.repo

import com.nags.operations.data.*
import com.nags.operations.data.db.dao.FlightDepartureDao
import com.nags.operations.data.db.entities.FlightDeparturePendingEntity
import java.io.IOException
import java.time.Instant
import java.util.Base64
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.async
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.Json
import org.junit.Assert.*
import org.junit.Test

class FlightDeparturesRepositoryTest {
    private val owner = MutableStateFlow<String?>("api|account")
    private val dao = DepartureMemoryDao()
    private val files = MemoryDepartureSignatures()
    private var online = false
    private var scheduled = 0
    private var savedArrival = false
    private var savedServices = false
    private var arrivalVersion: String? = null
    private var acknowledgedArrival: suspend (String, String) -> String = { _, reviewed ->
        if (reviewed == "AAAAAAAAAAE=") arrivalVersion ?: reviewed else reviewed
    }
    private var failure: Exception? = null
    private var acceptedFailure = false
    private var fetches = 0
    private var previewAcknowledged: suspend (String, FlightDepartureSaved, ByteArray) -> Unit = { _, _, _ -> }
    private val staff = "11111111-1111-1111-1111-111111111111"
    private val at = Instant.parse("2026-11-09T12:00:00Z")
    private val employee = AssignedEmployee(staff, "Employee", "E1")
    private val png = Base64.getDecoder().decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aPsYAAAAASUVORK5CYII=")
    private val input = SaveFlightDepartureRequest(at.plusSeconds(3600).toString(), Base64.getEncoder().encodeToString(png), at.toString(), " note ")
    private val submitted = FlightDepartureSubmission("departure", employee, input.atd, employee,
        remarks = "note", isAccepted = true, createdAtUtc = at.toString())
    private val flight = FlightDetail("44444444-4444-4444-4444-444444444444", "SV100", "customer", "SV", "Airline", "station", "AAA",
        "Station", "operation", "Transit", scheduledArrivalUtc = at.toString(), scheduledDepartureUtc = at.plusSeconds(3600).toString(),
        isPerLanding = false, createdAtUtc = at.toString(), rowVersion = "AAAAAAAAAAE=", stationTimeZoneId = "Asia/Riyadh",
        currentStaffMemberId = staff, canSaveArrival = true, canSaveDeparture = true)
    private var live = flight
    private val sent = mutableListOf<Pair<SaveFlightDepartureRequest, String>>()
    private var send: suspend (String, SaveFlightDepartureRequest, String) -> FlightDepartureSaved = { _, value, version ->
        sent += value to version
        failure?.let { throw it }
        FlightDepartureSaved(submitted.copy(atd = value.atd, remarks = value.remarks), "AAAAAAAAAAI=")
    }
    private fun repository() = FlightDeparturesRepository(dao, files, owner, { owner.value }, { online },
        send = { id, value, version -> send(id, value, version) }, accepted = { _, _ -> if (acceptedFailure) throw IOException("disk unavailable") },
        schedule = { scheduled++ }, json = Json, hasSavedArrival = { savedArrival }, hasSavedServices = { savedServices },
        acknowledgedArrivalVersion = { id, reviewed -> acknowledgedArrival(id, reviewed) },
        fetchFlight = { fetches++; live }, now = { at },
        onSignatureAcknowledged = { id, saved, bytes -> previewAcknowledged(id, saved, bytes) })

    @Test fun unsignedDepartureSurvivesRestartAndSyncWithoutCreatingSignatureFiles() = runBlocking {
        val unsigned = input.copy(signatureContent = null, signatureSignedAtUtc = null)
        val withArrival = flight.copy(arrivalSubmissions = listOf(FlightArrivalSubmission(
            "55555555-5555-5555-5555-555555555555", employee, at.toString(),
            "33333333-3333-3333-3333-333333333333", "A320", "HZ-TEST", receivedByEmployee = employee,
            isAccepted = true, createdAtUtc = at.toString())))
        repository().save(withArrival, unsigned)
        assertNull(dao.rows(owner.value!!).single().signatureFile)
        assertTrue(files.files.isEmpty())
        send = { _, value, version ->
            sent += value to version
            assertNull(value.signatureContent)
            assertNull(value.signatureSignedAtUtc)
            FlightDepartureSaved(submitted.copy(hasSignature = false), "AAAAAAAAAAI=")
        }
        online = true
        assertFalse(repository().syncPending())
        assertEquals(1, sent.size)
        assertTrue(dao.rows(owner.value!!).isEmpty())
        assertTrue(files.files.isEmpty())
    }

    @Test fun targetedAssignedCorrectionReusesOnlyTheRecordedTargetsSignature() = runBlocking {
        val author = employee.copy(staffMemberId = "22222222-2222-2222-2222-222222222222", fullName = "Original author")
        val original = submitted.copy(id = "66666666-6666-6666-6666-666666666666", submittedByEmployee = author,
            handledByEmployee = author, signatureVersion = "original-signature")
        val assigned = flight.copy(departureSubmissions = listOf(original), assignedEmployees = listOf(employee))
        val correction = input.copy(signatureContent = null, signatureSignedAtUtc = null, targetSubmissionId = original.id)
        assertFalse(assigned.departureEditable())
        assertTrue(assigned.canUpdateDepartureSubmission(original.id))
        assertTrue(runCatching { repository().save(assigned.copy(assignedEmployees = emptyList()), correction) }.isFailure)
        assertTrue(runCatching { repository().save(assigned, correction.copy(targetSubmissionId = null)) }.isFailure)
        repository().save(assigned, correction)
        val queued = dao.rows(owner.value!!).single()
        assertEquals(listOf(original), Json.decodeFromString<List<FlightDepartureSubmission>>(queued.reviewedDepartureJson))
        assertNull(queued.signatureFile)
        send = { _, value, version ->
            sent += value to version
            FlightDepartureSaved(original.copy(remarks = value.remarks), "AAAAAAAAAAI=")
        }
        online = true
        assertFalse(repository().syncPending())
        assertEquals(listOf(correction.normalized() to flight.rowVersion), sent)
        assertTrue(files.files.isEmpty())
    }

    @Test fun targetedSignatureReplacementRetainsExactRetryAndRejectsTargetSwitch() = runBlocking {
        val author = employee.copy(staffMemberId = "22222222-2222-2222-2222-222222222222")
        val original = submitted.copy(id = "66666666-6666-6666-6666-666666666666", submittedByEmployee = author)
        val other = original.copy(id = "77777777-7777-7777-7777-777777777777")
        val assigned = flight.copy(departureSubmissions = listOf(original, other), assignedEmployees = listOf(employee))
        val correction = input.copy(targetSubmissionId = original.id)
        val repo = repository()
        repo.save(assigned, correction)
        online = true; failure = IOException("lost response")
        assertTrue(repo.syncPending())
        val frozen = dao.rows(owner.value!!).single()
        assertTrue(runCatching { repo.save(assigned, correction.copy(targetSubmissionId = other.id)) }.isFailure)
        repo.save(assigned, correction.copy(signatureContent = null, signatureSignedAtUtc = null, remarks = "later edit"))
        assertEquals(frozen.attemptInputJson, dao.rows(owner.value!!).single().attemptInputJson)
        assertEquals(frozen.attemptSignatureFile, dao.rows(owner.value!!).single().attemptSignatureFile)
        failure = null
        send = { _, value, version -> sent += value to version; FlightDepartureSaved(original, "AAAAAAAAAAI=") }
        val restarted = repository()
        assertTrue(restarted.syncPending())
        assertFalse(restarted.syncPending())
        assertEquals(sent[0], sent[1])
        assertEquals(original.id, sent.last().first.targetSubmissionId)
        assertNull(sent.last().first.signatureContent)
        assertNull(sent.last().first.signatureSignedAtUtc)
        assertTrue(files.files.isEmpty())
    }

    @Test fun targetedComparisonRejectsLostAssignmentBeforeUpload() = runBlocking {
        val original = submitted.copy(id = "66666666-6666-6666-6666-666666666666",
            submittedByEmployee = employee.copy(staffMemberId = "22222222-2222-2222-2222-222222222222"))
        val assigned = flight.copy(departureSubmissions = listOf(original), assignedEmployees = listOf(employee))
        val repo = repository()
        repo.save(assigned, input.copy(signatureContent = null, signatureSignedAtUtc = null, targetSubmissionId = original.id))
        repo.onPriorWriteAcknowledged(flight.id, flight.rowVersion, "AAAAAAAAAAI=")
        live = assigned.copy(assignedEmployees = emptyList())
        online = true
        assertFalse(repo.syncPending())
        assertEquals("conflict", dao.rows(owner.value!!).single().status)
        assertTrue(sent.isEmpty())
    }

    @Test fun laterDepartureRelaxesBoundsBeforeServicesAndEarlierDepartureWaitsForCorrections() = runBlocking {
        val repo = repository()
        val existing = flight.copy(departureSubmissions = listOf(submitted))
        repo.save(existing, input.copy(atd = at.plusSeconds(7200).toString(), signatureContent = null, signatureSignedAtUtc = null))
        savedServices = true
        online = true
        assertTrue(repo.mustSyncBeforeServices(flight.id))
        assertFalse(repo.syncPending(priorToServices = true))
        assertEquals(1, sent.size)
        val restarted = repository()
        restarted.save(existing, input.copy(atd = at.plusSeconds(1800).toString(), signatureContent = null, signatureSignedAtUtc = null))
        assertFalse(restarted.mustSyncBeforeServices(flight.id))
        assertFalse(restarted.syncPending(priorToServices = true))
        assertFalse(restarted.syncPending())
        assertEquals(1, sent.size)
        savedServices = false
        assertFalse(restarted.syncPending())
        assertEquals(2, sent.size)
    }

    @Test fun frozenDepartureReplaysBeforeFreshArrivalAndServiceIntent() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        online = true
        failure = IOException("Unknown server result")
        assertTrue(repo.syncPending())
        assertTrue(repo.hasUnacknowledgedAttempt(flight.id))
        savedArrival = true
        savedServices = true
        failure = null
        assertFalse(repo.syncPending(priorToServices = true))
        assertEquals(2, sent.size)
        assertEquals(sent[0], sent[1])
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun acknowledgedPngIsCachedBeforeQueueCleanupAndPreviewFailureCannotUndoTheSave() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        val pending = dao.rows(owner.value!!).single()
        assertArrayEquals(png, repo.signaturePreview(flight.id, pending.changeId))
        var previews = 0
        previewAcknowledged = { id, saved, bytes ->
            previews++
            assertEquals(flight.id, id)
            assertEquals(submitted.id, saved.submission.id)
            assertArrayEquals(png, bytes)
            assertTrue(files.files.isNotEmpty())
            throw IOException("preview cache unavailable")
        }
        online = true
        assertFalse(repo.syncPending())
        assertEquals(1, previews)
        assertTrue(dao.rows(owner.value!!).isEmpty())
        assertTrue(files.files.isEmpty())
    }

    @Test fun liveComparisonIgnoresOnlyMissingLegacySignatureMetadata() = runBlocking {
        val cases = listOf(
            Triple(submitted, submitted.copy(signatureVersion = "new-metadata"), false),
            Triple(submitted, submitted.copy(signatureVersion = "new-metadata", remarks = "changed"), true),
            Triple(submitted.copy(signatureVersion = "v1"), submitted.copy(signatureVersion = "v2"), true),
            Triple(submitted.copy(signatureVersion = "v1"), submitted.copy(signatureVersion = "v1"), false),
        )
        for ((reviewed, latest, conflict) in cases) {
            val repo = repository()
            repo.clear()
            online = false
            sent.clear()
            val reviewedFlight = flight.copy(departureSubmissions = listOf(reviewed))
            repo.save(reviewedFlight, input.copy(signatureContent = null, signatureSignedAtUtc = null))
            repo.onArrivalAcknowledged(flight.id, flight.rowVersion, "AAAAAAAAAAI=")
            live = flight.copy(departureSubmissions = listOf(latest))
            online = true
            assertFalse(repo.syncPending())
            if (conflict) {
                assertEquals("conflict", dao.rows(owner.value!!).single().status)
                assertTrue(sent.isEmpty())
            } else {
                assertTrue(dao.rows(owner.value!!).isEmpty())
                assertEquals(1, sent.size)
            }
        }
    }

    @Test fun offlineSignatureAndInputSurviveRepositoryRecreationAndLeaveTheQueueOnlyAfterAcknowledgment() = runBlocking {
        repository().save(flight, input)
        val row = dao.rows(owner.value!!).single()
        assertNull(Json.decodeFromString<SaveFlightDepartureRequest>(row.inputJson).signatureContent)
        assertArrayEquals(png, files.read(row.signatureFile!!))
        assertEquals(1, scheduled)
        assertTrue(sent.isEmpty())
        val restarted = repository()
        assertEquals(row, restarted.changes.first().single())
        online = true
        assertFalse(restarted.syncPending())
        assertEquals(input.normalized(), sent.single().first)
        assertEquals(flight.rowVersion, sent.single().second)
        assertTrue(dao.rows(owner.value!!).isEmpty())
        assertTrue(files.files.isEmpty())
    }

    @Test fun repeatedOfflineEditsReuseTheDurableSignatureAndUploadOnlyLatestInput() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        val original = dao.rows(owner.value!!).single()
        repo.save(flight, input.copy(signatureContent = null, signatureSignedAtUtc = null, remarks = " latest "))
        val latest = dao.rows(owner.value!!).single()
        assertEquals(original.changeId, latest.changeId)
        assertEquals(original.signatureFile, latest.signatureFile)
        assertEquals(input.signatureSignedAtUtc, Json.decodeFromString<SaveFlightDepartureRequest>(latest.inputJson).signatureSignedAtUtc)
        assertEquals(1, files.files.size)
        online = true
        assertFalse(repo.syncPending())
        assertEquals(input.normalized().copy(remarks = "latest"), sent.single().first)
    }

    @Test fun lostResponseKeepsTheExactAttemptAndOriginalSignatureUntilReplayAcknowledgesIt() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        online = true
        failure = IOException("response lost after commit")
        assertTrue(repo.syncPending())
        val attempted = dao.rows(owner.value!!).single()
        val signedAgain = at.plusSeconds(10).toString()
        repo.save(flight, input.copy(signatureSignedAtUtc = signedAgain, remarks = "newer"))
        val edited = dao.rows(owner.value!!).single()
        assertEquals(attempted.attemptInputJson, edited.attemptInputJson)
        assertEquals(attempted.attemptRowVersion, edited.attemptRowVersion)
        assertEquals(attempted.attemptSignatureFile, edited.attemptSignatureFile)
        assertNotEquals(edited.signatureFile, edited.attemptSignatureFile)
        assertEquals(2, files.files.size)
        failure = null
        sent.clear()
        val restarted = repository()
        assertTrue(restarted.syncPending())
        assertEquals(input.normalized(), sent.single().first)
        val correction = dao.rows(owner.value!!).single()
        assertNull(correction.attemptInputJson)
        assertEquals("AAAAAAAAAAI=", correction.rowVersion)
        assertEquals(1, files.files.size)
        assertFalse(restarted.syncPending())
        assertEquals(signedAgain, sent.last().first.signatureSignedAtUtc)
        assertEquals("newer", sent.last().first.remarks)
        assertEquals("AAAAAAAAAAI=", sent.last().second)
        assertTrue(files.files.isEmpty())
    }

    @Test fun anEditDuringUploadIsDurableWithoutWaitingAndReusesAcknowledgedSignatureForItsCorrection() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        online = true
        val started = CompletableDeferred<Unit>()
        val finish = CompletableDeferred<Unit>()
        send = { _, value, version ->
            sent += value to version
            if (sent.size == 1) { started.complete(Unit); finish.await() }
            FlightDepartureSaved(submitted.copy(remarks = value.remarks), "AAAAAAAAAAI=")
        }
        val upload = async(start = CoroutineStart.UNDISPATCHED) { repo.syncPending() }
        started.await()
        val save = async(start = CoroutineStart.UNDISPATCHED) {
            repo.save(flight, input.copy(signatureContent = null, signatureSignedAtUtc = null, remarks = "correction"))
        }
        assertTrue(save.isCompleted)
        finish.complete(Unit)
        assertTrue(upload.await())
        val row = dao.rows(owner.value!!).single()
        assertNull(row.signatureFile)
        assertNull(Json.decodeFromString<SaveFlightDepartureRequest>(row.inputJson).signatureSignedAtUtc)
        assertTrue(files.files.isEmpty())
        assertFalse(repo.syncPending())
        assertNull(sent.last().first.signatureContent)
        assertEquals("correction", sent.last().first.remarks)
        assertEquals("AAAAAAAAAAI=", sent.last().second)
    }

    @Test fun acknowledgedArrivalAdvancesDesiredVersionAcrossRestartWithoutChangingAnExistingAttempt() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        repo.onArrivalAcknowledged(flight.id, flight.rowVersion, "AAAAAAAAAAI=")
        var row = dao.rows(owner.value!!).single()
        assertEquals("AAAAAAAAAAI=", row.rowVersion)
        assertTrue(row.needsDepartureComparison)
        live = flight.copy(rowVersion = "AAAAAAAAAAk=")
        online = true
        failure = IOException("lost departure response")
        val restarted = repository()
        assertTrue(restarted.syncPending())
        assertEquals("AAAAAAAAAAI=", sent.single().second) // Never adopt a GET's newer version.
        assertEquals(1, fetches)
        restarted.onArrivalAcknowledged(flight.id, "AAAAAAAAAAI=", "AAAAAAAAAAM=")
        row = dao.rows(owner.value!!).single()
        assertEquals("AAAAAAAAAAM=", row.rowVersion)
        assertEquals("AAAAAAAAAAI=", row.attemptRowVersion)
        live = live.copy(departureSubmissions = listOf(submitted)) // May be our already committed attempt.
        failure = null
        assertFalse(restarted.syncPending())
        assertEquals(1, fetches) // Replay must reach server idempotency rather than conflict with itself.
        assertEquals("AAAAAAAAAAI=", sent.last().second)
    }

    @Test fun arrivalPendingOrConflictBlocksOnlyDependentUploadUntilResolved() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        online = true
        savedArrival = true
        assertFalse(repo.syncPending())
        assertTrue(sent.isEmpty())
        assertEquals("pending", dao.rows(owner.value!!).single().status)
        savedArrival = false
        assertFalse(repo.syncPending())
        assertEquals(1, sent.size)
    }

    @Test fun anEarlierArrivalWorkerAcknowledgmentIsCapturedWhenDepartureIsSaved() = runBlocking {
        arrivalVersion = "AAAAAAAAAAI="
        val repo = repository()
        repo.save(flight, input)
        val row = dao.rows(owner.value!!).single()
        assertEquals("AAAAAAAAAAI=", row.rowVersion)
        assertTrue(row.needsDepartureComparison)
        online = true
        assertFalse(repo.syncPending())
        assertEquals(1, fetches)
        assertEquals("AAAAAAAAAAI=", sent.single().second)
    }

    @Test fun arrivalAcknowledgmentBetweenInitialVersionReadAndDeparturePublicationIsPersisted() = runBlocking {
        var calls = 0
        acknowledgedArrival = { _, reviewed -> if (++calls == 1) reviewed else "AAAAAAAAAAI=" }
        val repo = repository()
        repo.save(flight, input)
        val row = dao.rows(owner.value!!).single()
        assertEquals("AAAAAAAAAAI=", row.rowVersion)
        assertTrue(row.needsDepartureComparison)
        online = true
        assertFalse(repository().syncPending())
        assertEquals("AAAAAAAAAAI=", sent.single().second)
    }

    @Test fun interveningDepartureOrCancellationAfterAnArrivalAcknowledgmentRequiresReview() = runBlocking {
        for (current in listOf(flight.copy(departureSubmissions = listOf(submitted)), flight.copy(hasCancellationSubmission = true))) {
            val repo = repository()
            repo.clear()
            repo.save(flight, input)
            repo.onArrivalAcknowledged(flight.id, flight.rowVersion, "AAAAAAAAAAI=")
            live = current
            online = true
            assertFalse(repo.syncPending())
            val row = dao.rows(owner.value!!).single()
            assertEquals("conflict", row.status)
            assertNull(row.attemptInputJson)
            assertNotNull(row.signatureFile)
        }
        assertTrue(sent.isEmpty())
    }

    @Test fun cachedComparisonIsTransientAndRetriesWhenTheLiveConnectionReturns() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        repo.onArrivalAcknowledged(flight.id, flight.rowVersion, "AAAAAAAAAAI=")
        live = flight.copy(fromCache = true)
        online = true
        assertTrue(repo.syncPending())
        assertEquals("pending", dao.rows(owner.value!!).single().status)
        assertTrue(sent.isEmpty())
        live = flight
        assertFalse(repo.syncPending())
        assertEquals(1, sent.size)
    }

    @Test fun ownDepartureAcknowledgmentBeforeAReopenedEditIsSavedAdvancesOnlyItsKnownVersion() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        online = true
        assertFalse(repo.syncPending())
        live = flight.copy(departureSubmissions = listOf(submitted), rowVersion = "AAAAAAAAAAk=")
        repo.save(flight, input.copy(signatureContent = null, remarks = "late edit"))
        assertFalse(repo.syncPending())
        assertEquals("AAAAAAAAAAI=", sent.last().second)
        assertNull(sent.last().first.signatureContent)
        assertNull(sent.last().first.signatureSignedAtUtc)
        assertEquals("late edit", sent.last().first.remarks)
    }

    @Test fun workerAcknowledgmentBetweenTheTwoSaveLocksPreservesSignatureReuseAndCorrectVersion() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        online = true
        var workerWon = false
        acknowledgedArrival = { _, version ->
            if (!workerWon) { workerWon = true; assertFalse(repo.syncPending()) }
            version
        }
        live = flight.copy(departureSubmissions = listOf(submitted))
        repo.save(flight, input.copy(signatureContent = null, signatureSignedAtUtc = null, remarks = "edited while ACK completed"))
        val correction = dao.rows(owner.value!!).single()
        assertEquals("AAAAAAAAAAI=", correction.rowVersion)
        assertNull(correction.signatureFile)
        assertTrue(correction.needsDepartureComparison)
        assertFalse(repo.syncPending())
        assertEquals("AAAAAAAAAAI=", sent.last().second)
        assertNull(sent.last().first.signatureContent)
        assertEquals("edited while ACK completed", sent.last().first.remarks)
    }

    @Test fun arrivalThenDepartureAcknowledgmentsCarrySignatureProofAcrossAReopenedEdit() = runBlocking {
        arrivalVersion = "AAAAAAAAAAI="
        val repo = repository()
        repo.save(flight, input)
        online = true
        send = { _, value, version ->
            sent += value to version
            FlightDepartureSaved(submitted.copy(remarks = value.remarks), "AAAAAAAAAAM=")
        }
        assertFalse(repo.syncPending())
        live = flight.copy(departureSubmissions = listOf(submitted))
        repo.save(flight, input.copy(signatureContent = null, signatureSignedAtUtc = null, remarks = "reopened correction"))
        val correction = dao.rows(owner.value!!).single()
        assertEquals("AAAAAAAAAAM=", correction.rowVersion)
        assertNull(correction.signatureFile)
        assertFalse(repo.syncPending())
        assertEquals("AAAAAAAAAAM=", sent.last().second)
        assertNull(sent.last().first.signatureContent)
    }

    @Test fun missingSignatureFilesBecomeReviewableFailureRatherThanEndlessRetry() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        files.files.clear()
        online = true
        assertFalse(repo.syncPending())
        assertEquals("failed", dao.rows(owner.value!!).single().status)
        assertTrue(sent.isEmpty())
    }

    @Test fun unknownOutcomesKeepFrozenProofWhileDefiniteRejectionsKeepOnlyDesiredSignatureAndInput() = runBlocking {
        for ((error, status) in listOf(IOException("offline") to "pending", ApiException(401, "signed out") to "pending",
            ApiException(408, "timeout") to "pending", ApiException(429, "retry later") to "pending", ApiException(500, "server failure") to "pending",
            ApiException(409, "stale") to "conflict", ApiException(403, "not permitted") to "failed")) {
            val repo = repository()
            repo.clear()
            repo.save(flight, input)
            online = true
            failure = error
            repo.syncPending()
            val row = dao.rows(owner.value!!).single()
            assertEquals(status, row.status)
            assertNotNull(row.signatureFile)
            if (status == "pending") {
                assertNotNull(row.attemptInputJson)
                assertNotNull(row.attemptSignatureFile)
            } else {
                assertNull(row.attemptInputJson)
                assertNull(row.attemptRowVersion)
                assertNull(row.attemptSignatureFile)
            }
            assertEquals(input.normalized(), Json.decodeFromString<SaveFlightDepartureRequest>(row.inputJson).copy(signatureContent = input.signatureContent))
            assertEquals(1, files.files.size)
        }
    }

    @Test fun cancellationAndCacheFailureAfterAcknowledgmentKeepTheAttemptForIdempotentReplay() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        online = true
        failure = CancellationException("process stopped")
        assertTrue(runCatching { repo.syncPending() }.exceptionOrNull() is CancellationException)
        val original = dao.rows(owner.value!!).single()
        failure = null
        acceptedFailure = true
        assertTrue(repo.syncPending())
        assertEquals(original.attemptInputJson, dao.rows(owner.value!!).single().attemptInputJson)
        acceptedFailure = false
        assertFalse(repository().syncPending())
        assertTrue(files.files.isEmpty())
    }

    @Test fun discardAndLateNetworkFailureNeverResurrectInputOrRetainItsUnreferencedFile() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        online = true
        val started = CompletableDeferred<Unit>()
        val finish = CompletableDeferred<Unit>()
        send = { _, _, _ -> started.complete(Unit); finish.await(); throw IOException("late failure") }
        val upload = async(start = CoroutineStart.UNDISPATCHED) { repo.syncPending() }
        started.await()
        repo.discard(flight.id)
        assertTrue(files.files.isEmpty())
        finish.complete(Unit)
        upload.await()
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun pauseWaitsForTheAuthenticatedUploadAndRejectsNewSavesUntilResumed() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        online = true
        val started = CompletableDeferred<Unit>()
        val finish = CompletableDeferred<Unit>()
        send = { _, _, _ -> started.complete(Unit); finish.await(); FlightDepartureSaved(submitted, "AAAAAAAAAAI=") }
        val upload = async(start = CoroutineStart.UNDISPATCHED) { repo.syncPending() }
        started.await()
        val pause = async(start = CoroutineStart.UNDISPATCHED) { repo.pause() }
        assertFalse(pause.isCompleted)
        assertTrue(runCatching { repo.save(flight, input) }.isFailure)
        finish.complete(Unit)
        upload.await(); pause.await()
        assertTrue(runCatching { repo.save(flight, input) }.isFailure)
        repo.resume()
        repo.save(flight.copy(rowVersion = "AAAAAAAAAAI=", departureSubmissions = listOf(submitted)), input)
    }

    @Test fun accountScopedObservationAndCleanupNeverDeleteAnotherOwnersSignature() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        owner.value = "another-api|account"
        assertTrue(repo.changes.first().isEmpty())
        repo.save(flight, input)
        assertEquals(2, files.files.size)
        repo.discard(flight.id)
        assertEquals(1, files.files.size)
        owner.value = "api|account"
        assertEquals(1, repo.changes.first().size)
        files.files["orphan"] = png
        repo.resume()
        assertEquals(1, files.files.size)
        repo.clear()
        assertTrue(files.files.isEmpty())
    }

    @Test fun maximumSizedSignatureStaysOutsideTheDesiredAndAttemptedSqliteRows() = runBlocking {
        // Add a legal ancillary PNG text chunk before IEND to exercise the full accepted byte limit.
        val data = ByteArray(2_097_152 - png.size - 12) { 65 }
        "Comment".toByteArray().copyInto(data)
        data[7] = 0
        val chunk = java.io.ByteArrayOutputStream().also { bytes ->
            java.io.DataOutputStream(bytes).use { output ->
                output.writeInt(data.size)
                val kind = "tEXt".toByteArray()
                output.write(kind); output.write(data)
                val crc = java.util.zip.CRC32().apply { update(kind); update(data) }
                output.writeInt(crc.value.toInt())
            }
        }.toByteArray()
        val large = png.copyOfRange(0, png.size - 12) + chunk + png.copyOfRange(png.size - 12, png.size)
        assertEquals(2_097_152, large.size)
        val repo = repository()
        repo.save(flight, input.copy(signatureContent = Base64.getEncoder().encodeToString(large)))
        online = true
        failure = IOException("lost response")
        assertTrue(repo.syncPending())
        val row = dao.rows(owner.value!!).single()
        assertTrue(row.inputJson.length < 1000)
        assertTrue(row.attemptInputJson!!.length < 1000)
        assertEquals(row.signatureFile, row.attemptSignatureFile)
        assertArrayEquals(large, files.read(row.signatureFile!!))
    }

    @Test fun orphanCleanupFailureDoesNotTurnSuccessfulDiskSaveOrAcknowledgmentIntoFailure() = runBlocking {
        files.cleanupFailure = true
        val repo = repository()
        repo.save(flight, input)
        assertEquals(1, dao.rows(owner.value!!).size)
        online = true
        assertFalse(repo.syncPending())
        assertTrue(dao.rows(owner.value!!).isEmpty())
        assertFalse(files.files.isEmpty())
        files.cleanupFailure = false
        repo.resume()
        assertTrue(files.files.isEmpty())
    }

    @Test fun invalidRequiredFieldsReadOnlyAndExpiredFlightsNeverReachTheQueue() = runBlocking {
        val repo = repository()
        for (invalid in listOf(input.copy(atd = ""), input.copy(signatureContent = null),
            input.copy(signatureSignedAtUtc = null), input.copy(signatureContent = "not PNG"))) {
            assertTrue(runCatching { repo.save(flight, invalid) }.isFailure)
        }
        val other = submitted.copy(submittedByEmployee = employee.copy(staffMemberId = "22222222-2222-2222-2222-222222222222"))
        for (blocked in listOf(flight.copy(departureSubmissions = listOf(other)), flight.copy(canSaveDeparture = false),
            flight.copy(hasCancellationSubmission = true), flight.copy(currentStaffMemberId = null), flight.copy(rowVersion = "bad"),
            flight.copy(scheduledArrivalUtc = at.minusSeconds(172800).toString(), scheduledDepartureUtc = at.minusSeconds(86400).toString()))) {
            assertTrue(runCatching { repo.save(blocked, input) }.isFailure)
        }
        assertTrue(dao.rows(owner.value!!).isEmpty())
        assertTrue(files.files.isEmpty())
    }
}

private class DepartureMemoryDao : FlightDepartureDao {
    private val state = MutableStateFlow<List<FlightDeparturePendingEntity>>(emptyList())
    override fun observe(owner: String): Flow<List<FlightDeparturePendingEntity>> = state.map { rows -> rows.filter { it.ownerSubject == owner } }
    override suspend fun rows(owner: String) = state.value.filter { it.ownerSubject == owner }
    override suspend fun put(row: FlightDeparturePendingEntity) {
        state.value = state.value.filterNot { it.ownerSubject == row.ownerSubject && it.flightId == row.flightId } + row
    }
    override suspend fun delete(owner: String, flightId: String, changeId: String) {
        state.value = state.value.filterNot { it.ownerSubject == owner && it.flightId == flightId && it.changeId == changeId }
    }
    override suspend fun signatureFiles() = state.value.flatMap { listOfNotNull(it.signatureFile, it.attemptSignatureFile) }.distinct()
    override suspend fun clear() { state.value = emptyList() }
}

private class MemoryDepartureSignatures : DepartureSignatureStore {
    val files = linkedMapOf<String, ByteArray>()
    private var sequence = 0
    var cleanupFailure = false
    override suspend fun save(content: ByteArray): String = "signature-${++sequence}".also { files[it] = content.copyOf() }
    override suspend fun read(key: String): ByteArray = files[key]?.copyOf() ?: throw IOException("Missing signature")
    override suspend fun retainOnly(referenced: Set<String>) {
        if (cleanupFailure) throw IOException("cleanup unavailable")
        files.keys.retainAll(referenced)
    }
}
