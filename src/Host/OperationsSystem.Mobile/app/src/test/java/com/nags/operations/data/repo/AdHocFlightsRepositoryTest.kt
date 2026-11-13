package com.nags.operations.data.repo

import com.nags.operations.data.*
import com.nags.operations.data.db.dao.AdHocFlightDao
import com.nags.operations.data.db.entities.*
import java.io.IOException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.launch
import kotlinx.coroutines.yield
import kotlinx.coroutines.async
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.encodeToString
import org.junit.Assert.*
import org.junit.Test

class AdHocFlightsRepositoryTest {
    @Test fun offlineCreationAndDependentWorkSurviveRepositoryRecreationAndWaitForCreationAcknowledgment() = runBlocking {
        val f = Fixture()
        val repo = f.repository()
        repo.refreshContext()
        f.online = false
        val flight = repo.newFlight(f.id, f.input)
        repo.save(flight, f.input) { f.work[f.id] = flight.rowVersion }
        assertEquals(1, f.scheduled)
        assertTrue(f.creates.isEmpty())
        assertTrue(f.releases.isEmpty())
        val restarted = f.repository()
        assertEquals(f.input.flightNumber, restarted.localFlight(f.id)!!.flightNumber)
        assertFalse(restarted.localFlight(f.id)!!.canEditFlightDetails)
        assertTrue(restarted.localFlight(f.id)!!.assignedEmployees.isEmpty())
        f.online = true
        assertFalse(restarted.syncPending())
        assertEquals(listOf(f.id to f.input), f.creates)
        assertEquals(listOf(Triple(f.id, LOCAL_ADHOC_VERSION, f.v1)), f.releases)
        assertEquals(f.v1, f.work[f.id])
        assertEquals("acknowledged", restarted.row(f.id)!!.status)
    }

    @Test fun lostCreationResponseRetriesTheExactOriginalFingerprintBeforeAnyNewPlanningDetails() = runBlocking {
        val f = Fixture()
        val repo = f.repository()
        repo.refreshContext()
        val flight = repo.newFlight(f.id, f.input)
        repo.save(flight, f.input) {}
        f.createHandler = { _, _ -> throw IOException("Lost response") }
        assertTrue(repo.syncPending())
        val frozen = repo.row(f.id)!!.attemptInputJson
        val correction = f.input.copy(flightNumber = " edited ")
        repo.save(flight, correction) {}
        assertEquals(frozen, repo.row(f.id)!!.attemptInputJson)
        f.createHandler = { id, input -> f.ack(id, input, f.v1) }
        assertTrue(f.repository().syncPending())
        assertEquals(listOf(f.id to f.input, f.id to f.input), f.creates)
        assertTrue(f.updates.isEmpty())
        assertEquals("pending", repo.row(f.id)!!.status)
        assertFalse(f.repository().syncPending())
        assertEquals(listOf(Triple(f.id, correction.normalized(), f.v1)), f.updates)
        assertEquals("EDITED", repo.localFlight(f.id)!!.flightNumber)
        assertEquals(listOf(Triple(f.id, LOCAL_ADHOC_VERSION, f.v1), Triple(f.id, f.v1, "AAAAAAAAAAI=")), f.planningAcks)
    }

    @Test fun lateSectionOrMediaStagingFailureRollsBackCreationAndKeepsEarlierOwnedInput() = runBlocking {
        val f = Fixture()
        val repo = f.repository()
        repo.refreshContext()
        val existingId = "77777777-7777-7777-7777-777777777777"
        f.work[existingId] = "earlier-owned-version"
        val flight = repo.newFlight(f.id, f.input)
        assertTrue(runCatching { repo.save(flight, f.input) {
            f.work[f.id] = flight.rowVersion
            throw IOException("Selected attachment cannot be copied")
        } }.isFailure)
        assertNull(repo.row(f.id))
        assertEquals(mapOf(existingId to "earlier-owned-version"), f.work)
        assertEquals(0, f.scheduled)
        assertTrue(f.creates.isEmpty())
    }

    @Test fun aFrozenOperationalAttemptCannotBeRetargetedByAPlanningSave() = runBlocking {
        val f = Fixture()
        val repo = f.repository()
        repo.refreshContext()
        val recorded = f.ack(f.id, f.input, f.v1)
        f.work[f.id] = f.v1
        f.frozenWork = true
        // The fixture rejects the same hold that the actual coordinator rejects for frozen requests.
        assertTrue(runCatching { repo.save(recorded, f.input.copy(flightNumber = "CHANGED")) {} }.isFailure)
        assertNull(repo.row(f.id))
        assertEquals(f.v1, f.work[f.id])
        assertTrue(f.releases.isEmpty())
    }

    @Test fun knownStationChangesAndDeniedContextRemoveLocalMembershipButRetainRecoverableIntent() = runBlocking {
        val f = Fixture()
        val repo = f.repository()
        repo.refreshContext()
        repo.save(repo.newFlight(f.id, f.input), f.input) {}
        repo.confirmStation("88888888-8888-8888-8888-888888888888")
        assertTrue(repo.localFlights().isEmpty())
        assertNotNull(repo.recoveryFlight(f.id))
        assertEquals(1, repo.pending.first().size)
        repo.refreshContext()
        f.contextHandler = { throw ApiException(403, "Denied") }
        assertTrue(runCatching { repo.refreshContext() }.isFailure)
        assertNull(repo.cachedContext())
        assertTrue(repo.localFlights().isEmpty())
        assertNotNull(repo.row(f.id))
    }

    @Test fun switchedAccountCannotPublishAnOldCreationResponseOrExposeItsLocalFlight() = runBlocking {
        val f = Fixture()
        val repo = f.repository()
        repo.refreshContext()
        repo.save(repo.newFlight(f.id, f.input), f.input) {}
        val started = CompletableDeferred<Unit>()
        val response = CompletableDeferred<FlightDetail>()
        f.createHandler = { _, _ -> started.complete(Unit); response.await() }
        val pending = async { repo.syncPending() }
        started.await()
        f.owner.value = "other-api|other-account"
        response.complete(f.ack(f.id, f.input, f.v1))
        assertFalse(pending.await())
        assertTrue(repo.localFlights().isEmpty())
        assertTrue(repo.pending.first().isEmpty())
        assertTrue(f.releases.isEmpty())
        f.owner.value = f.firstOwner
        assertNull(repo.row(f.id)!!.materializedDetailJson)
        assertNotNull(repo.row(f.id)!!.attemptInputJson)
    }

    @Test fun transmittedUnknownCreationCannotBeDiscardedButDefinitiveFailureCanBeDiscardedWithDependentWork() = runBlocking {
        val f = Fixture()
        val repo = f.repository()
        repo.refreshContext()
        val flight = repo.newFlight(f.id, f.input)
        repo.save(flight, f.input) { f.work[f.id] = flight.rowVersion }
        f.createHandler = { _, _ -> throw IOException("Unknown outcome") }
        repo.syncPending()
        assertTrue(runCatching { repo.discard(f.id) }.isFailure)
        assertNotNull(repo.row(f.id))
        assertEquals(1, f.work.size)
        f.createHandler = { _, _ -> throw ApiException(400, "Rejected before creation") }
        assertFalse(repo.syncPending())
        assertTrue(repo.row(f.id)!!.attemptRejected)
        repo.discard(f.id)
        assertNull(repo.row(f.id))
        assertTrue(f.work.isEmpty())
    }

    @Test fun onlyMaterializedFlightsAreRevokedByACompleteServerManifestAndPreparedOnesReleaseTheirFallback() = runBlocking {
        val f = Fixture()
        val repo = f.repository()
        repo.refreshContext()
        repo.save(repo.newFlight(f.id, f.input), f.input) {}
        repo.syncPending()
        val newId = "77777777-7777-7777-7777-777777777777"
        repo.save(repo.newFlight(newId, f.input), f.input) {}
        repo.reconcileManifest(emptySet(), false)
        assertEquals(listOf(newId), repo.localFlights().map { it.id })
        assertNotNull(repo.recoveryFlight(f.id))
        repo.reconcileManifest(setOf(f.id), true)
        assertNull(repo.row(f.id))
        assertNotNull(repo.row(newId))
    }

    @Test fun arrivalOrWorkImmediatelyLocksPreviouslyUnsentFlightDetails() = runBlocking {
        val f = Fixture()
        val repo = f.repository()
        repo.refreshContext()
        val flight = repo.newFlight(f.id, f.input)
        repo.save(flight, f.input) { f.work[f.id] = flight.rowVersion }
        val before = repo.row(f.id)
        assertTrue(runCatching { repo.save(flight, f.input.copy(flightNumber = "CHANGED")) {} }.isFailure)
        assertEquals(before, repo.row(f.id))
    }

    @Test fun localSourceHintsIgnoreInitialStateAndAttemptBookkeepingWithoutSuppressingRealIdentityChanges() = runBlocking {
        val f = Fixture()
        val repo = f.repository()
        var hints = 0
        val collector = launch(start = CoroutineStart.UNDISPATCHED) { repo.localChanges.collect { hints++ } }
        try {
            repeat(20) { yield() }
            assertEquals(0, hints)
            repo.refreshContext()
            repeat(20) { yield() }
            assertEquals(0, hints)
            repo.save(repo.newFlight(f.id, f.input), f.input) {}
            repeat(20) { yield() }
            assertEquals(1, hints)
            f.createHandler = { _, _ -> throw IOException("Lost response") }
            repo.syncPending()
            repeat(20) { yield() }
            assertEquals(1, hints)
            repo.retry(f.id)
            repeat(20) { yield() }
            assertEquals(1, hints)
            f.createHandler = { id, input -> f.ack(id, input, f.v1) }
            repo.syncPending()
            repeat(20) { yield() }
            assertEquals(2, hints)
        } finally { collector.cancel() }
    }

    @Test fun aPendingCustomerCorrectionKeepsItsSelectedCaptionAndRejectsOutOfWindowStaging() = runBlocking {
        val f = Fixture()
        val repo = f.repository()
        repo.refreshContext()
        repo.save(repo.newFlight(f.id, f.input), f.input) {}
        repo.syncPending()
        val customer = "66666666-6666-6666-6666-666666666666"
        val corrected = f.input.copy(customerId = customer)
        repo.save(f.ack(f.id, f.input, f.v1).copy(customerName = "Selected customer", customerIataCode = "XY"), corrected) {}
        assertEquals(customer, repo.localFlight(f.id)!!.customerId)
        assertEquals("Selected customer", repo.localFlight(f.id)!!.customerName)
        assertEquals("XY", repo.localFlight(f.id)!!.customerIataCode)
        val before = repo.row(f.id)
        assertTrue(runCatching { repo.save(f.ack(f.id, f.input, f.v1),
            corrected.copy(scheduledArrivalUtc = "2026-11-15T10:00:00Z", scheduledDepartureUtc = "2026-11-15T11:00:00Z")) {} }.isFailure)
        assertEquals(before, repo.row(f.id))
    }

    @Test fun revokedMaterializedRecoveryRedactsOtherEmployeesGraphAndKeepsOnlyTheRetainedIdentity() = runBlocking {
        val f = Fixture()
        val repo = f.repository()
        repo.refreshContext()
        val colleague = AssignedEmployee("66666666-6666-6666-6666-666666666666", "Colleague", "E2")
        val arrived = f.ack(f.id, f.input, f.v1).copy(assignedEmployees = listOf(colleague),
            arrivalSubmissions = listOf(FlightArrivalSubmission("77777777-7777-7777-7777-777777777777", colleague,
                f.context.asOfUtc, "88888888-8888-8888-8888-888888888888", "Aircraft", "TAIL", receivedByEmployee = colleague,
                isAccepted = true, createdAtUtc = f.context.asOfUtc)),
            services = listOf(FlightServiceRecord("99999999-9999-9999-9999-999999999999", f.context.allowedServiceIds.single(),
                "Service", f.context.asOfUtc, createdByUserId = "colleague", createdAtUtc = f.context.asOfUtc)))
        f.dao.put(AdHocFlightPendingEntity(f.firstOwner, f.id, f.station,
            com.nags.operations.data.api.HttpClientFactory.json.encodeToString(f.input),
            com.nags.operations.data.api.HttpClientFactory.json.encodeToString(arrived), f.v1, f.context.asOfUtc,
            materializedDetailJson = com.nags.operations.data.api.HttpClientFactory.json.encodeToString(arrived), viewRevoked = true))
        val recovered = repo.recoveryFlight(f.id)!!
        assertEquals(f.input.flightNumber, recovered.flightNumber)
        assertTrue(recovered.arrivalSubmissions.isEmpty())
        assertTrue(recovered.departureSubmissions.isEmpty())
        assertTrue(recovered.services.isEmpty())
        assertTrue(recovered.tasks.isEmpty())
        assertTrue(recovered.cancellationSubmissions.isEmpty())
        assertTrue(recovered.assignedEmployees.isEmpty())
        assertFalse(recovered.canEditFlightDetails)
        assertFalse(recovered.canSaveArrival || recovered.canSaveDeparture || recovered.canSaveServices || recovered.canSaveTasks || recovered.canSaveCancellation)
    }

    @Test fun discardingMaterializedPlanningPreservesTheDependentOperationalIntentAtItsOriginalReviewedVersion() = runBlocking {
        val f = Fixture()
        val repo = f.repository()
        repo.refreshContext()
        val recorded = f.ack(f.id, f.input, f.v1)
        repo.save(recorded, f.input.copy(flightNumber = "CORRECTED")) { f.work[f.id] = f.v1 }
        assertNotNull(repo.row(f.id))
        repo.discard(f.id)
        assertNull(repo.row(f.id))
        assertEquals(mapOf(f.id to f.v1), f.work)
        assertEquals(listOf(Triple(f.id, f.v1, f.v1)), f.releases)
    }

    @Test fun recoveredCreationWithChangedPlanningAdoptsCurrentDisplayButKeepsTheOriginalWorkForReview() = runBlocking {
        val f = Fixture()
        val repo = f.repository()
        repo.refreshContext()
        val flight = repo.newFlight(f.id, f.input)
        repo.save(flight, f.input) { f.work[f.id] = flight.rowVersion }
        val current = f.input.copy(flightNumber = "CURRENT", scheduledDepartureUtc = "2026-11-13T12:00:00Z")
        f.createHandler = { id, _ -> f.ack(id, current, f.v1) }
        repo.syncPending()
        assertEquals("CURRENT", repo.localFlight(f.id)!!.flightNumber)
        assertEquals(current.scheduledDepartureUtc, repo.localFlight(f.id)!!.scheduledDepartureUtc)
        assertEquals(f.input, com.nags.operations.data.api.HttpClientFactory.json.decodeFromString<AdHocFlightInput>(
            checkNotNull(repo.row(f.id)!!.acknowledgedCreationInputJson)))
        assertEquals(LOCAL_ADHOC_VERSION, f.work[f.id])
        assertTrue(f.heldReviews.containsKey(f.id))
        assertTrue(f.releases.isEmpty())
        assertTrue(f.planningAcks.isEmpty())
    }

    @Test fun recoveredCreationWithAColleaguesArrivalHoldsInitialWorkAndPreservesLaterDesiredPlanning() = runBlocking {
        val f = Fixture()
        val repo = f.repository()
        repo.refreshContext()
        val flight = repo.newFlight(f.id, f.input)
        repo.save(flight, f.input) {}
        f.createHandler = { _, _ -> throw IOException("Lost response") }
        repo.syncPending()
        val later = f.input.copy(flightNumber = "LATER")
        repo.save(flight, later) { f.work[f.id] = flight.rowVersion }
        val colleague = AssignedEmployee("66666666-6666-6666-6666-666666666666", "Colleague", "E2")
        f.createHandler = { id, original -> f.ack(id, original, f.v1).copy(canEditFlightDetails = false,
            arrivalSubmissions = listOf(FlightArrivalSubmission("77777777-7777-7777-7777-777777777777", colleague,
                f.context.asOfUtc, "88888888-8888-8888-8888-888888888888", "Aircraft", "TAIL", receivedByEmployee = colleague,
                isAccepted = true, createdAtUtc = f.context.asOfUtc))) }
        assertTrue(repo.syncPending())
        assertEquals(later, com.nags.operations.data.api.HttpClientFactory.json.decodeFromString<AdHocFlightInput>(repo.row(f.id)!!.inputJson))
        assertEquals("pending", repo.row(f.id)!!.status)
        assertEquals(LOCAL_ADHOC_VERSION, f.work[f.id])
        assertTrue(f.heldReviews.containsKey(f.id))
        assertTrue(f.releases.isEmpty())
        assertTrue(f.planningAcks.isEmpty())
    }

    @Test fun confirmedPartialWorkLocksMaterializedPlanningAfterThePendingQueueClearsAndRestart() = runBlocking {
        for (kind in listOf("arrival", "service", "task", "cancellation")) {
            val f = Fixture()
            val repo = f.repository()
            repo.refreshContext()
            repo.save(repo.newFlight(f.id, f.input), f.input) {}
            repo.syncPending()
            assertTrue(repo.localFlight(f.id)!!.canEditFlightDetails)
            f.work[f.id] = f.v1
            val employee = AssignedEmployee(f.staff, "Employee", "E1")
            val acknowledged = f.ack(f.id, f.input, "AAAAAAAAAAI=").let { detail -> when (kind) {
                "arrival" -> detail.copy(arrivalSubmissions = listOf(FlightArrivalSubmission("arrival", employee,
                    f.context.asOfUtc, "aircraft", "Aircraft", "TAIL", receivedByEmployee = employee,
                    isAccepted = true, createdAtUtc = f.context.asOfUtc)))
                "service" -> detail.copy(services = listOf(FlightServiceRecord("service", "service-type", "Service",
                    f.context.asOfUtc, createdByUserId = "user", createdAtUtc = f.context.asOfUtc)))
                "task" -> detail.copy(tasks = listOf(FlightTaskRecord("task", 0, f.context.asOfUtc,
                    createdByUserId = "user", createdAtUtc = f.context.asOfUtc)))
                else -> detail.copy(hasCancellationSubmission = true, cancellationSubmissions = listOf(
                    FlightCancellationSubmission("cancel", employee, f.context.asOfUtc, isAccepted = true, createdAtUtc = f.context.asOfUtc)))
            } }
            // Legacy section responses preserve the old canEdit=true value until the cache projects the known work.
            assertTrue(acknowledged.canEditFlightDetails)
            repo.recordAcknowledged(acknowledged)
            f.work.clear()
            val restarted = f.repository().localFlight(f.id)!!
            assertFalse(kind, restarted.canEditFlightDetails)
            assertTrue(restarted.canSaveArrival && restarted.canSaveDeparture && restarted.canSaveServices && restarted.canSaveTasks)
            assertEquals("AAAAAAAAAAI=", restarted.rowVersion)
            assertTrue(runCatching { f.repository().save(restarted, f.input.copy(flightNumber = "CHANGED")) {} }.isFailure)
        }
    }

    private class Fixture {
        val firstOwner = "api|account"
        val owner = MutableStateFlow<String?>(firstOwner)
        val dao = MemoryAdHocDao()
        var online = true
        val id = "11111111-1111-1111-1111-111111111111"
        val station = "22222222-2222-2222-2222-222222222222"
        val staff = "33333333-3333-3333-3333-333333333333"
        val input = AdHocFlightInput("44444444-4444-4444-4444-444444444444", "N123AB", "2026-11-13T10:00:00Z", "2026-11-13T11:00:00Z")
        val v1 = "AAAAAAAAAAE="
        val context = AdHocFlightContext(station, "AAA", "Station", "Etc/UTC", staff, true, true, true, true, true,
            listOf("55555555-5555-5555-5555-555555555555"), "2026-11-13T10:00:00Z", "2026-11-12T22:00:00Z", "2026-11-13T22:00:00Z")
        var contextHandler: suspend () -> AdHocFlightContext = { context }
        val work = linkedMapOf<String, String>()
        var frozenWork = false
        var scheduled = 0
        val creates = mutableListOf<Pair<String, AdHocFlightInput>>()
        val updates = mutableListOf<Triple<String, AdHocFlightInput, String>>()
        val releases = mutableListOf<Triple<String, String, String>>()
        val planningAcks = mutableListOf<Triple<String, String, String>>()
        val heldReviews = mutableMapOf<String, String>()
        var createHandler: suspend (String, AdHocFlightInput) -> FlightDetail = { id, input -> ack(id, input, v1) }
        fun ack(id: String, input: AdHocFlightInput, version: String) = FlightDetail(id, input.flightNumber, input.customerId,
            customerName = "Customer", stationId = station, stationIata = "AAA", stationName = "Station",
            operationTypeId = ADHOC_OPERATION_TYPE_ID, operationTypeName = "AdHoc", scheduledArrivalUtc = input.scheduledArrivalUtc,
            scheduledDepartureUtc = input.scheduledDepartureUtc, isPerLanding = false, createdAtUtc = context.asOfUtc,
            rowVersion = version, stationTimeZoneId = context.stationTimeZoneId, currentStaffMemberId = staff,
            canSaveArrival = true, canSaveDeparture = true, canSaveServices = true, canSaveTasks = true,
            isAdHocFlight = true, canEditFlightDetails = true)
        private suspend fun transaction(block: suspend () -> Unit) {
            val rows = dao.pending.value.toList(); val contexts = dao.contexts.value.toList(); val oldWork = work.toMap()
            try { block() } catch (error: Throwable) {
                dao.pending.value = rows; dao.contexts.value = contexts; work.clear(); work.putAll(oldWork); throw error
            }
        }
        fun repository() = AdHocFlightsRepository(dao, owner, { owner.value }, { staff }, { "Employee" }, { online },
            { contextHandler() }, { id, input -> creates += id to input; createHandler(id, input) },
            { id, input, version -> updates += Triple(id, input, version); ack(id, input, "AAAAAAAAAAI=") },
            FlightOperationsGate(),
            commit = { identity, sections, after -> transaction { identity(); sections(); after() } },
            holdWork = { id -> check(!frozenWork); work[id] },
            releaseWork = { id, before, after -> work[id]?.let { check(it == before); work[id] = after; releases += Triple(id, before, after) } },
            removeWork = { work.remove(it); Unit }, hasWork = { it in work }, transaction = { transaction(it) },
            schedule = { scheduled++ }, now = { context.asOfUtc },
            onPlanningAcknowledged = { id, before, after -> planningAcks += Triple(id, before, after) },
            holdWorkForReview = { id, message -> heldReviews[id] = message })
    }
}

private class MemoryAdHocDao : AdHocFlightDao {
    val contexts = MutableStateFlow<List<AdHocFlightContextEntity>>(emptyList())
    val pending = MutableStateFlow<List<AdHocFlightPendingEntity>>(emptyList())
    override suspend fun context(owner: String) = contexts.value.firstOrNull { it.ownerSubject == owner }
    override fun observeContext(owner: String) = contexts.map { rows -> rows.firstOrNull { it.ownerSubject == owner } }
    override suspend fun putContext(row: AdHocFlightContextEntity) { contexts.value = contexts.value.filterNot { it.ownerSubject == row.ownerSubject } + row }
    override suspend fun deleteContext(owner: String) { contexts.value = contexts.value.filterNot { it.ownerSubject == owner } }
    override suspend fun rows(owner: String) = pending.value.filter { it.ownerSubject == owner }
    override fun observe(owner: String) = pending.map { rows -> rows.filter { it.ownerSubject == owner } }
    override suspend fun put(row: AdHocFlightPendingEntity) { pending.value = pending.value.filterNot { it.ownerSubject == row.ownerSubject && it.flightId == row.flightId } + row }
    override suspend fun delete(owner: String, id: String) { pending.value = pending.value.filterNot { it.ownerSubject == owner && it.flightId == id } }
    override suspend fun clearRows() { pending.value = emptyList() }
    override suspend fun clearContexts() { contexts.value = emptyList() }
}
