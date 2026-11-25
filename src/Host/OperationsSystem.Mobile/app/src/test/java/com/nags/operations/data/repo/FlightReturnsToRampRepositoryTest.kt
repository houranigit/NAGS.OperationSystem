package com.nags.operations.data.repo

import com.nags.operations.data.*
import com.nags.operations.data.db.dao.FlightReturnToRampDao
import com.nags.operations.data.db.entities.FlightReturnToRampPendingEntity
import java.io.IOException
import java.time.Instant
import java.util.Base64
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import org.junit.Assert.*
import org.junit.Test

class FlightReturnsToRampRepositoryTest {
    private val at = Instant.parse("2026-11-15T12:00:00Z")
    private val owner = MutableStateFlow<String?>("api|11111111-1111-1111-1111-111111111111")
    private val employee = AssignedEmployee("22222222-2222-2222-2222-222222222222", "Employee", "E1")
    private val id = "33333333-3333-3333-3333-333333333333"
    private val flight = FlightDetail("44444444-4444-4444-4444-444444444444", "SV100", "customer", "SV", "Airline", "station", "JED",
        "Station", "operation", "Transit", scheduledArrivalUtc = at.minusSeconds(3600).toString(), scheduledDepartureUtc = at.toString(),
        isPerLanding = false, createdAtUtc = at.toString(), rowVersion = "AAAAAAAAAAE=", stationTimeZoneId = "Asia/Riyadh",
        currentStaffMemberId = employee.staffMemberId, canSaveReturnsToRamp = true, approvalStatus = "Approved",
        departureSubmissions = listOf(FlightDepartureSubmission("departure", employee, at.toString(), employee,
            isAccepted = true, hasSignature = false, createdAtUtc = at.toString())))
    private val input = SaveFlightReturnToRampRequest(FlightReturnToRampArrivalInput(at.plusSeconds(60).toString(), " arrival "))
    private val preview = FlightReturnToRampRecord(id, 1, input.arrival.arrivedAtUtc, employee.staffMemberId,
        employee.fullName, createdByUserId = "", createdAtUtc = at.toString(), canEdit = true)
    private val dao = RtrMemoryDao()
    private val files = RtrMemoryFiles()
    private var online = false
    private var blocked = false
    private var failure: Exception? = null
    private var current = flight
    private val attempts = mutableListOf<Pair<SaveFlightReturnToRampRequest, String>>()
    private fun repo() = FlightReturnsToRampRepository(dao, files, owner, { owner.value }, { online },
        send = { _, _, request, version ->
            attempts += request to version
            failure?.let { throw it }
            flight.copy(rowVersion = "AAAAAAAAAAI=", approvalStatus = "PendingRtr",
                returnToRamps = listOf(preview.copy(createdByUserId = owner.value!!.substringAfter('|'))))
        }, accepted = { _, _ -> }, schedule = {}, json = Json,
        fetchFlight = { current }, hasBlockingPriorWork = { blocked }, now = { at })

    @Test fun offlineOccurrenceSurvivesRepositoryRestartAndProjectsSavedSummary() = runBlocking {
        repo().save(flight, id, input, preview)
        val row = dao.rows(owner.value!!).single()
        assertEquals("arrival", Json.decodeFromString<SaveFlightReturnToRampRequest>(row.inputJson).arrival.remarks)
        val projected = repo().project(flight).returnToRamps.single()
        assertEquals("pending", projected.localStatus)
        assertEquals(owner.value!!.substringAfter('|'), projected.createdByUserId)
        online = true
        assertFalse(repo().syncPending())
        assertEquals(1, attempts.size)
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun savedDepartureAllowsNewReturnWithoutFlightApproval() = runBlocking {
        val repository = repo()
        for (status in listOf("NotApproved", "Unlocked")) {
            repository.save(flight.copy(approvalStatus = status), id, input, preview)
            assertEquals(id, dao.rows(owner.value!!).single().occurrenceId)
            repository.discard(flight.id, id)
        }
    }

    @Test fun prerequisitesAndPermissionFailuresNeverPublishLocalInput() = runBlocking {
        for (invalid in listOf(flight.copy(departureSubmissions = emptyList()), flight.copy(canSaveReturnsToRamp = false),
            flight.copy(hasCancellationSubmission = true), flight.copy(returnToRamps = listOf(preview.copy(id = "another-open"))))) {
            assertTrue(runCatching { repo().save(invalid, id, input, preview) }.isFailure)
        }
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun firstReturnMustStartAfterTheLatestRecordedDeparture() = runBlocking {
        val otherEmployee = AssignedEmployee("55555555-5555-5555-5555-555555555555", "Colleague", "E2")
        val laterDeparture = flight.departureSubmissions.single().copy(id = "later", submittedByEmployee = otherEmployee,
            isAccepted = false, atd = "2026-11-15T15:01:00+03:00")
        val conflictingFlight = flight.copy(departureSubmissions = flight.departureSubmissions + laterDeparture)
        val repository = repo()
        for (invalid in listOf(input.arrival.copy(arrivedAtUtc = at.toString()), input.arrival)) {
            val failure = runCatching { repository.save(conflictingFlight, id, input.copy(arrival = invalid), preview) }.exceptionOrNull()
            assertEquals(ReturnToRampTimelineIssue.ArrivalAfterFlightDeparture.message, failure?.message)
        }
        assertTrue(dao.rows(owner.value!!).isEmpty())

        val after = at.plusSeconds(61).toString()
        repository.save(conflictingFlight, id, input.copy(arrival = input.arrival.copy(arrivedAtUtc = after)), preview.copy(arrivedAtUtc = after))
        assertEquals(id, dao.rows(owner.value!!).single().occurrenceId)
    }

    @Test fun editsCheckBothSidesOfTheTimelineAndUnchangedHistoricalTimesRemainEditable() {
        val previous = preview.copy(sequence = 1, departedAtUtc = at.plusSeconds(90).toString(), isOpen = false)
        val middle = preview.copy(id = "middle", sequence = 2, arrivedAtUtc = at.plusSeconds(100).toString(),
            departedAtUtc = at.plusSeconds(120).toString(), isOpen = false)
        val next = preview.copy(id = "next", sequence = 3, arrivedAtUtc = at.plusSeconds(130).toString())
        val timeline = flight.copy(returnToRamps = listOf(next, previous, middle))
        val invalid = middle.input().copy(arrival = FlightReturnToRampArrivalInput(previous.departedAtUtc!!),
            departure = FlightReturnToRampDepartureInput(next.arrivedAtUtc))
        assertEquals(setOf(ReturnToRampTimelineIssue.ArrivalAfterPreviousDeparture, ReturnToRampTimelineIssue.DepartureBeforeNextArrival),
            timeline.returnToRampTimelineIssues(middle.id, invalid))
        assertEquals(setOf(ReturnToRampTimelineIssue.DepartureBeforeNextArrival),
            timeline.returnToRampTimelineIssues(middle.id, middle.input().copy(departure = null)))

        val recordedOverlap = middle.copy(departedAtUtc = next.arrivedAtUtc)
        val historic = timeline.copy(returnToRamps = listOf(previous, recordedOverlap, next))
        assertTrue(historic.returnToRampTimelineIssues(recordedOverlap.id, recordedOverlap.input().copy(
            arrival = recordedOverlap.input().arrival.copy(remarks = "Corrected remarks"))).isEmpty())
    }

    @Test fun normalDepartureCorrectionMustRemainBeforeFirstReturn() {
        val timeline = flight.copy(returnToRamps = listOf(preview))
        assertTrue(timeline.departureOverlapsFirstReturn(SaveFlightDepartureRequest(preview.arrivedAtUtc)))
        assertFalse(timeline.departureOverlapsFirstReturn(SaveFlightDepartureRequest(at.plusSeconds(1).toString())))

        val historic = timeline.copy(departureSubmissions = timeline.departureSubmissions.map { it.copy(atd = preview.arrivedAtUtc) })
        assertFalse(historic.departureOverlapsFirstReturn(SaveFlightDepartureRequest(preview.arrivedAtUtc, remarks = "Corrected remarks")))
    }

    @Test fun aNewReturnChecksTheDepartureOfAPendingPreviousReturn() = runBlocking {
        val repository = repo()
        val departure = at.plusSeconds(90).toString()
        val first = preview.copy(isOpen = false, departedAtUtc = departure, services = listOf(
            FlightServiceRecord("66666666-6666-6666-6666-666666666666", "55555555-5555-5555-5555-555555555555", "Transit",
                preview.arrivedAtUtc, endUtc = departure, createdByUserId = owner.value!!.substringAfter('|'),
                createdAtUtc = at.toString(), performers = listOf(FlightServicePerformerRecord(
                    "88888888-8888-8888-8888-888888888888", employee.staffMemberId, employee.fullName, employee.employeeId,
                    preview.arrivedAtUtc, departure)))))
        repository.save(flight, id, first.input(), first)
        val second = preview.copy(id = "99999999-9999-9999-9999-999999999999", sequence = 2, arrivedAtUtc = departure)

        val rejected = runCatching { repository.save(flight, second.id, second.input(), second) }.exceptionOrNull()
        assertEquals(ReturnToRampTimelineIssue.ArrivalAfterPreviousDeparture.message, rejected?.message)
        assertEquals(listOf(id), dao.rows(owner.value!!).map { it.occurrenceId })
    }

    @Test fun pendingReturnCanBeEditedAndAnotherCanStartAfterItCloses() = runBlocking {
        val prior = preview.copy(sequence = 1, departedAtUtc = at.plusSeconds(70).toString(), isOpen = false)
        val pending = preview.copy(id = "55555555-5555-5555-5555-555555555555", sequence = 2, isOpen = false,
            arrivedAtUtc = at.plusSeconds(80).toString(), departedAtUtc = at.plusSeconds(90).toString())
        val reviewFlight = flight.copy(approvalStatus = "PendingRtr", approvedReturnToRampSequence = 1,
            returnToRamps = listOf(prior, pending))
        assertFalse(reviewFlight.canEditReturnToRamp(prior))
        assertTrue(reviewFlight.canEditReturnToRamp(pending))
        assertFalse(reviewFlight.canAddReturnToRamp(listOf(prior, pending.copy(isOpen = true))))
        assertTrue(runCatching { repo().save(reviewFlight, id, input, prior) }.isFailure)
        val newId = "66666666-6666-6666-6666-666666666666"
        val afterReturns = at.plusSeconds(100).toString()
        repo().save(reviewFlight, newId, input.copy(arrival = input.arrival.copy(arrivedAtUtc = afterReturns)),
            preview.copy(id = newId, sequence = 3, arrivedAtUtc = afterReturns))
        assertEquals(newId, dao.rows(owner.value!!).single().occurrenceId)
    }

    @Test fun unknownAttemptReplaysExactlyAfterUserEditsAndProcessRestart() = runBlocking {
        val repository = repo()
        repository.save(flight, id, input, preview)
        online = true; failure = IOException("response lost")
        assertTrue(repository.syncPending())
        repository.save(flight, id, input.copy(arrival = input.arrival.copy(remarks = "later")), preview)
        failure = null
        val restarted = repo()
        restarted.syncPending()
        assertEquals(attempts[0], attempts[1])
        assertEquals("later", restarted.pendingInput(flight.id, id)!!.arrival.remarks)
        restarted.syncPending()
        assertEquals("later", attempts.last().first.arrival.remarks)
        assertEquals("AAAAAAAAAAI=", attempts.last().second)
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun blockedNormalWorkWaitsAndOnlyItsKnownAcknowledgmentAdvancesVersion() = runBlocking {
        val repository = repo()
        repository.save(flight, id, input, preview)
        online = true; blocked = true
        assertTrue(repository.syncPending())
        assertTrue(attempts.isEmpty())
        repository.onPriorWriteAcknowledged(flight.id, "AAAAAAAAAAE=", "AAAAAAAAAAI=")
        blocked = false; current = flight.copy(rowVersion = "AAAAAAAAAAI=")
        repository.syncPending()
        assertEquals("AAAAAAAAAAI=", attempts.single().second)
    }

    @Test fun unknownOutcomeCannotBeDiscardedAndForeignVersionIsNotAdopted() = runBlocking {
        val repository = repo()
        repository.save(flight, id, input, preview)
        online = true; failure = IOException("lost response")
        repository.syncPending()
        assertTrue(runCatching { repository.discard(flight.id, id) }.isFailure)
        repository.onPriorWriteAcknowledged(flight.id, "AAAAAAAAAAE=", "AAAAAAAAAAM=")
        assertEquals("AAAAAAAAAAE=", dao.rows(owner.value!!).single().attemptRowVersion)
        assertEquals("AAAAAAAAAAE=", dao.rows(owner.value!!).single().rowVersion)
    }

    @Test fun staleReviewCannotReadReplaceOrDiscardNewerSavedInput() = runBlocking {
        val repository = repo()
        repository.save(flight, id, input, preview)
        val staleId = dao.rows(owner.value!!).single().changeId
        repository.save(flight, id, input.copy(arrival = input.arrival.copy(remarks = "Newer saved input")), preview)
        val retained = dao.rows(owner.value!!).single()
        assertTrue(runCatching { repository.pendingInput(flight.id, id, staleId) }.isFailure)
        assertTrue(runCatching { repository.saveReviewed(flight, id, staleId, input, preview) }.isFailure)
        assertTrue(runCatching { repository.discardReviewed(flight.id, id, staleId) }.isFailure)
        assertEquals(retained, dao.rows(owner.value!!).single())
        assertEquals("Newer saved input", repository.pendingInput(flight.id, id, retained.changeId)!!.arrival.remarks)
    }

    @Test fun unknownOutcomeReviewCannotReplaceOrDiscardTheFrozenAttempt() = runBlocking {
        val repository = repo()
        repository.save(flight, id, input, preview)
        online = true; failure = IOException("lost response")
        repository.syncPending()
        val retained = dao.rows(owner.value!!).single()
        assertTrue(runCatching { repository.saveReviewed(flight, id, retained.changeId,
            input.copy(arrival = input.arrival.copy(remarks = "Review replacement")), preview) }.isFailure)
        assertTrue(runCatching { repository.discardReviewed(flight.id, id, retained.changeId) }.isFailure)
        assertEquals(retained, dao.rows(owner.value!!).single())
        assertNotNull(retained.attemptInputJson)
    }

    @Test fun reviewedSaveUsesTheComparedOnlineVersionAndBaseline() = runBlocking {
        val before = preview.copy(arrivalRemarks = "Before")
        val repository = repo()
        repository.save(flight.copy(returnToRamps = listOf(before)), id, input, preview)
        val row = dao.rows(owner.value!!).single().copy(status = "conflict", error = "Changed online")
        dao.put(row)
        val recorded = before.copy(arrivedAtUtc = at.plusSeconds(30).toString(), arrivalRemarks = "Colleague")
        val reviewed = flight.copy(rowVersion = "AAAAAAAAAAM=", returnToRamps = listOf(recorded))
        val staged = mergeReturnToRampReview(recorded, repository.pendingInput(flight.id, id, row.changeId)!!, before)
        repository.saveReviewed(reviewed, id, row.changeId, staged, preview.copy(arrivedAtUtc = recorded.arrivedAtUtc))
        val saved = dao.rows(owner.value!!).single()
        assertEquals("pending", saved.status)
        assertNull(saved.error)
        assertNotEquals(row.changeId, saved.changeId)
        assertEquals(reviewed.rowVersion, saved.rowVersion)
        assertEquals(recorded, Json.decodeFromString<FlightReturnToRampRecord>(saved.reviewedOccurrenceJson!!))
        assertEquals("arrival", repository.pendingInput(flight.id, id, saved.changeId)!!.arrival.remarks)
        assertEquals(recorded.arrivedAtUtc, repository.pendingInput(flight.id, id, saved.changeId)!!.arrival.arrivedAtUtc)
    }

    @Test fun failedReviewValidationRetainsItsOriginalQueueRow() = runBlocking {
        val repository = repo()
        repository.save(flight, id, input, preview)
        val retained = dao.rows(owner.value!!).single().copy(status = "failed", error = "Invalid online")
        dao.put(retained)
        assertTrue(runCatching { repository.saveReviewed(flight, id, retained.changeId,
            input.copy(arrival = input.arrival.copy(arrivedAtUtc = "invalid")), preview) }.isFailure)
        assertEquals(retained, dao.rows(owner.value!!).single())
    }

    @Test fun explicitlyReviewedUnsentInputUsesTheComparedVersionInsteadOfItsOldQueueVersion() = runBlocking {
        val repository = repo()
        repository.save(flight, id, input, preview)
        val pending = dao.rows(owner.value!!).single()
        val reviewed = flight.copy(rowVersion = "AAAAAAAAAAM=")
        repository.saveReviewed(reviewed, id, pending.changeId, input, preview)
        assertEquals(reviewed.rowVersion, dao.rows(owner.value!!).single().rowVersion)
    }

    @Test fun reviewedDiscardRemovesOnlyItsExactOccurrence() = runBlocking {
        val repository = repo()
        repository.save(flight, id, input, preview)
        val first = dao.rows(owner.value!!).single()
        val secondId = "99999999-9999-9999-9999-999999999999"
        val second = first.copy(occurrenceId = secondId, changeId = "other-change", previewJson = Json.encodeToString(preview.copy(id = secondId, sequence = 2)))
        dao.put(second)
        repository.discardReviewed(flight.id, id, first.changeId)
        assertEquals(listOf(second), dao.rows(owner.value!!))
    }

    @Test fun predecessorAcknowledgedBeforeTheRtrWasStagedStillUsesOnlyKnownVersionLineage() = runBlocking {
        val repository = repo()
        repository.onPriorWriteAcknowledged(flight.id, "AAAAAAAAAAE=", "AAAAAAAAAAI=")
        current = flight.copy(rowVersion = "AAAAAAAAAAI=")
        repository.save(flight, id, input, preview)
        assertEquals("AAAAAAAAAAI=", dao.rows(owner.value!!).single().rowVersion)
        online = true
        repository.syncPending()
        assertEquals("AAAAAAAAAAI=", attempts.single().second)
    }

    @Test fun changedTargetAfterOwnPredecessorRequiresReviewWithoutSending() = runBlocking {
        val existing = preview.copy(createdByUserId = owner.value!!.substringAfter('|'))
        val baseline = flight.copy(returnToRamps = listOf(existing))
        val repository = repo()
        repository.save(baseline, id, input, preview)
        repository.onPriorWriteAcknowledged(flight.id, "AAAAAAAAAAE=", "AAAAAAAAAAI=")
        online = true; current = baseline.copy(rowVersion = "AAAAAAAAAAI=", returnToRamps = listOf(existing.copy(arrivalRemarks = "external change")))
        repository.syncPending()
        assertTrue(attempts.isEmpty())
        assertEquals("conflict", dao.rows(owner.value!!).single().status)
    }

    @Test fun attachmentBytesStayOutOfRoomAndSurviveFailedUpload() = runBlocking {
        val serviceId = "55555555-5555-5555-5555-555555555555"
        val recordId = "66666666-6666-6666-6666-666666666666"
        val attachmentId = "77777777-7777-7777-7777-777777777777"
        val bytes = ByteArray(256_000) { (it % 200).toByte() }.also { byteArrayOf(137.toByte(), 80, 78, 71).copyInto(it) }
        val request = input.copy(services = listOf(FlightReturnToRampServiceInput(recordId, SaveFlightServiceRequest(serviceId,
            input.arrival.arrivedAtUtc, performers = listOf(SaveFlightServicePerformerRequest("88888888-8888-8888-8888-888888888888", employee.staffMemberId, input.arrival.arrivedAtUtc)),
            attachments = listOf(FlightServiceAttachmentInput(attachmentId, 0, "sample.png", "image/png", Base64.getEncoder().encodeToString(bytes)))))))
        val repository = repo()
        repository.save(flight, id, request, preview)
        assertTrue(dao.rows(owner.value!!).single().inputJson.length < 3000)
        online = true; failure = IOException("lost response")
        repository.syncPending()
        assertTrue(dao.rows(owner.value!!).single().attemptInputJson!!.length < 3000)
        assertArrayEquals(bytes, Base64.getDecoder().decode(repo().pendingInput(flight.id, id)!!.services.single().input.attachments!!.single().content))
    }

    @Test fun occurrenceSequenceOrdersUploadsAndAcknowledgmentAdvancesTheNextOccurrence() = runBlocking {
        val secondId = "99999999-9999-9999-9999-999999999999"
        val service = FlightServiceRecord("66666666-6666-6666-6666-666666666666", "55555555-5555-5555-5555-555555555555", "Transit",
            input.arrival.arrivedAtUtc, endUtc = at.plusSeconds(80).toString(), createdByUserId = owner.value!!.substringAfter('|'),
            createdAtUtc = at.toString(), performers = listOf(FlightServicePerformerRecord("88888888-8888-8888-8888-888888888888",
                employee.staffMemberId, employee.fullName, employee.employeeId, input.arrival.arrivedAtUtc, at.plusSeconds(80).toString())))
        val first = preview.copy(isOpen = false, departedAtUtc = at.plusSeconds(90).toString(), services = listOf(service))
        val projected = flight.copy(returnToRamps = listOf(first))
        val repository = FlightReturnsToRampRepository(dao, files, owner, { owner.value }, { online },
            send = { _, occurrence, request, version ->
                attempts += request to version
                current = current.copy(rowVersion = if (occurrence == id) "AAAAAAAAAAI=" else "AAAAAAAAAAM=",
                    returnToRamps = if (occurrence == id) listOf(first) else listOf(first, preview.copy(id = secondId,
                        sequence = 2, arrivedAtUtc = request.arrival.arrivedAtUtc)))
                current
            }, accepted = { _, _ -> }, schedule = {}, json = Json, fetchFlight = { current },
            hasBlockingPriorWork = { false }, now = { at })
        val secondArrival = at.plusSeconds(100).toString()
        repository.save(projected, secondId, input.copy(arrival = input.arrival.copy(arrivedAtUtc = secondArrival, remarks = "second")),
            preview.copy(id = secondId, sequence = 2, arrivedAtUtc = secondArrival))
        repository.save(projected, id, first.input().copy(arrival = input.arrival.copy(remarks = "first")), first)
        current = projected
        online = true
        repository.syncPending()
        assertEquals(listOf("first", "second"), attempts.map { it.first.arrival.remarks })
        assertEquals(listOf("AAAAAAAAAAE=", "AAAAAAAAAAI="), attempts.map { it.second })
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }
}

private class RtrMemoryDao : FlightReturnToRampDao {
    private val state = MutableStateFlow<List<FlightReturnToRampPendingEntity>>(emptyList())
    override fun observe(owner: String): Flow<List<FlightReturnToRampPendingEntity>> = state.map { rows -> rows.filter { it.ownerSubject == owner } }
    override suspend fun rows(owner: String) = state.value.filter { it.ownerSubject == owner }
    override suspend fun allRows() = state.value
    override suspend fun put(row: FlightReturnToRampPendingEntity) {
        state.value = state.value.filterNot { it.ownerSubject == row.ownerSubject && it.flightId == row.flightId && it.occurrenceId == row.occurrenceId } + row
    }
    override suspend fun delete(owner: String, flightId: String, id: String, changeId: String) {
        state.value = state.value.filterNot { it.ownerSubject == owner && it.flightId == flightId && it.occurrenceId == id && it.changeId == changeId }
    }
    override suspend fun clear() { state.value = emptyList() }
}
private class RtrMemoryFiles : ServiceAttachmentStore {
    private val values = mutableMapOf<String, ByteArray>()
    override suspend fun save(content: ByteArray): String = "file-${values.size}".also { values[it] = content.copyOf() }
    override suspend fun import(uri: String): String = error("not used")
    override suspend fun read(key: String) = values[key]?.copyOf() ?: throw IOException("missing")
    override suspend fun retainOnly(referenced: Set<String>) { values.keys.retainAll(referenced) }
}
