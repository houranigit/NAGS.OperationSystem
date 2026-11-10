package com.nags.operations.data.repo

import com.nags.operations.data.*
import com.nags.operations.data.db.dao.FlightArrivalDao
import com.nags.operations.data.db.dao.FlightCancellationDao
import com.nags.operations.data.db.entities.FlightArrivalPendingEntity
import com.nags.operations.data.db.entities.FlightCancellationPendingEntity
import java.io.IOException
import java.time.Instant
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.async
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.sync.Mutex
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import org.junit.Assert.*
import org.junit.Test

class FlightCancellationsRepositoryTest {
    private val dao = CancellationMemoryDao()
    private val owner = MutableStateFlow<String?>("api|account")
    private var online = false
    private val at = Instant.parse("2026-11-09T12:00:00Z")
    private val staff = "11111111-1111-1111-1111-111111111111"
    private val other = "22222222-2222-2222-2222-222222222222"
    private val employee = AssignedEmployee(staff, "Employee", "E1")
    private val flight = FlightDetail("44444444-4444-4444-4444-444444444444", "SV100", "customer", "SV", "Airline", "station", "AAA",
        "Station", "operation", "Transit", scheduledArrivalUtc = at.toString(), scheduledDepartureUtc = at.plusSeconds(3600).toString(),
        isPerLanding = false, createdAtUtc = at.toString(), rowVersion = "AAAAAAAAAAE=", stationTimeZoneId = "Etc/UTC",
        currentStaffMemberId = staff, canSaveArrival = true, canSaveDeparture = true, canSaveCancellation = true)
    private val input = SaveFlightCancellationRequest(at.toString(), " notes ")
    private val submitted = FlightCancellationSubmission("33333333-3333-3333-3333-333333333333", employee, at.toString(), "notes", true, at.toString())
    private var sent = mutableListOf<Pair<SaveFlightCancellationRequest, String>>()
    private var reverted = mutableListOf<Pair<String, String>>()
    private var accepted = 0
    private var acceptedReverts = 0
    private var scheduleCount = 0
    private var savedWork = false
    private val submissionGate = Mutex()
    private var send: suspend (String, SaveFlightCancellationRequest, String) -> FlightCancellationSaved = { _, value, version ->
        sent += value to version; FlightCancellationSaved(submitted.copy(remarks = value.remarks), "AAAAAAAAAAI=")
    }
    private var onRevert: suspend (String, String, String) -> FlightCancellationReverted = { _, id, version ->
        reverted += id to version; FlightCancellationReverted(emptyList(), "AAAAAAAAAAM=")
    }
    private fun repository(work: suspend (String) -> Boolean = { savedWork }) = FlightCancellationsRepository(dao, owner,
        { owner.value }, { online }, { id, value, version -> send(id, value, version) },
        { id, submission, version -> onRevert(id, submission, version) }, { _, _ -> accepted++ },
        { _, _ -> acceptedReverts++ }, { scheduleCount++ }, Json, work, submissionGate, { at })

    @Test fun offlineSaveSurvivesRestartAndAcknowledgesOnlyAfterConnectionReturns() = runBlocking {
        repository().save(flight, input.copy(acknowledgeConflictingSubmission = true))
        val stored = dao.rows(owner.value!!).single()
        assertEquals("save", stored.operation)
        assertEquals("notes", Json.decodeFromString<SaveFlightCancellationRequest>(stored.inputJson!!).remarks)
        assertTrue(Json.decodeFromString<SaveFlightCancellationRequest>(stored.inputJson).acknowledgeConflictingSubmission)
        val restarted = repository()
        assertEquals(stored, restarted.changes.first().single())
        assertTrue(restarted.syncPending())
        assertTrue(sent.isEmpty())
        online = true
        assertFalse(restarted.syncPending())
        assertEquals(1, sent.size)
        assertEquals(1, accepted)
        assertFalse(restarted.hasSavedInput(flight.id))
    }

    @Test fun revertingAnUnsentCancellationOnlyRemovesItsLocalIntent() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        repo.revert(flight)
        assertFalse(repo.hasSavedInput(flight.id))
        online = true
        assertFalse(repo.syncPending())
        assertTrue(sent.isEmpty())
        assertTrue(reverted.isEmpty())
    }

    @Test fun anUnknownSaveIsReplayedBeforeRevertingItsExactAcknowledgedId() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        online = true
        send = { _, value, version -> sent += value to version; throw IOException("lost response") }
        assertTrue(repo.syncPending())
        assertTrue(runCatching { repo.discard(flight.id) }.isFailure)
        repo.revert(flight)
        val desired = dao.rows(owner.value!!).single()
        assertEquals("revert", desired.operation)
        assertEquals("save", desired.attemptOperation)
        send = { _, value, version -> sent += value to version; FlightCancellationSaved(submitted, "AAAAAAAAAAI=") }
        assertFalse(repo.syncPending())
        assertEquals(sent[0], sent[1])
        assertEquals(listOf(submitted.id to "AAAAAAAAAAI="), reverted)
        assertEquals(1, acceptedReverts)
        assertFalse(repo.hasSavedInput(flight.id))
    }

    @Test fun offlineRevertRetainsItsTargetAndVersionAcrossAnUnknownResponse() = runBlocking {
        val repo = repository()
        val canceled = flight.copy(cancellationSubmissions = listOf(submitted), hasCancellationSubmission = true)
        repo.revert(canceled)
        assertEquals(submitted.id, dao.rows(owner.value!!).single().submissionId)
        online = true
        onRevert = { _, id, version -> reverted += id to version; throw IOException("lost response") }
        assertTrue(repo.syncPending())
        onRevert = { _, id, version -> reverted += id to version; FlightCancellationReverted(emptyList(), "AAAAAAAAAAM=") }
        assertFalse(repository().syncPending())
        assertEquals(reverted[0], reverted[1])
        assertEquals(1, acceptedReverts)
    }

    @Test fun aKnownConflictRequiresExplicitFreshSaveAndNeverSilentlyRebases() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        online = true
        send = { _, value, version -> sent += value to version; throw ApiException(412, "changed") }
        assertFalse(repo.syncPending())
        val conflict = dao.rows(owner.value!!).single()
        assertEquals("conflict", conflict.status)
        assertTrue(repo.hasSavedInput(flight.id))
        repo.retry(flight.id)
        assertFalse(repo.syncPending())
        assertEquals(sent[0], sent[1])
        repo.save(flight.copy(rowVersion = "AAAAAAAAAAM="), input.copy(acknowledgeConflictingSubmission = true))
        val reviewed = dao.rows(owner.value!!).single()
        assertNotEquals(conflict.changeId, reviewed.changeId)
        assertEquals("AAAAAAAAAAM=", reviewed.rowVersion)
        assertNull(reviewed.attemptOperation)
    }

    @Test fun ownWorkBlocksCancellationButOtherWorkRequiresAcknowledgement() = runBlocking {
        val arrival = FlightArrivalSubmission("arrival", employee, at.toString(), "55555555-5555-5555-5555-555555555555", "A320", "TAIL",
            receivedByEmployee = employee, isAccepted = true, createdAtUtc = at.toString())
        val repo = repository()
        assertTrue(runCatching { repo.save(flight.copy(arrivalSubmissions = listOf(arrival)), input.copy(acknowledgeConflictingSubmission = true)) }.isFailure)
        savedWork = true
        assertTrue(runCatching { repo.save(flight, input) }.isFailure)
        savedWork = false
        val foreign = flight.copy(arrivalSubmissions = listOf(arrival.copy(submittedByEmployee = employee.copy(staffMemberId = other))))
        assertTrue(runCatching { repo.save(foreign, input) }.isFailure)
        repo.save(foreign, input.copy(acknowledgeConflictingSubmission = true))
        assertTrue(repo.hasSavedInput(flight.id))
    }

    @Test fun sharedSaveGatePreventsSimultaneousOwnWorkAndCancellationIntent() = runBlocking {
        val arrivals = CancellationTestArrivalDao()
        val started = CompletableDeferred<Unit>()
        val release = CompletableDeferred<Unit>()
        val repo = repository { started.complete(Unit); release.await(); arrivals.rows(owner.value!!).isNotEmpty() }
        val arrivalRepo = FlightArrivalsRepository(arrivals, owner, { owner.value }, { false },
            send = { _, _, _ -> error("offline") }, accepted = { _, _ -> }, schedule = { }, json = Json, now = { at },
            hasSavedCancellation = { repo.hasSavedInput(it) }, submissionGate = submissionGate)
        val cancelSave = async(start = CoroutineStart.UNDISPATCHED) { repo.save(flight, input) }
        started.await()
        val workSave = async(start = CoroutineStart.UNDISPATCHED) {
            runCatching { arrivalRepo.save(flight, SaveFlightArrivalRequest(at.toString(), "55555555-5555-5555-5555-555555555555", "TAIL")) }
        }
        assertFalse(workSave.isCompleted)
        release.complete(Unit)
        cancelSave.await()
        assertTrue(workSave.await().isFailure)
        assertTrue(arrivals.rows(owner.value!!).isEmpty())
        assertTrue(repo.hasSavedInput(flight.id))
    }

    @Test fun changingOwnerOrPausingPreventsLateLocalWritesAndQueuePublication() = runBlocking {
        val repo = repository()
        repo.save(flight, input)
        online = true
        val started = CompletableDeferred<Unit>()
        val release = CompletableDeferred<Unit>()
        send = { _, _, _ -> started.complete(Unit); release.await(); FlightCancellationSaved(submitted, "AAAAAAAAAAI=") }
        val upload = async(start = CoroutineStart.UNDISPATCHED) { repo.syncPending() }
        started.await()
        owner.value = "api|other"
        release.complete(Unit)
        assertFalse(upload.await())
        assertEquals(0, accepted)
        assertTrue(repo.changes.first().isEmpty())
        owner.value = "api|account"
        assertTrue(repo.hasSavedInput(flight.id))
        repo.pause()
        assertTrue(runCatching { repo.save(flight, input) }.isFailure)
        repo.resume()
        assertFalse(repo.changes.first().isEmpty())
    }

    @Test fun newCancellationAttributionPreservesOtherEmployeeWorkAndLegacyCachesRemainConservative() {
        val otherCancellation = submitted.copy(submittedByEmployee = employee.copy(staffMemberId = other))
        val mixed = flight.copy(cancellationSubmissions = listOf(otherCancellation), hasCancellationSubmission = true)
        assertFalse(mixed.hasOwnCancellation())
        assertTrue(mixed.arrivalEditable())
        assertTrue(mixed.departureEditable())
        assertFalse(flight.copy(cancellationSubmissions = listOf(submitted), hasCancellationSubmission = true).arrivalEditable())
        assertFalse(flight.copy(hasCancellationSubmission = true).departureEditable())
    }
}

private class CancellationMemoryDao : FlightCancellationDao {
    private val state = MutableStateFlow<List<FlightCancellationPendingEntity>>(emptyList())
    override fun observe(owner: String): Flow<List<FlightCancellationPendingEntity>> = state.map { rows -> rows.filter { it.ownerSubject == owner } }
    override suspend fun rows(owner: String) = state.value.filter { it.ownerSubject == owner }
    override suspend fun put(row: FlightCancellationPendingEntity) { state.value = state.value.filterNot { it.ownerSubject == row.ownerSubject && it.flightId == row.flightId } + row }
    override suspend fun delete(owner: String, flightId: String, changeId: String) { state.value = state.value.filterNot { it.ownerSubject == owner && it.flightId == flightId && it.changeId == changeId } }
    override suspend fun clear() { state.value = emptyList() }
}

private class CancellationTestArrivalDao : FlightArrivalDao {
    private val values = mutableListOf<FlightArrivalPendingEntity>()
    override fun observe(owner: String): Flow<List<FlightArrivalPendingEntity>> = kotlinx.coroutines.flow.flowOf(values.filter { it.ownerSubject == owner })
    override suspend fun rows(owner: String) = values.filter { it.ownerSubject == owner }
    override suspend fun put(row: FlightArrivalPendingEntity) { values.removeAll { it.ownerSubject == row.ownerSubject && it.flightId == row.flightId }; values += row }
    override suspend fun delete(owner: String, flightId: String, changeId: String) { values.removeAll { it.ownerSubject == owner && it.flightId == flightId && it.changeId == changeId } }
    override suspend fun clear() { values.clear() }
}
