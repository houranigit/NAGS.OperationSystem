package com.nags.operations.data.repo

import com.nags.operations.data.*
import com.nags.operations.data.db.dao.FlightArrivalDao
import com.nags.operations.data.db.entities.FlightArrivalPendingEntity
import java.io.IOException
import java.time.Instant
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

class FlightArrivalsRepositoryTest {
    private val owner = MutableStateFlow<String?>("api|account")
    private val dao = ArrivalMemoryDao()
    private var online = false
    private var sends = 0
    private var scheduled = 0
    private var failure: Exception? = null
    private var acceptFailure = false
    private var unknownOtherWork = false
    private var savedServices = false
    private var live: FlightDetail? = null
    private var durableAcknowledgment: suspend (String, String, String) -> Unit = { _, _, _ -> }
    private val staff = "11111111-1111-1111-1111-111111111111"
    private val otherStaff = "22222222-2222-2222-2222-222222222222"
    private val aircraft = "33333333-3333-3333-3333-333333333333"
    private val at = Instant.parse("2026-11-09T12:00:00Z")
    private val input = SaveFlightArrivalRequest(at.toString(), aircraft, " HZ-TEST ", " ", " note ")
    private val employee = AssignedEmployee(staff, "Employee", "E1")
    private val submitted = FlightArrivalSubmission("arrival", employee, at.toString(), aircraft, "A320", "HZ-TEST",
        receivedByEmployee = employee, remarks = "note", isAccepted = true, createdAtUtc = at.toString())
    private val flight = FlightDetail("44444444-4444-4444-4444-444444444444", "SV100", "customer", "SV", "Airline", "station", "AAA",
        "Station", "operation", "Transit", scheduledArrivalUtc = at.toString(), scheduledDepartureUtc = at.plusSeconds(3600).toString(),
        isPerLanding = false, createdAtUtc = at.toString(), rowVersion = "AAAAAAAAAAE=", stationTimeZoneId = "Asia/Riyadh", currentStaffMemberId = staff, canSaveArrival = true)
    private var send: suspend (String, SaveFlightArrivalRequest, String) -> FlightArrivalSaved = { _, request, version ->
        sends++
        assertEquals("HZ-TEST", request.aircraftTailNumber)
        assertNull(request.customerWorkReferenceNumber)
        assertEquals("note", request.remarks)
        assertEquals(flight.rowVersion, version)
        failure?.let { throw it }
        FlightArrivalSaved(submitted, "AAAAAAAAAAI=")
    }
    private fun repository() = FlightArrivalsRepository(dao, owner, { owner.value }, { online },
        send = { id, input, version -> send(id, input, version) },
        accepted = { _, _ -> if (acceptFailure) throw IOException("disk unavailable") },
        schedule = { scheduled++ }, json = Json, now = { at }, onAcknowledgedBeforeDelete = { id, before, after -> durableAcknowledgment(id, before, after) },
        fetchFlight = { live ?: flight }, hasUnacknowledgedOtherWork = { unknownOtherWork }, hasSavedServices = { savedServices })

    @Test fun peerAcknowledgmentAdvancesOnlyReviewedUnsentInputAndNeverUsesFetchedVersion() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        repo.onPriorWriteAcknowledged(flight.id, flight.rowVersion, "AAAAAAAAAAI=")
        assertEquals("AAAAAAAAAAI=", dao.rows(owner.value!!).single().rowVersion)
        assertTrue(dao.rows(owner.value!!).single().needsArrivalComparison)
        live = flight.copy(rowVersion = "AAAAAAAAAAk=") // An unrelated newer read is not acknowledgment lineage.
        send = { _, _, version ->
            sends++
            assertEquals("AAAAAAAAAAI=", version)
            throw ApiException(409, "External flight edit")
        }
        online = true
        assertFalse(repo.syncPending())
        assertEquals("conflict", dao.rows(owner.value!!).single().status)
        assertEquals(1, sends)
    }

    @Test fun changedReviewedArrivalConflictsBeforeUploadAfterPeerAcknowledgment() = runBlocking {
        val repo = repository()
        repo.save(flight.copy(arrivalSubmissions = listOf(submitted)), input)
        repo.onPriorWriteAcknowledged(flight.id, flight.rowVersion, "AAAAAAAAAAI=")
        live = flight.copy(rowVersion = "AAAAAAAAAAI=", arrivalSubmissions = listOf(submitted.copy(remarks = "Another edit")))
        online = true
        assertFalse(repo.syncPending())
        assertEquals("conflict", dao.rows(owner.value!!).single().status)
        assertNull(dao.rows(owner.value!!).single().attemptInputJson)
        assertEquals(0, sends)
    }

    @Test fun acknowledgmentBeforeLocalSaveStillAdvancesOnlyItsOwnLineage() = runBlocking {
        val repo = repository()
        repo.onPriorWriteAcknowledged(flight.id, flight.rowVersion, "AAAAAAAAAAI=")
        repo.save(flight, input)
        assertEquals("AAAAAAAAAAI=", dao.rows(owner.value!!).single().rowVersion)
        send = { _, _, version ->
            sends++
            assertEquals("AAAAAAAAAAI=", version)
            FlightArrivalSaved(submitted, "AAAAAAAAAAM=")
        }
        live = flight.copy(rowVersion = "AAAAAAAAAAI=")
        online = true
        assertFalse(repo.syncPending())
        assertEquals(1, sends)
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun frozenRequestIsNotAdvancedAndReplaysBeforeNewOtherWork() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        online = true
        failure = IOException("Unknown result")
        assertTrue(repo.syncPending())
        val frozen = dao.rows(owner.value!!).single()
        repo.onPriorWriteAcknowledged(flight.id, flight.rowVersion, "AAAAAAAAAAI=")
        assertEquals(frozen.rowVersion, dao.rows(owner.value!!).single().rowVersion)
        unknownOtherWork = true
        failure = null
        assertFalse(repo.syncPending())
        assertEquals(2, sends)
        assertTrue(dao.rows(owner.value!!).isEmpty())
        repo.save(flight, input)
        assertFalse(repo.syncPending()) // Fresh arrival waits; it cannot block recovery of unknown work.
        assertEquals(2, sends)
        assertNull(dao.rows(owner.value!!).single().attemptInputJson)
    }

    @Test fun laterArrivalWaitsForServiceCorrectionsWhileEarlierArrivalRunsFirst() = runBlocking {
        val repo = repository()
        repo.save(flight.copy(arrivalSubmissions = listOf(submitted)), input.copy(ata = at.plusSeconds(60).toString()))
        savedServices = true
        online = true
        assertFalse(repo.mustSyncBeforeServices(flight.id))
        assertFalse(repo.syncPending(priorToServices = true))
        assertFalse(repo.syncPending())
        assertEquals(0, sends)
        savedServices = false
        assertFalse(repo.syncPending())
        assertEquals(1, sends)
        repo.save(flight.copy(arrivalSubmissions = listOf(submitted)), input.copy(ata = at.minusSeconds(60).toString()))
        assertTrue(repo.mustSyncBeforeServices(flight.id))
    }

    @Test fun offlineSaveSurvivesRepositoryRecreationAndOnlyAcknowledgedUploadsAreRemoved() = runBlocking {
        repository().save(flight, input)
        assertEquals(1, dao.rows(owner.value!!).size)
        assertEquals(0, sends)
        assertEquals(1, scheduled)
        val restarted = repository()
        assertEquals("pending", restarted.changes.first().single().status)
        online = true
        assertFalse(restarted.syncPending())
        assertEquals(1, sends)
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun dependentAcknowledgmentIsDurableBeforeArrivalDeletionAndReplaysAfterInterruption() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        online = true
        var callbacks = 0
        durableAcknowledgment = { id, before, after ->
            callbacks++
            assertEquals(flight.id, id)
            assertEquals(flight.rowVersion, before)
            assertEquals("AAAAAAAAAAI=", after)
            assertEquals(1, dao.rows(owner.value!!).size)
            if (callbacks == 1) throw CancellationException("process stopped before dependency persisted")
        }
        assertTrue(runCatching { repo.syncPending() }.exceptionOrNull() is CancellationException)
        val retained = dao.rows(owner.value!!).single()
        assertEquals("pending", retained.status)
        assertEquals(retained.inputJson, retained.attemptInputJson)
        assertFalse(repository().syncPending())
        assertEquals(2, callbacks)
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun lostResponseIsRetriedWithIdenticalInputAndOriginalVersion() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        val original = dao.rows(owner.value!!).single()
        online = true
        failure = IOException("lost response after server commit")
        val acknowledgments = mutableListOf<Pair<String, String>>()
        assertTrue(repo.syncPending { id, version -> acknowledgments += id to version })
        assertTrue(acknowledgments.isEmpty())
        val retained = dao.rows(owner.value!!).single()
        assertEquals(original.changeId, retained.changeId)
        assertEquals(original.inputJson, retained.inputJson)
        assertEquals(original.rowVersion, retained.rowVersion)
        failure = null
        assertFalse(repo.syncPending { id, version -> acknowledgments += id to version })
        assertEquals(listOf(flight.id to "AAAAAAAAAAI="), acknowledgments)
        assertEquals(2, sends)
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun unreadableLocalInputIsKeptForReviewWithoutAnUploadLoop() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        val row = dao.rows(owner.value!!).single()
        dao.put(row.copy(inputJson = "invalid JSON"))
        online = true
        assertFalse(repo.syncPending())
        assertEquals("failed", dao.rows(owner.value!!).single().status)
        assertEquals(0, sends)
    }

    @Test fun conflictsStayVisibleAndDoNotAutomaticallyOverwriteServerData() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        online = true
        failure = ApiException(409, "stale")
        assertFalse(repo.syncPending())
        val conflict = dao.rows(owner.value!!).single()
        assertEquals("conflict", conflict.status)
        assertFalse(repo.syncPending())
        assertEquals(1, sends)
        assertEquals(conflict.inputJson, dao.rows(owner.value!!).single().inputJson)
        repo.retry(flight.id)
        assertEquals("pending", dao.rows(owner.value!!).single().status)
        assertEquals(flight.rowVersion, dao.rows(owner.value!!).single().rowVersion)
    }

    @Test fun invalidRequiredFieldsAndReadOnlyArrivalsNeverReachTheQueue() = runBlocking {
        val repo = repository()
        for (invalid in listOf(input.copy(ata = ""), input.copy(aircraftTypeId = "bad"), input.copy(aircraftTailNumber = "  "))) {
            assertTrue(runCatching { repo.save(flight, invalid) }.isFailure)
        }
        val foreign = submitted.copy(submittedByEmployee = employee.copy(staffMemberId = otherStaff))
        for (blocked in listOf(flight.copy(arrivalSubmissions = listOf(foreign)), flight.copy(canSaveArrival = false),
            flight.copy(hasCancellationSubmission = true), flight.copy(currentStaffMemberId = null), flight.copy(rowVersion = "bad"),
            flight.copy(scheduledArrivalUtc = at.minusSeconds(86400 * 2).toString(), scheduledDepartureUtc = at.minusSeconds(86400).toString()))) {
            assertTrue(runCatching { repo.save(blocked, input) }.isFailure)
        }
        assertTrue(dao.rows(owner.value!!).isEmpty())
        assertEquals(0, sends)
    }

    @Test fun repeatedOfflineEditsUploadOnlyTheLatestInput() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        val original = dao.rows(owner.value!!).single()
        val latest = input.copy(aircraftTailNumber = "HZ-NEW", remarks = "latest")
        repo.save(flight, input.copy(remarks = "intermediate"))
        repo.save(flight, latest)
        val row = dao.rows(owner.value!!).single()
        assertEquals(original.changeId, row.changeId)
        assertEquals(original.rowVersion, row.rowVersion)
        assertNull(row.attemptInputJson)
        assertEquals(latest.normalized(), Json.decodeFromString<SaveFlightArrivalRequest>(row.inputJson))
        val sent = mutableListOf<SaveFlightArrivalRequest>()
        send = { _, value, version ->
            sent += value
            assertEquals(flight.rowVersion, version)
            FlightArrivalSaved(submitted, "AAAAAAAAAAI=")
        }
        online = true
        assertFalse(repository().syncPending())
        assertEquals(listOf(latest.normalized()), sent)
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun editingDuringAnUploadKeepsTheCorrectionAndUsesTheAcknowledgedVersion() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        online = true
        val started = CompletableDeferred<Unit>()
        val finish = CompletableDeferred<Unit>()
        val sent = mutableListOf<Pair<SaveFlightArrivalRequest, String>>()
        send = { _, value, version ->
            sent += value to version
            if (sent.size == 1) { started.complete(Unit); finish.await() }
            FlightArrivalSaved(submitted, "AAAAAAAAAAI=")
        }
        val upload = async(start = CoroutineStart.UNDISPATCHED) { repo.syncPending() }
        started.await()
        val latest = input.copy(aircraftTailNumber = "HZ-NEW")
        val save = async(start = CoroutineStart.UNDISPATCHED) { repo.save(flight, latest) }
        assertTrue(save.isCompleted)
        finish.complete(Unit)
        assertTrue(upload.await())
        val correction = dao.rows(owner.value!!).single()
        assertEquals(latest.normalized(), Json.decodeFromString<SaveFlightArrivalRequest>(correction.inputJson))
        assertEquals("AAAAAAAAAAI=", correction.rowVersion)
        assertNull(correction.attemptInputJson)
        assertFalse(repo.syncPending())
        assertEquals(listOf(input.normalized() to flight.rowVersion, latest.normalized() to "AAAAAAAAAAI="), sent)
    }

    @Test fun editedInputSurvivesRestartAndReplaysAnAmbiguousRequestBeforeTheCorrection() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        online = true
        assertTrue(runCatching {
            failure = IOException("lost response")
            repo.syncPending()
        }.getOrThrow())
        online = false
        val latest = input.copy(remarks = "edited after lost response")
        repo.save(flight, latest)
        assertEquals(input.normalized(), Json.decodeFromString<SaveFlightArrivalRequest>(dao.rows(owner.value!!).single().attemptInputJson!!))
        val sent = mutableListOf<Pair<SaveFlightArrivalRequest, String>>()
        send = { _, value, version -> sent += value to version; FlightArrivalSaved(submitted, "AAAAAAAAAAI=") }
        val restarted = repository()
        online = true
        assertTrue(restarted.syncPending())
        assertFalse(restarted.syncPending())
        assertEquals(listOf(input.normalized() to flight.rowVersion, latest.normalized() to "AAAAAAAAAAI="), sent)
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun aLateFailureRetainsNewerLocalEditsAndTheOriginalAttempt() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        online = true
        val started = CompletableDeferred<Unit>()
        val finish = CompletableDeferred<Unit>()
        send = { _, _, _ -> started.complete(Unit); finish.await(); throw IOException("late failure") }
        val upload = async(start = CoroutineStart.UNDISPATCHED) { repo.syncPending() }
        started.await()
        val latest = input.copy(remarks = "new local edit")
        repo.save(flight, latest)
        finish.complete(Unit)
        assertTrue(upload.await())
        val row = dao.rows(owner.value!!).single()
        assertEquals(latest.normalized(), Json.decodeFromString<SaveFlightArrivalRequest>(row.inputJson))
        assertEquals(input.normalized(), Json.decodeFromString<SaveFlightArrivalRequest>(row.attemptInputJson!!))
        assertEquals(flight.rowVersion, row.rowVersion)
    }

    @Test fun externalConflictsRetainTheEditedInputForExplicitReview() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        online = true
        failure = IOException("lost response")
        assertTrue(repo.syncPending())
        val latest = input.copy(remarks = "new local edit")
        repo.save(flight, latest)
        failure = ApiException(409, "external conflict")
        assertFalse(repo.syncPending())
        val row = dao.rows(owner.value!!).single()
        assertEquals("conflict", row.status)
        assertEquals(flight.rowVersion, row.rowVersion)
        assertEquals(latest.normalized(), Json.decodeFromString<SaveFlightArrivalRequest>(row.inputJson))
        assertFalse(repo.syncPending())
        assertEquals(2, sends)
    }

    @Test fun deniedOrExpiredFlightsKeepTheirInputForReview() = runBlocking {
        val repo = repository()
        online = true
        for (status in listOf(400, 403, 404)) {
            repo.save(flight, input)
            failure = ApiException(status, "rejected")
            assertFalse(repo.syncPending())
            val failed = dao.rows(owner.value!!).single()
            assertEquals("failed", failed.status)
            assertNotNull(failed.error)
            repo.discard(flight.id)
        }
    }

    @Test fun cancellationAndCacheWriteFailureRetainReplayableInput() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        online = true
        failure = CancellationException("stopped")
        assertTrue(runCatching { repo.syncPending() }.exceptionOrNull() is CancellationException)
        assertEquals("pending", dao.rows(owner.value!!).single().status)
        failure = null
        acceptFailure = true
        assertTrue(repo.syncPending())
        assertEquals("pending", dao.rows(owner.value!!).single().status)
        acceptFailure = false
        assertFalse(repo.syncPending())
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun accountSwitchCannotSendAnotherOwnersRowsAndClearRejectsQueuedSaves() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        owner.value = "api|different-account"
        online = true
        assertFalse(repo.syncPending())
        assertEquals(0, sends)
        assertTrue(repo.changes.first().isEmpty())
        repo.pause()
        repo.clear()
        assertTrue(runCatching { repo.save(flight, input) }.isFailure)
        assertTrue(dao.rows("api|account").isEmpty())
    }

    @Test fun sessionPauseWaitsForAnAuthenticatedUploadBeforeCredentialsCanChange() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        online = true
        val started = CompletableDeferred<Unit>()
        val finish = CompletableDeferred<Unit>()
        send = { _, _, _ -> started.complete(Unit); finish.await(); FlightArrivalSaved(submitted, "AAAAAAAAAAI=") }
        val upload = async(start = CoroutineStart.UNDISPATCHED) { repo.syncPending() }
        started.await()
        val pause = async(start = CoroutineStart.UNDISPATCHED) { repo.pause() }
        assertFalse(pause.isCompleted)
        finish.complete(Unit)
        upload.await(); pause.await()
        assertTrue(runCatching { repo.save(flight, input) }.isFailure)
    }

    @Test fun aSlowUploadDoesNotBlockSavingAnotherFlightLocally() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        online = true
        val started = CompletableDeferred<Unit>()
        val finish = CompletableDeferred<Unit>()
        send = { _, _, _ -> started.complete(Unit); finish.await(); FlightArrivalSaved(submitted, "AAAAAAAAAAI=") }
        val upload = async(start = CoroutineStart.UNDISPATCHED) { repo.syncPending() }
        started.await()
        val otherFlight = flight.copy(id = "55555555-5555-5555-5555-555555555555")
        val localSave = async(start = CoroutineStart.UNDISPATCHED) { repo.save(otherFlight, input) }
        assertTrue(localSave.isCompleted)
        assertEquals(2, dao.rows(owner.value!!).size)
        finish.complete(Unit)
        assertTrue(upload.await()) // The newly queued flight still needs its own upload.
        assertEquals(otherFlight.id, dao.rows(owner.value!!).single().flightId)
    }

    @Test fun aLateFailedUploadCannotResurrectDiscardedOrReplacedInput() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        online = true
        val started = CompletableDeferred<Unit>()
        val finish = CompletableDeferred<Unit>()
        send = { _, _, _ -> started.complete(Unit); finish.await(); throw IOException("late failure") }
        val upload = async(start = CoroutineStart.UNDISPATCHED) { repo.syncPending() }
        started.await()
        repo.discard(flight.id)
        repo.save(flight, input.copy(aircraftTailNumber = "HZ-NEW"))
        val replacement = dao.rows(owner.value!!).single()
        finish.complete(Unit)
        assertTrue(upload.await())
        assertEquals(replacement, dao.rows(owner.value!!).single())
    }

    @Test fun acknowledgedVersionFollowsOwnCorrectionChainAndResetsForAnotherOwner() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        online = true
        repo.syncPending()
        assertEquals("AAAAAAAAAAI=", repo.acknowledgedVersion(flight.id, flight.rowVersion))
        send = { _, _, version ->
            assertEquals("AAAAAAAAAAI=", version)
            FlightArrivalSaved(submitted, "AAAAAAAAAAM=")
        }
        repo.save(flight.copy(rowVersion = "AAAAAAAAAAI="), input.copy(remarks = "corrected"))
        repo.syncPending()
        assertEquals("AAAAAAAAAAM=", repo.acknowledgedVersion(flight.id, flight.rowVersion))
        assertEquals("unrelated", repo.acknowledgedVersion(flight.id, "unrelated"))
        owner.value = "api|another-account"
        assertEquals(flight.rowVersion, repo.acknowledgedVersion(flight.id, flight.rowVersion))
    }

    @Test fun accountPauseWaitsForAnOnlineDepartureSessionGate() = runBlocking {
        val repo = repository()
        val started = CompletableDeferred<Unit>()
        val finish = CompletableDeferred<Unit>()
        val departure = async(start = CoroutineStart.UNDISPATCHED) {
            repo.withCurrentSession { started.complete(Unit); finish.await() }
        }
        started.await()
        val pause = async(start = CoroutineStart.UNDISPATCHED) { repo.pause() }
        assertFalse(pause.isCompleted)
        finish.complete(Unit)
        departure.await()
        pause.await()
        assertTrue(runCatching { repo.withCurrentSession { } }.isFailure)
    }

    @Test fun ownAndForeignSubmissionsSelectTheCorrectReadOnlyState() {
        val foreign = submitted.copy(id = "foreign", submittedByEmployee = employee.copy(staffMemberId = otherStaff))
        assertFalse(flight.copy(arrivalSubmissions = listOf(foreign)).arrivalEditable())
        val ownAndForeign = flight.copy(arrivalSubmissions = listOf(foreign, submitted))
        assertTrue(ownAndForeign.arrivalEditable())
        assertEquals(submitted, ownAndForeign.visibleArrival())
        assertEquals(setOf(ArrivalInputError.AtaRequired), input.copy(ata = "0001-01-01T00:00:00Z").validationErrors(flight.rowVersion))
        assertTrue(input.copy(ata = "2026-01-01T15:00:00+03:00").validationErrors(flight.rowVersion).isEmpty())
    }

    @Test fun editingOtherFieldsDoesNotShiftArrivalDuringTheRepeatedDaylightSavingHour() {
        val zone = java.time.ZoneId.of("America/Chicago")
        assertEquals("2026-11-01T07:30:00Z", arrivalInputTime("2026-11-01T01:30", "2026-11-01T07:30:00Z", zone))
        assertEquals("2026-11-01T08:30:00Z", arrivalInputTime("2026-11-01T02:30", "2026-11-01T07:30:00Z", zone))
    }
}

private class ArrivalMemoryDao : FlightArrivalDao {
    private val state = MutableStateFlow<List<FlightArrivalPendingEntity>>(emptyList())
    override fun observe(owner: String): Flow<List<FlightArrivalPendingEntity>> = state.map { rows -> rows.filter { it.ownerSubject == owner } }
    override suspend fun rows(owner: String) = state.value.filter { it.ownerSubject == owner }
    override suspend fun put(row: FlightArrivalPendingEntity) {
        state.value = state.value.filterNot { it.ownerSubject == row.ownerSubject && it.flightId == row.flightId } + row
    }
    override suspend fun delete(owner: String, flightId: String, changeId: String) {
        state.value = state.value.filterNot { it.ownerSubject == owner && it.flightId == flightId && it.changeId == changeId }
    }
    override suspend fun clear() { state.value = emptyList() }
}
