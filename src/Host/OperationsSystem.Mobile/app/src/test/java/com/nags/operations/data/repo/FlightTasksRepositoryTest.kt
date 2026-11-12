package com.nags.operations.data.repo

import com.nags.operations.data.*
import com.nags.operations.data.db.dao.FlightTaskDao
import com.nags.operations.data.db.entities.FlightTaskPendingEntity
import java.io.IOException
import java.time.Instant
import java.util.Base64
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.async
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import org.junit.Assert.*
import org.junit.Test

class FlightTasksRepositoryTest {
    private val owner = MutableStateFlow<String?>("api|account")
    private val dao = TaskMemoryDao()
    private val files = TaskMemoryFiles()
    private val now = Instant.parse("2026-11-10T12:00:00Z")
    private val record = "11111111-1111-1111-1111-111111111111"
    private val second = "22222222-2222-2222-2222-222222222222"
    private val staff = "33333333-3333-3333-3333-333333333333"
    private val performer = "55555555-5555-5555-5555-555555555555"
    private val v1 = "AAAAAAAAAAE="
    private val v2 = "AAAAAAAAAAI="
    private val v3 = "AAAAAAAAAAM="
    private var online = false
    private var fail: Exception? = null
    private var arrival = false
    private var departureAttempt = false
    private var siblingAttempt = false
    private var cancellation = false
    private var predecessor: String? = null
    private var beforeDelete: suspend (String, String, String) -> Unit = { _, _, _ -> }
    private var legacyFile: ByteArray? = null
    private var resourceTypes = FlightTaskResourceCatalogs()
    private var acknowledgedMedia = emptyMap<String, ByteArray>()
    private val input = SaveFlightTaskRequest(0, now.toString(), notes = "note",
        performers = listOf(SaveFlightTaskPerformerRequest(performer, staff, now.toString())))
    private val flight = FlightDetail("66666666-6666-6666-6666-666666666666", "SV100", "customer", "SV", "Airline", "station", "AAA",
        "Station", "operation", "Transit", scheduledArrivalUtc = now.toString(), scheduledDepartureUtc = now.plusSeconds(3600).toString(),
        isPerLanding = false, createdAtUtc = now.toString(), rowVersion = v1, stationTimeZoneId = "Asia/Riyadh",
        currentStaffMemberId = staff, canSaveTasks = true)
    private var live = flight
    private val sent = mutableListOf<Triple<String, SaveFlightTaskRequest?, String>>()
    private fun saved(id: String, value: SaveFlightTaskRequest, version: String = v2) = FlightTaskSaved(
        FlightTaskRecord(id, value.taskType, value.startUtc, value.endUtc, value.ataChapterId,
            customerReferenceNumber = value.customerReferenceNumber, notes = value.notes, createdByUserId = "user", createdAtUtc = now.toString(),
            performers = value.performers.map { FlightTaskPerformerRecord(it.id, it.staffMemberId, "Employee", "E1", it.startUtc, it.endUtc, it.notes) },
            canEdit = true, canSetEnd = true, canDelete = true, canChangeType = true, attachments = value.attachments.orEmpty().map {
                FlightServiceAttachmentRecord(it.id, it.kind, it.fileName, it.contentType,
                    it.content?.let { bytes -> Base64.getDecoder().decode(bytes).size.toLong() } ?: 6L)
            }), version)
    private var sender: suspend (String, String, SaveFlightTaskRequest, String) -> FlightTaskSaved = { _, id, value, version ->
        sent += Triple(id, value, version); fail?.let { throw it }; saved(id, value, if (version == v1) v2 else v3)
    }
    private fun repository() = FlightTasksRepository(dao, files, owner, { owner.value }, { online },
        sendSave = { a, b, c, d -> sender(a, b, c, d) }, sendDelete = { _, id, version ->
            sent += Triple(id, null, version); fail?.let { throw it }; FlightTaskDeleted(id, v3)
        }, acceptedSave = { _, result -> live = live.copy(rowVersion = result.rowVersion,
            tasks = live.tasks.filterNot { it.id == result.task.id } + result.task) },
        acceptedDelete = { _, result -> live = live.copy(rowVersion = result.rowVersion, tasks = live.tasks.filterNot { it.id == result.id }) },
        schedule = {}, json = Json, hasSavedArrival = { arrival }, acknowledgedPredecessorVersion = { _, reviewed ->
            if (reviewed == v1) predecessor ?: reviewed else reviewed
        }, fetchFlight = { live }, now = { now }, onAcknowledgedBeforeDelete = { a, b, c -> beforeDelete(a, b, c) },
        hasSavedCancellation = { cancellation }, onAttachmentsAcknowledged = { _, _, content -> acknowledgedMedia = content },
        hasUnacknowledgedDepartureAttempt = { departureAttempt }, hasUnacknowledgedSiblingWork = { siblingAttempt },
        legacyDraftAttachment = { _, _, _ -> legacyFile }, resourceCatalogs = { resourceTypes })

    @Test fun assignedFullCorrectionRetainsStableTaskCreatorAndIgnoresDisplayOnlyRefresh() = runBlocking {
        val closed = input.copy(endUtc = now.plusSeconds(60).toString(), performers = input.performers.map { it.copy(endUtc = now.toString()) })
        val baseline = saved(record, closed).task.copy(createdByUserId = "original-author", createdByDisplayName = "Original author",
            endedByUserId = "original-closer", canEdit = true, canSetEnd = true, canChangeType = true, canDelete = false)
        val reviewed = flight.copy(tasks = listOf(baseline))
        val desired = baseline.input().copy(taskType = 1, notes = "Assigned correction", endUtc = now.plusSeconds(90).toString())
        val repo = repository()
        repo.save(reviewed, record, desired)
        val queued = dao.rows(owner.value!!).single()
        assertEquals(baseline, Json.decodeFromString<FlightTaskRecord>(queued.reviewedTaskJson!!))
        try { repo.delete(reviewed, record); fail("Expected creator/closer deletion guard") } catch (_: IllegalStateException) { }
        assertEquals(queued, dao.rows(owner.value!!).single())
        live = reviewed.copy(tasks = listOf(baseline.copy(createdByDisplayName = "Updated account display name",
            endedByDisplayName = "Authoritative closer")))
        sender = { _, id, value, version ->
            sent += Triple(id, value, version)
            FlightTaskSaved(baseline.copy(taskType = value.taskType, notes = value.notes, endUtc = value.endUtc), v2)
        }
        online = true
        assertFalse(repo.syncPending())
        assertEquals(record, sent.single().first)
        assertEquals(1, sent.single().second!!.taskType)
        assertEquals("original-author", live.tasks.single().createdByUserId)
        assertEquals(baseline.createdAtUtc, live.tasks.single().createdAtUtc)
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun explicitDeleteCapabilityAllowsForeignClosedTaskWithoutCreatingAnotherOccurrence() = runBlocking {
        val closed = input.copy(endUtc = now.plusSeconds(60).toString(), performers = input.performers.map { it.copy(endUtc = now.toString()) })
        val original = saved(record, closed).task.copy(createdByUserId = "original-author", endedByUserId = "other-closer", canDelete = true)
        live = flight.copy(tasks = listOf(original))
        val repo = repository()
        repo.delete(live, record)
        assertEquals("delete", dao.rows(owner.value!!).single().operation)
        assertEquals(original, Json.decodeFromString<FlightTaskRecord>(dao.rows(owner.value!!).single().reviewedTaskJson!!))
        online = true
        assertFalse(repo.syncPending())
        assertEquals(listOf(Triple(record, null, v1)), sent)
        assertTrue(live.tasks.isEmpty())
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun deniedOrLostForeignTaskDeleteCapabilityNeverSendsFreshMutation() = runBlocking {
        val original = saved(record, input).task.copy(createdByUserId = "another-author", canDelete = false)
        val denied = flight.copy(tasks = listOf(original))
        val repo = repository()
        assertTrue(runCatching { repo.delete(denied, record) }.isFailure)
        assertTrue(dao.rows(owner.value!!).isEmpty())
        repo.delete(denied.copy(tasks = listOf(original.copy(canDelete = true))), record)
        live = denied
        online = true
        assertFalse(repo.syncPending())
        assertTrue(sent.isEmpty())
        assertEquals("conflict", dao.rows(owner.value!!).single().status)
        assertNull(dao.rows(owner.value!!).single().attemptOperation)
    }

    @Test fun offlinePendingEditsSurviveRepositoryRecreationAndSyncOnlyLatestValue() = runBlocking {
        repository().save(flight, record, input)
        val original = dao.rows(owner.value!!).single()
        repository().save(flight, record, input.copy(taskType = 1, notes = "latest offline edit"))
        val latest = dao.rows(owner.value!!).single()
        assertEquals(original.changeId, latest.changeId)
        online = true
        assertFalse(repository().syncPending())
        assertEquals(1, sent.size)
        assertEquals(1, sent.single().second!!.taskType)
        assertEquals("latest offline edit", sent.single().second!!.notes)
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun invalidClosureCannotReplacePreviouslyDurableOpenWork() = runBlocking {
        val repo = repository()
        repo.save(flight, record, input)
        val durable = dao.rows(owner.value!!).single()
        try { repo.save(flight, record, input.copy(endUtc = now.plusSeconds(60).toString())); fail("Expected open staff rejection") }
        catch (_: IllegalStateException) { }
        assertEquals(durable, dao.rows(owner.value!!).single())
        repo.save(flight, record, input.copy(endUtc = now.plusSeconds(60).toString(), performers = input.performers.map { it.copy(endUtc = now.toString()) }))
        assertNotNull(Json.decodeFromString<SaveFlightTaskRequest>(dao.rows(owner.value!!).single().inputJson!!).endUtc)
    }

    @Test fun freshClosedTaskRejectsOpenDurationResourcesBeforeSavingAnyIntent() = runBlocking {
        resourceTypes = FlightTaskResourceCatalogs(tools = mapOf(second to 1))
        val value = input.copy(endUtc = now.plusSeconds(60).toString(),
            performers = input.performers.map { it.copy(endUtc = now.toString()) },
            tools = listOf(SaveFlightTaskResourceRequest(second, second, startUtc = now.toString())))
        val repo = repository()
        try { repo.save(flight, record, value); fail("Expected duration End rejection") } catch (_: IllegalStateException) { }
        assertTrue(dao.rows(owner.value!!).isEmpty())
        repo.save(flight, record, value.copy(tools = value.tools.map { it.copy(endUtc = now.toString()) }))
        assertEquals(1, dao.rows(owner.value!!).size)
    }

    @Test fun frozenOlderClosedTaskWithOpenDurationReachesServerForExactAcknowledgment() = runBlocking {
        val value = input.copy(endUtc = now.plusSeconds(60).toString(),
            performers = input.performers.map { it.copy(endUtc = now.toString()) },
            tools = listOf(SaveFlightTaskResourceRequest(second, second, startUtc = now.toString()))).normalized()
        val frozen = Json.encodeToString(value)
        dao.put(FlightTaskPendingEntity(owner.value!!, flight.id, record, flight.flightNumber, "older-attempt", "save",
            frozen, v1, null, now.toString(), attemptOperation = "save", attemptInputJson = frozen,
            attemptRowVersion = v1, attemptAttachmentFilesJson = "{}"))
        online = true
        assertFalse(repository().syncPending())
        assertEquals(value, sent.single().second)
        assertEquals(v1, sent.single().third)
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun lostResponseReplaysFrozenInputThenSavesNewerDesiredCorrection() = runBlocking {
        val repo = repository()
        repo.save(flight, record, input)
        online = true; fail = IOException("Lost response")
        assertTrue(repo.syncPending())
        val frozen = dao.rows(owner.value!!).single()
        repo.save(flight, record, input.copy(notes = "newer desired"))
        assertEquals(frozen.attemptInputJson, dao.rows(owner.value!!).single().attemptInputJson)
        fail = null
        assertTrue(repo.syncPending())
        assertEquals(Json.decodeFromString<SaveFlightTaskRequest>(frozen.attemptInputJson!!), sent[1].second)
        assertEquals(v1, sent[1].third)
        assertFalse(repo.syncPending())
        assertEquals("newer desired", sent.last().second!!.notes)
        assertEquals(v2, sent.last().third)
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun freshTaskWaitsForUnknownSiblingAndDepartureAttemptsButFrozenReplayDoesNot() = runBlocking {
        val repo = repository()
        repo.save(flight, record, input)
        online = true; siblingAttempt = true
        assertFalse(repo.syncPending()); assertTrue(sent.isEmpty())
        siblingAttempt = false; departureAttempt = true
        assertFalse(repo.syncPending()); assertTrue(sent.isEmpty())
        departureAttempt = false; fail = IOException("Lost response")
        assertTrue(repo.syncPending()); assertEquals(1, sent.size)
        siblingAttempt = true; departureAttempt = true; fail = null
        assertFalse(repo.syncPending()); assertEquals(2, sent.size)
    }

    @Test fun ownAcknowledgmentCanAdvanceQueuedVersionButExternalChangesRequireReview() = runBlocking {
        val repo = repository()
        repo.save(flight, record, input)
        live = flight.copy(rowVersion = v2)
        online = true
        assertFalse(repo.syncPending())
        assertEquals("conflict", dao.rows(owner.value!!).single().status)
        assertTrue(sent.isEmpty())
        val fresh = repository()
        fresh.save(live, record, input.copy(notes = "reviewed"))
        assertFalse(fresh.syncPending())
        assertEquals(v2, sent.single().third)
    }

    @Test fun siblingOwnAcknowledgmentRequiresComparisonAndDoesNotAdoptAReadVersion() = runBlocking {
        val repo = repository()
        repo.save(flight, record, input)
        repo.onPriorWriteAcknowledged(flight.id, v1, v2)
        live = flight.copy(rowVersion = v3)
        online = true
        assertFalse(repo.syncPending())
        assertEquals("conflict", dao.rows(owner.value!!).single().status)
        assertEquals(v2, dao.rows(owner.value!!).single().rowVersion)
        assertTrue(sent.isEmpty())
    }

    @Test fun legacyDraftMediaImportsPrivatelyAndAcknowledgedBytesCacheBeforeCleanup() = runBlocking {
        val bytes = "%PDF-test".toByteArray()
        val attachment = FlightServiceAttachmentInput(second, 2, "document.pdf", "application/pdf")
        legacyFile = bytes
        val repo = repository()
        repo.save(flight, record, input.copy(attachments = listOf(attachment)))
        assertEquals(1, files.content.size)
        legacyFile = null
        online = true
        assertFalse(repository().syncPending())
        assertArrayEquals(bytes, Base64.getDecoder().decode(sent.single().second!!.attachments!!.single().content))
        assertArrayEquals(bytes, acknowledgedMedia[second])
        assertTrue(files.content.isEmpty())
    }

    @Test fun mediaReferencedByFrozenAttemptRemainsUntilExactAcknowledgment() = runBlocking {
        val first = FlightServiceAttachmentInput(second, 2, "first.pdf", "application/pdf", Base64.getEncoder().encodeToString("%PDF-first".toByteArray()))
        val replacement = first.copy(id = "77777777-7777-7777-7777-777777777777", fileName = "second.pdf", content = Base64.getEncoder().encodeToString("%PDF-second".toByteArray()))
        val repo = repository()
        repo.save(flight, record, input.copy(attachments = listOf(first)))
        online = true; fail = IOException("Lost response")
        assertTrue(repo.syncPending())
        repo.save(flight, record, input.copy(attachments = listOf(replacement)))
        assertEquals(2, files.content.size)
        fail = null
        assertTrue(repo.syncPending())
        assertEquals(1, files.content.size)
        assertFalse(repo.syncPending())
        assertTrue(files.content.isEmpty())
        assertEquals(first.id, sent[1].second!!.attachments!!.single().id)
        assertEquals(replacement.id, sent.last().second!!.attachments!!.single().id)
    }

    @Test fun omittedAttachmentListRetainsPendingFilesAndExplicitEmptyListRemovesThem() = runBlocking {
        val attachment = FlightServiceAttachmentInput(second, 2, "document.pdf", "application/pdf",
            Base64.getEncoder().encodeToString("%PDF-test".toByteArray()))
        val repo = repository()
        repo.save(flight, record, input.copy(attachments = listOf(attachment)))
        repo.save(flight, record, input.copy(notes = "corrected", attachments = null))
        val retained = Json.decodeFromString<SaveFlightTaskRequest>(dao.rows(owner.value!!).single().inputJson!!)
        assertEquals(second, retained.attachments!!.single().id)
        assertEquals(1, files.content.size)
        repo.save(flight, record, input.copy(attachments = emptyList()))
        assertTrue(Json.decodeFromString<SaveFlightTaskRequest>(dao.rows(owner.value!!).single().inputJson!!).attachments!!.isEmpty())
        assertTrue(files.content.isEmpty())
    }

    @Test fun invalidReplacementFileKeepsExistingPendingInputAndBytes() = runBlocking {
        val attachment = FlightServiceAttachmentInput(second, 2, "document.pdf", "application/pdf",
            Base64.getEncoder().encodeToString("%PDF-original".toByteArray()))
        val repo = repository()
        repo.save(flight, record, input.copy(attachments = listOf(attachment)))
        val original = dao.rows(owner.value!!).single()
        try {
            repo.save(flight, record, input.copy(attachments = listOf(attachment.copy(
                id = "77777777-7777-7777-7777-777777777777", content = Base64.getEncoder().encodeToString("not a PDF".toByteArray())))))
            fail("Expected byte validation")
        } catch (_: IllegalStateException) { }
        assertEquals(original, dao.rows(owner.value!!).single())
        assertEquals(1, files.content.size)
        assertArrayEquals("%PDF-original".toByteArray(), files.content.values.single())
    }

    @Test fun otherOperatorsEndProtectsRemovalWhileCreatorCanCorrectTypeAndOwnNotes() = runBlocking {
        val closed = input.copy(endUtc = now.plusSeconds(60).toString(), performers = input.performers.map { it.copy(endUtc = now.toString()) })
        val baseline = saved(record, closed).task.copy(endedByUserId = "other", canSetEnd = false, canChangeType = true, canDelete = false)
        val current = flight.copy(tasks = listOf(baseline))
        val repo = repository()
        try { repo.delete(current, record); fail("Expected delete protection") } catch (_: IllegalStateException) { }
        repo.save(current, record, baseline.input().copy(taskType = 1, notes = "my correction"))
        val desired = Json.decodeFromString<SaveFlightTaskRequest>(dao.rows(owner.value!!).single().inputJson!!)
        assertEquals(1, desired.taskType)
        assertEquals("my correction", desired.notes)
    }

    @Test fun otherOperatorsCanOnlySetAllowedTaskEnd() = runBlocking {
        val baseline = saved(record, input.copy(performers = input.performers.map { it.copy(endUtc = now.toString()) })).task.copy(canEdit = false, canChangeType = false, canDelete = false)
        val current = flight.copy(tasks = listOf(baseline))
        val repo = repository()
        try { repo.save(current, record, baseline.input().copy(notes = "other person's notes")); fail("Expected core protection") } catch (_: IllegalStateException) { }
        repo.save(current, record, baseline.input().copy(endUtc = now.plusSeconds(60).toString()))
        assertEquals(1, dao.rows(owner.value!!).size)
    }

    @Test fun explicitlyReviewedUnchangedReadonlyTaskCanRecoverByExactAcknowledgment() = runBlocking {
        val closed = input.copy(endUtc = now.plusSeconds(60).toString(), performers = input.performers.map { it.copy(endUtc = now.toString()) })
        val recordValue = saved(record, closed).task.copy(createdByUserId = "someone-else", endedByUserId = "closer",
            canEdit = false, canSetEnd = false, canDelete = false, canChangeType = false)
        live = flight.copy(tasks = listOf(recordValue))
        sender = { _, id, value, version -> sent += Triple(id, value, version); FlightTaskSaved(recordValue, v2) }
        val repo = repository()
        repo.save(live, record, recordValue.input())
        online = true
        assertFalse(repo.syncPending())
        assertEquals(recordValue.input(), sent.single().second)
        assertTrue(dao.rows(owner.value!!).isEmpty())
        try { repo.save(live, record, recordValue.input().copy(notes = "unauthorized")); fail("Expected read-only details") }
        catch (_: IllegalStateException) { }
    }

    @Test fun localUnsentTaskCanBeRemovedWithoutADeleteRequestAndAmbiguousAttemptCannotBeDiscarded() = runBlocking {
        val repo = repository()
        repo.save(flight, record, input)
        repo.delete(flight, record)
        assertTrue(dao.rows(owner.value!!).isEmpty())
        repo.save(flight, record, input)
        online = true; fail = IOException("Unknown result")
        assertTrue(repo.syncPending())
        try { repo.discard(flight.id, record); fail("Expected unknown attempt protection") } catch (_: IllegalStateException) { }
        assertEquals(1, dao.rows(owner.value!!).size)
    }

    @Test fun pauseWaitsForInFlightSendAndClearsDurableInputOnlyAfterItSettles() = runBlocking {
        val started = CompletableDeferred<Unit>(); val release = CompletableDeferred<Unit>()
        sender = { _, id, value, _ -> started.complete(Unit); release.await(); saved(id, value) }
        val repo = repository(); repo.save(flight, record, input); online = true
        val sync = async { repo.syncPending() }; started.await()
        val pause = async { repo.pause() }
        release.complete(Unit); sync.await(); pause.await()
        repo.clear(); assertTrue(dao.rows(owner.value!!).isEmpty()); assertTrue(files.content.isEmpty())
    }
}

private class TaskMemoryDao : FlightTaskDao {
    private val state = MutableStateFlow<List<FlightTaskPendingEntity>>(emptyList())
    override fun observe(owner: String): Flow<List<FlightTaskPendingEntity>> = state.map { rows -> rows.filter { it.ownerSubject == owner } }
    override suspend fun rows(owner: String) = state.value.filter { it.ownerSubject == owner }.sortedBy { it.savedAtUtc }
    override suspend fun allRows() = state.value
    override suspend fun put(row: FlightTaskPendingEntity) { state.value = state.value.filterNot {
        it.ownerSubject == row.ownerSubject && it.flightId == row.flightId && it.taskRecordId == row.taskRecordId
    } + row }
    override suspend fun delete(owner: String, flightId: String, recordId: String, changeId: String) { state.value = state.value.filterNot {
        it.ownerSubject == owner && it.flightId == flightId && it.taskRecordId == recordId && it.changeId == changeId
    } }
    override suspend fun deleteForFlight(owner: String, flightId: String) { state.value = state.value.filterNot { it.ownerSubject == owner && it.flightId == flightId } }
    override suspend fun clear() { state.value = emptyList() }
}
private class TaskMemoryFiles : TaskAttachmentStore {
    val content = linkedMapOf<String, ByteArray>()
    private var sequence = 0
    override suspend fun save(content: ByteArray): String = "file-${++sequence}".also { this.content[it] = content.copyOf() }
    override suspend fun import(uri: String): String = throw IOException("Source unavailable")
    override suspend fun read(key: String): ByteArray = content[key]?.copyOf() ?: throw IOException("File missing")
    override suspend fun retainOnly(referenced: Set<String>) { content.keys.retainAll(referenced) }
}
