package com.nags.operations.data.repo

import com.nags.operations.data.*
import com.nags.operations.data.db.dao.*
import com.nags.operations.data.db.entities.*
import java.io.File
import java.io.IOException
import java.time.Instant
import java.util.Base64
import java.util.UUID
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import org.junit.Assert.*
import org.junit.Test

class FlightOperationsBatchesRepositoryTest {
    @Test fun planningHoldKeepsMemberStatusPendingAndOnlyAnExactAcknowledgmentReleasesItsVersion() = runBlocking {
        val f = Fixture()
        val repository = f.repository()
        repository.save {
            f.store.arrivals.put(f.arrival())
            f.store.services.put(f.service())
            f.store.tasks.put(f.task())
        }
        assertEquals(f.v1, repository.holdForPlanning(f.flightId))
        repository.adoptLegacyPending()
        assertEquals("waiting-creation", f.store.batchRows.single().status)
        assertTrue(f.store.arrivalRows.all { it.status == "pending" })
        assertTrue(f.store.serviceRows.all { it.status == "pending" })
        assertTrue(f.store.taskRows.all { it.status == "pending" })
        f.online = true
        assertFalse(repository.syncPending())
        assertTrue(f.sent.isEmpty())
        val before = f.store.snapshot()
        assertTrue(runCatching { repository.releaseAfterPlanning(f.flightId, f.v2, f.v3) }.isFailure)
        assertEquals(before, f.store.snapshot())
        repository.releaseAfterPlanning(f.flightId, f.v1, f.v2)
        val released = f.store.batchRows.single()
        assertEquals("pending", released.status)
        assertEquals(f.v2, released.rowVersion)
        assertTrue(released.needsComparison)
        assertNull(released.attemptInputJson)
        assertTrue(f.store.arrivalRows.all { it.rowVersion == f.v2 && it.status == "pending" })
        assertTrue(f.store.serviceRows.all { it.rowVersion == f.v2 && it.status == "pending" })
        assertTrue(f.store.taskRows.all { it.rowVersion == f.v2 && it.status == "pending" })
    }

    @Test fun planningCannotHoldReleaseOrDiscardAnUncertainWholeSave() = runBlocking {
        val f = Fixture()
        val repository = f.repository()
        repository.save { f.store.arrivals.put(f.arrival()); f.store.services.put(f.service()) }
        f.online = true
        f.loseNextResponse = true
        assertTrue(repository.syncPending())
        assertNotNull(f.store.batchRows.single().attemptInputJson)
        val before = f.store.snapshot()
        assertTrue(runCatching { repository.holdForPlanning(f.flightId) }.isFailure)
        assertEquals(before, f.store.snapshot())
        assertTrue(runCatching { repository.releaseAfterPlanning(f.flightId, f.v1, f.v2) }.isFailure)
        assertEquals(before, f.store.snapshot())
        assertTrue(runCatching { repository.removeUnsentPlanningWork(f.flightId) }.isFailure)
        assertEquals(before, f.store.snapshot())
        assertEquals(1, f.sent.size)
    }

    @Test fun lateSectionFailureRollsBackEveryQueueAndFormDraftAndPreservesPreviouslyOwnedFiles() = runBlocking {
        val f = Fixture()
        val repository = f.repository()
        f.files["old-file"] = "%PDF-old".toByteArray()
        val original = f.service(files = mapOf(f.attachmentId to "old-file"))
        repository.save { f.store.services.put(original) }
        val oldDraft = FlightServiceFormDraftEntity(f.owner.value!!, f.flightId, "original draft", "{}", f.time)
        f.store.services.putDraft(oldDraft)
        val before = f.store.snapshot()
        f.scheduleCalls = 0

        try {
            repository.save {
                f.store.tasks.put(f.task())
                f.store.arrivals.put(f.arrival())
                f.store.departures.put(f.departure())
                f.store.services.putDraft(oldDraft.copy(formJson = "must roll back"))
                f.files["new-invalid-file"] = "invalid".toByteArray()
                throw IOException("The later service attachment cannot be imported")
            }
            fail("Expected the complete local Save to fail")
        } catch (_: IOException) { }

        assertEquals(before, f.store.snapshot())
        assertEquals(setOf("old-file"), f.files.keys)
        assertEquals(listOf(true, false, true, false), f.cleanupDeferrals)
        assertFalse(f.cleanupDeferred)
        assertEquals(0, f.scheduleCalls)
        assertTrue(f.sent.isEmpty())
    }

    @Test fun lateMediaHydrationFailureCannotPublishEarlierTaskOrReplaceOldDraft() = runBlocking {
        val f = Fixture()
        val draft = FlightServiceFormDraftEntity(f.owner.value!!, f.flightId, "kept draft", "{}", f.time)
        f.store.services.putDraft(draft)
        val before = f.store.snapshot()
        f.serviceHydrationError = IOException("The selected service file is unavailable")

        try {
            f.repository().save {
                f.store.tasks.put(f.task())
                f.store.services.put(f.service())
                f.store.services.putDraft(draft.copy(formJson = "temporary draft"))
            }
            fail("Expected media preparation failure")
        } catch (_: IOException) { }

        assertEquals(before, f.store.snapshot())
        assertEquals(0, f.scheduleCalls)
        assertTrue(f.sent.isEmpty())
    }

    @Test fun hundredMiBLimitIncludesEveryServiceTaskAndDepartureSignatureBeforeAnyLocalCommit() = runBlocking {
        val f = Fixture()
        val pdf = ByteArray(1024 * 1024)
        "%PDF-".toByteArray().copyInto(pdf)
        f.files["shared-pdf"] = pdf
        f.files["signature"] = f.png
        val before = f.store.snapshot()
        try {
            f.repository().save {
                repeat(50) {
                    f.store.services.put(f.service(id = UUID.randomUUID().toString(), files = mapOf(UUID.randomUUID().toString() to "shared-pdf")))
                    f.store.tasks.put(f.task(id = UUID.randomUUID().toString(), files = mapOf(UUID.randomUUID().toString() to "shared-pdf")))
                }
                f.store.departures.put(f.departure(signatureFile = "signature"))
            }
            fail("100 MiB of work attachments plus a signature must exceed the whole-Save limit")
        } catch (error: IllegalStateException) {
            assertTrue(error.message.orEmpty().contains("100 MB"))
        }
        assertEquals(100L * 1024 * 1024, FlightOperationsBatchLimits.MAX_INLINE_BYTES)
        assertEquals(before, f.store.snapshot())
        assertTrue(f.files.isEmpty())
        assertEquals(0, f.scheduleCalls)
        assertTrue(f.sent.isEmpty())
    }

    @Test fun allSectionsSurviveCoordinatorRecreationAndOnlyOneWholeHttpRequestAcknowledgesThem() = runBlocking {
        val f = Fixture()
        f.files["signature"] = f.png
        f.repository().save {
            f.store.arrivals.put(f.arrival())
            f.store.departures.put(f.departure(signatureFile = "signature"))
            f.store.services.put(f.service())
            f.store.tasks.put(f.task())
        }
        val repository = f.repository()
        f.online = true
        val unused = UnusedMediaStore()
        var individualSends = 0
        val arrivals = FlightArrivalsRepository(f.store.arrivals, f.owner, { f.owner.value }, { true },
            send = { _, _, _ -> individualSends++; error("A grouped arrival cannot drain separately") },
            accepted = { _, _ -> }, schedule = {}, json = f.json, now = { Instant.parse(f.time) },
            grouped = { id, record -> repository.contains("arrival", id, record) })
        val departures = FlightDeparturesRepository(f.store.departures, unused, f.owner, { f.owner.value }, { true },
            send = { _, _, _ -> individualSends++; error("A grouped departure cannot drain separately") },
            accepted = { _, _ -> }, schedule = {}, json = f.json, hasSavedArrival = { false },
            acknowledgedArrivalVersion = { _, version -> version }, fetchFlight = { f.live },
            grouped = { id, record -> repository.contains("departure", id, record) })
        val services = FlightServicesRepository(f.store.services, unused, f.owner, { f.owner.value }, { true },
            sendSave = { _, _, _, _ -> individualSends++; error("A grouped service cannot drain separately") },
            sendDelete = { _, _, _ -> individualSends++; error("A grouped service cannot delete separately") },
            acceptedSave = { _, _ -> }, acceptedDelete = { _, _ -> }, schedule = {}, json = f.json,
            hasSavedArrival = { false }, acknowledgedPredecessorVersion = { _, version -> version }, fetchFlight = { f.live },
            grouped = { id, record -> repository.contains("service", id, record) })
        val tasks = FlightTasksRepository(f.store.tasks, unused, f.owner, { f.owner.value }, { true },
            sendSave = { _, _, _, _ -> individualSends++; error("A grouped task cannot drain separately") },
            sendDelete = { _, _, _ -> individualSends++; error("A grouped task cannot delete separately") },
            acceptedSave = { _, _ -> }, acceptedDelete = { _, _ -> }, schedule = {}, json = f.json,
            hasSavedArrival = { false }, acknowledgedPredecessorVersion = { _, version -> version }, fetchFlight = { f.live },
            grouped = { id, record -> repository.contains("task", id, record) })

        arrivals.syncPending(); departures.syncPending(); services.syncPending(); tasks.syncPending()
        assertEquals(0, individualSends)
        assertEquals(1, f.store.arrivalRows.size)
        assertEquals(1, f.store.departureRows.size)
        assertEquals(1, f.store.serviceRows.size)
        assertEquals(1, f.store.taskRows.size)
        assertFalse(repository.syncPending())
        assertEquals(1, f.sent.size)
        val request = f.sent.single().second
        assertNotNull(request.arrival)
        assertNotNull(request.departure)
        assertEquals(1, request.serviceChanges.size)
        assertEquals(1, request.taskChanges.size)
        assertEquals(1, f.accepted.size)
        assertTrue(f.store.batchRows.isEmpty())
        assertTrue(f.store.arrivalRows.isEmpty())
        assertTrue(f.store.departureRows.isEmpty())
        assertTrue(f.store.serviceRows.isEmpty())
        assertTrue(f.store.taskRows.isEmpty())
    }

    @Test fun lostWholeResponseThenDeletingNewServiceAndTaskRetainsFrozenAttemptUntilAcknowledged() = runBlocking {
        val f = Fixture()
        val repository = f.repository()
        val reviewed = f.live
        repository.save { f.store.services.put(f.service()); f.store.tasks.put(f.task()) }
        f.online = true
        f.loseNextResponse = true
        assertTrue(repository.syncPending())
        val frozen = f.store.batchRows.single().attemptInputJson
        val original = f.sent.single()
        assertNotNull(frozen)
        try {
            repository.discard("service", f.flightId, f.serviceId)
            fail("Unknown whole-Save outcomes cannot be discarded")
        } catch (_: IllegalStateException) { }
        f.serviceRepository(repository).delete(reviewed, f.serviceId)
        f.taskRepository(repository).delete(reviewed, f.taskId)
        val desired = f.decode(f.store.batchRows.single().inputJson)
        assertEquals("delete", desired.services.single().operation)
        assertEquals("delete", desired.tasks.single().operation)
        assertEquals(frozen, f.store.batchRows.single().attemptInputJson)
        assertTrue(repository.syncPending())
        assertEquals(original, f.sent[1])
        assertNull(f.store.batchRows.single().attemptInputJson)
        assertEquals(f.v2, f.store.batchRows.single().rowVersion)
        assertFalse(repository.syncPending())
        assertEquals(3, f.sent.size)
        assertNull(f.sent.last().second.serviceChanges.single().input)
        assertNull(f.sent.last().second.taskChanges.single().input)
        assertEquals(f.v2, f.sent.last().third)
        assertTrue(f.store.batchRows.isEmpty())
        assertTrue(f.live.services.isEmpty())
        assertTrue(f.live.tasks.isEmpty())
    }

    @Test fun newerDesiredInputReusingAcknowledgedFileIdsRetainsFrozenBytesThenOmitsReupload() = runBlocking {
        val f = Fixture()
        f.files["service-file"] = "%PDF-service".toByteArray()
        f.files["task-file"] = "%PDF-task".toByteArray()
        val service = f.service(files = mapOf(f.attachmentId to "service-file"))
        val task = f.task(files = mapOf(f.taskAttachmentId to "task-file"))
        val repository = f.repository()
        repository.save { f.store.services.put(service); f.store.tasks.put(task) }
        f.online = true
        f.loseNextResponse = true
        assertTrue(repository.syncPending())
        val frozen = f.store.batchRows.single().attemptInputJson
        repository.save {
            f.store.services.put(service.copy(inputJson = f.json.encodeToString(f.serviceInput(service).copy(notes = "new desired service"))))
            f.store.tasks.put(task.copy(inputJson = f.json.encodeToString(f.taskInput(task).copy(notes = "new desired task"))))
        }
        assertEquals(frozen, f.store.batchRows.single().attemptInputJson)
        assertEquals(setOf("service-file"), repository.fileReferences("service"))
        assertEquals(setOf("task-file"), repository.fileReferences("task"))
        assertTrue(repository.syncPending())
        assertEquals(f.sent.first(), f.sent[1])
        assertTrue(f.files.isEmpty())
        assertFalse(repository.syncPending())
        assertEquals(3, f.sent.size)
        assertEquals("new desired service", f.sent.last().second.serviceChanges.single().input!!.notes)
        assertEquals("new desired task", f.sent.last().second.taskChanges.single().input!!.notes)
        assertNull(f.sent.last().second.serviceChanges.single().input!!.attachments!!.single().content)
        assertNull(f.sent.last().second.taskChanges.single().input!!.attachments!!.single().content)
    }

    @Test fun staleCachedReadCannotSilentlyRebaseTheWholeRequestOrAcknowledgeAnyActual() = runBlocking {
        val f = Fixture()
        val repository = f.repository()
        repository.save { f.store.arrivals.put(f.arrival()); f.store.services.put(f.service()); f.store.tasks.put(f.task()) }
        f.live = f.live.copy(rowVersion = f.v2)
        f.online = true
        assertFalse(repository.syncPending())
        assertEquals(1, f.sent.size)
        assertEquals(f.v1, f.sent.single().third)
        assertTrue(f.accepted.isEmpty())
        assertEquals("conflict", f.store.batchRows.single().status)
        assertEquals("conflict", f.store.arrivalRows.single().status)
        assertEquals("conflict", f.store.serviceRows.single().status)
        assertEquals("conflict", f.store.taskRows.single().status)
        assertEquals(f.v1, f.store.batchRows.single().rowVersion)
        assertNotNull(f.store.batchRows.single().attemptInputJson)
        assertTrue(repository.discard("task", f.flightId, f.taskId))
        assertTrue(f.store.batchRows.isEmpty())
        assertTrue(f.store.arrivalRows.isEmpty())
        assertTrue(f.store.serviceRows.isEmpty())
        assertTrue(f.store.taskRows.isEmpty())
    }

    @Test fun missingMediaBeforeTheFirstSendDoesNotCreateAnUnknownAttemptOrPreventWholeDiscard() = runBlocking {
        val f = Fixture()
        val repository = f.repository()
        f.files["service-file"] = "%PDF-service".toByteArray()
        repository.save {
            f.store.arrivals.put(f.arrival())
            f.store.services.put(f.service(files = mapOf(f.attachmentId to "service-file")))
            f.store.tasks.put(f.task())
        }
        f.removeFile("service-file")
        f.online = true
        assertTrue(repository.syncPending())
        assertTrue(f.sent.isEmpty())
        assertTrue(f.accepted.isEmpty())
        assertNull(f.store.batchRows.single().attemptInputJson)
        assertNull(f.store.batchRows.single().attemptRowVersion)
        assertTrue(repository.discard("service", f.flightId, f.serviceId))
        assertTrue(f.store.batchRows.isEmpty())
        assertTrue(f.store.arrivalRows.isEmpty())
        assertTrue(f.store.serviceRows.isEmpty())
        assertTrue(f.store.taskRows.isEmpty())
    }

    @Test fun localHydrationFailureAfterLostWholeResponseNeverAuthorizesDiscardingTheUnknownAttempt() = runBlocking {
        val f = Fixture()
        val repository = f.repository()
        repository.save { f.store.arrivals.put(f.arrival()); f.store.services.put(f.service()); f.store.tasks.put(f.task()) }
        f.online = true
        f.loseNextResponse = true
        assertTrue(repository.syncPending())
        val frozen = f.store.batchRows.single().attemptInputJson
        f.serviceHydrationError = IllegalStateException("The local private file cannot currently be read")
        assertFalse(repository.syncPending())
        assertEquals(1, f.sent.size)
        assertNotNull(f.sent.single().second.arrival)
        assertTrue(f.accepted.isEmpty())
        assertEquals(frozen, f.store.batchRows.single().attemptInputJson)
        assertEquals("failed", f.store.batchRows.single().status)
        assertEquals(1, f.store.arrivalRows.size)
        assertEquals(1, f.store.serviceRows.size)
        assertEquals(1, f.store.taskRows.size)
        try {
            repository.discard("service", f.flightId, f.serviceId)
            fail("A local preparation failure cannot prove the earlier HTTP attempt was rejected")
        } catch (_: IllegalStateException) { }
        assertEquals(frozen, f.store.batchRows.single().attemptInputJson)
    }

    @Test fun failedLocalAcceptanceAfterServerSuccessKeepsTheWholeAttemptReplayable() = runBlocking {
        val f = Fixture()
        val repository = f.repository()
        repository.save { f.store.arrivals.put(f.arrival()); f.store.tasks.put(f.task()) }
        f.online = true
        f.acceptanceFailure = IllegalStateException("Cache write failed after the whole response")
        assertTrue(repository.syncPending())
        val frozen = f.store.batchRows.single().attemptInputJson
        assertNotNull(frozen)
        assertEquals("pending", f.store.batchRows.single().status)
        assertEquals(1, f.store.arrivalRows.size)
        assertEquals(1, f.store.taskRows.size)
        f.acceptanceFailure = null
        assertFalse(repository.syncPending())
        assertEquals(f.sent.first(), f.sent.last())
        assertTrue(f.store.batchRows.isEmpty())
        assertTrue(f.store.arrivalRows.isEmpty())
        assertTrue(f.store.taskRows.isEmpty())
    }

    @Test fun legacyOwnAcknowledgmentPersistsEveryDesiredMemberVersionAcrossCoordinatorRecreation() = runBlocking {
        val f = Fixture()
        val legacy = f.service(id = UUID.randomUUID().toString()).copy(attemptOperation = "save",
            attemptInputJson = f.json.encodeToString(f.baseServiceInput), attemptRowVersion = f.v1)
        f.store.services.put(legacy)
        val repository = f.repository()
        repository.save { f.store.services.put(f.service()); f.store.tasks.put(f.task()) }
        repository.onPriorWriteAcknowledged(f.flightId, f.v1, f.v2)
        assertEquals(legacy, f.store.serviceRows.single { it.serviceRecordId == legacy.serviceRecordId })
        val persisted = f.decode(f.store.batchRows.single().inputJson)
        assertEquals(listOf(f.v2, f.v2), persisted.versions())
        assertEquals(f.v2, f.store.batchRows.single().rowVersion)
        f.store.services.put(f.store.serviceRows.single { it.serviceRecordId == f.serviceId }.copy(rowVersion = f.v2))
        f.store.tasks.put(f.store.taskRows.single().copy(rowVersion = f.v2))
        f.live = f.live.copy(rowVersion = f.v3)
        val task = f.store.taskRows.single()
        val recreated = f.repository()
        recreated.save {
            f.store.tasks.put(task.copy(rowVersion = f.v2,
                inputJson = f.json.encodeToString(f.taskInput(task).copy(notes = "edit after restart"))))
        }
        val edited = f.decode(f.store.batchRows.single().inputJson)
        assertEquals(f.serviceId, edited.services.single().serviceRecordId)
        assertEquals("edit after restart", f.taskInput(edited.tasks.single()).notes)
        assertEquals(listOf(f.v2, f.v2), edited.versions())
        assertEquals(f.v2, f.store.batchRows.single().rowVersion)
        assertEquals(legacy.attemptInputJson, f.store.serviceRows.single { it.serviceRecordId == legacy.serviceRecordId }.attemptInputJson)
        assertTrue(f.sent.isEmpty())
    }

    @Test fun legacyAcknowledgmentNeverChangesAnAlreadyFrozenWholeAttempt() = runBlocking {
        val f = Fixture()
        val repository = f.repository()
        repository.save { f.store.services.put(f.service()); f.store.tasks.put(f.task()) }
        f.online = true
        f.loseNextResponse = true
        assertTrue(repository.syncPending())
        val frozen = f.store.batchRows.single()
        repository.onPriorWriteAcknowledged(f.flightId, f.v1, f.v3)
        assertEquals(frozen, f.store.batchRows.single())
    }

    @Test fun standaloneServiceAndTaskCorrectionsUpdateTheOwningUnsentBatch() = runBlocking {
        exerciseStandaloneCorrections(frozen = false)
    }

    @Test fun standaloneCorrectionsAfterLostWholeResponseReplayOldAttemptBeforePublishingLatestValues() = runBlocking {
        exerciseStandaloneCorrections(frozen = true)
    }

    private suspend fun exerciseStandaloneCorrections(frozen: Boolean) {
        val f = Fixture()
        val coordinator = f.repository()
        val service = f.service()
        val task = f.task()
        val reviewed = f.live
        coordinator.save { f.store.services.put(service); f.store.tasks.put(task) }
        if (frozen) {
            f.online = true
            f.loseNextResponse = true
            assertTrue(coordinator.syncPending())
        }
        val attempt = f.store.batchRows.single().attemptInputJson
        val services = f.serviceRepository(coordinator)
        val tasks = f.taskRepository(coordinator)
        services.save(reviewed, f.serviceId, f.serviceInput(service).copy(notes = "standalone service correction"))
        tasks.save(reviewed, f.taskId, f.taskInput(task).copy(notes = "standalone task correction"))
        val desired = f.decode(f.store.batchRows.single().inputJson)
        assertEquals("standalone service correction", f.serviceInput(desired.services.single()).notes)
        assertEquals("standalone task correction", f.taskInput(desired.tasks.single()).notes)
        assertEquals(attempt, f.store.batchRows.single().attemptInputJson)
        f.online = true
        if (frozen) {
            assertTrue(coordinator.syncPending())
            assertEquals(f.sent.first(), f.sent[1])
        }
        assertFalse(coordinator.syncPending())
        assertEquals(if (frozen) 3 else 1, f.sent.size)
        val latest = f.sent.last().second
        assertEquals("standalone service correction", latest.serviceChanges.single().input!!.notes)
        assertEquals("standalone task correction", latest.taskChanges.single().input!!.notes)
        assertEquals("standalone service correction", f.live.services.single().notes)
        assertEquals("standalone task correction", f.live.tasks.single().notes)
        assertTrue(f.store.batchRows.isEmpty())
        assertTrue(f.store.serviceRows.isEmpty())
        assertTrue(f.store.taskRows.isEmpty())
    }

    @Test fun nestedPublicSectionSavesPublishOneCompleteGroupAndUseOneOuterTransaction() = runBlocking {
        val f = Fixture()
        val coordinator = f.repository()
        val services = f.serviceRepository(coordinator)
        val tasks = f.taskRepository(coordinator)
        val service = f.service()
        val task = f.task()
        coordinator.save {
            services.save(f.live, f.serviceId, f.serviceInput(service))
            coordinator.save { tasks.save(f.live, f.taskId, f.taskInput(task)) }
        }
        assertEquals(1, f.store.transactionCalls)
        assertEquals(listOf(true, false), f.cleanupDeferrals)
        assertEquals(1, f.scheduleCalls)
        val desired = f.decode(f.store.batchRows.single().inputJson)
        assertEquals(f.serviceId, desired.services.single().serviceRecordId)
        assertEquals(f.taskId, desired.tasks.single().taskRecordId)
        f.online = true
        assertFalse(coordinator.syncPending())
        assertEquals(1, f.sent.size)
        assertEquals(1, f.sent.single().second.serviceChanges.size)
        assertEquals(1, f.sent.single().second.taskChanges.size)
    }

    @Test fun upgradeAdoptsAllFourUnsentSectionRowsBeforeAnyIndividualDrain() = runBlocking {
        val f = Fixture()
        f.files["signature"] = f.png
        f.store.arrivals.put(f.arrival())
        f.store.departures.put(f.departure(signatureFile = "signature"))
        f.store.services.put(f.service())
        f.store.tasks.put(f.task())
        val coordinator = f.repository()
        coordinator.adoptLegacyPending()
        assertEquals(1, f.store.batchRows.size)
        assertTrue(coordinator.contains("arrival", f.flightId))
        assertTrue(coordinator.contains("departure", f.flightId))
        assertTrue(coordinator.contains("service", f.flightId, f.serviceId))
        assertTrue(coordinator.contains("task", f.flightId, f.taskId))
        assertTrue(coordinator.blocksFreshDrain(f.flightId))
        f.online = true
        f.serviceRepository(coordinator).syncPending()
        f.taskRepository(coordinator).syncPending()
        assertNull(f.store.serviceRows.single().attemptOperation)
        assertNull(f.store.taskRows.single().attemptOperation)
        assertEquals("pending", f.store.serviceRows.single().status)
        assertEquals("pending", f.store.taskRows.single().status)
        assertFalse(coordinator.syncPending())
        assertEquals(1, f.sent.size)
        assertNotNull(f.sent.single().second.arrival)
        assertNotNull(f.sent.single().second.departure)
        assertEquals(1, f.sent.single().second.serviceChanges.size)
        assertEquals(1, f.sent.single().second.taskChanges.size)
        assertTrue(f.store.batchRows.isEmpty())
        assertTrue(f.store.arrivalRows.isEmpty())
        assertTrue(f.store.departureRows.isEmpty())
        assertTrue(f.store.serviceRows.isEmpty())
        assertTrue(f.store.taskRows.isEmpty())
    }

    @Test fun upgradeMixedReviewedVersionsStayHeldEvenAfterRetryWithoutFreezingOrSending() = runBlocking {
        assertUpgradeHeld(mixedVersions = true)
    }

    @Test fun upgradeMissingMediaHoldsTheWholeFlightAndAllowsSafeWholeDiscardBeforeAnySend() = runBlocking {
        assertUpgradeHeld(mixedVersions = false)
    }

    private suspend fun assertUpgradeHeld(mixedVersions: Boolean) {
        val f = Fixture()
        f.files["signature"] = f.png
        val service = if (mixedVersions) f.service().copy(rowVersion = f.v2)
            else f.service(files = mapOf(f.attachmentId to "missing-private-file"))
        f.store.arrivals.put(f.arrival())
        f.store.departures.put(f.departure(signatureFile = "signature"))
        f.store.services.put(service)
        f.store.tasks.put(f.task())
        val coordinator = f.repository()
        coordinator.adoptLegacyPending()
        assertEquals(if (mixedVersions) "conflict" else "failed", f.store.batchRows.single().status)
        assertTrue(coordinator.contains("arrival", f.flightId))
        assertTrue(coordinator.contains("departure", f.flightId))
        assertTrue(coordinator.contains("service", f.flightId, f.serviceId))
        assertTrue(coordinator.contains("task", f.flightId, f.taskId))
        assertEquals(service.inputJson, f.store.serviceRows.single().inputJson)
        f.online = true
        f.serviceRepository(coordinator).syncPending()
        f.taskRepository(coordinator).syncPending()
        assertFalse(coordinator.syncPending())
        assertTrue(f.sent.isEmpty())
        assertTrue(f.accepted.isEmpty())
        assertNull(f.store.batchRows.single().attemptInputJson)
        if (mixedVersions) {
            assertTrue(coordinator.retry("task", f.flightId, f.taskId))
            assertFalse(coordinator.syncPending())
            assertEquals("conflict", f.store.batchRows.single().status)
            assertNull(f.store.batchRows.single().attemptInputJson)
            assertTrue(f.sent.isEmpty())
        } else {
            assertTrue(coordinator.discard("service", f.flightId, f.serviceId))
            assertTrue(f.store.batchRows.isEmpty())
            assertTrue(f.store.arrivalRows.isEmpty())
            assertTrue(f.store.departureRows.isEmpty())
            assertTrue(f.store.serviceRows.isEmpty())
            assertTrue(f.store.taskRows.isEmpty())
        }
    }

    @Test fun upgradeLeavesFrozenLegacyRequestExactThenAdoptsItsNewerDesiredValueAfterTheKnownAcknowledgment() = runBlocking {
        val f = Fixture()
        val original = f.service()
        val originalInput = f.serviceInput(original)
        val legacy = original.copy(inputJson = f.json.encodeToString(originalInput.copy(notes = "newer unsent legacy correction")),
            attemptOperation = "save", attemptInputJson = original.inputJson, attemptRowVersion = f.v1)
        f.store.services.put(legacy)
        f.store.tasks.put(f.task())
        val coordinator = f.repository()
        coordinator.adoptLegacyPending()
        assertEquals(legacy, f.store.serviceRows.single())
        assertFalse(coordinator.contains("service", f.flightId, f.serviceId))
        assertTrue(coordinator.contains("task", f.flightId, f.taskId))
        f.online = true
        assertTrue(coordinator.syncPending())
        assertTrue(f.sent.isEmpty())
        val legacySent = mutableListOf<Pair<SaveFlightServiceRequest, String>>()
        var legacyFetches = 0
        val repository = FlightServicesRepository(f.store.services, UnusedMediaStore(), f.owner, { f.owner.value }, { true },
            sendSave = { _, id, input, version ->
                legacySent += input to version
                f.applyServerRequest(SaveFlightOperationsBatchRequest(serviceChanges = listOf(FlightServiceBatchChange(id, input))), f.v2)
                FlightServiceSaved(f.live.services.single(), f.v2, 0, 1)
            }, sendDelete = { _, _, _ -> error("The original legacy request is a save") },
            acceptedSave = { id, result -> coordinator.legacyServiceAccepted(id, result) }, acceptedDelete = { _, _ -> },
            schedule = {}, json = f.json, hasSavedArrival = { false }, acknowledgedPredecessorVersion = { _, version -> version },
            fetchFlight = { legacyFetches++; f.live }, now = { Instant.parse(f.time) },
            grouped = { id, record -> coordinator.blocksFreshDrain(id) || coordinator.contains("service", id, record) },
            onAcknowledgedBeforeDelete = { id, before, after -> coordinator.onPriorWriteAcknowledged(id, before, after) })
        repository.syncPending()
        assertEquals(listOf(originalInput.normalized() to f.v1), legacySent)
        assertEquals(0, legacyFetches)
        coordinator.adoptLegacyPending()
        assertTrue(coordinator.contains("service", f.flightId, f.serviceId))
        assertFalse(coordinator.syncPending())
        assertEquals(1, f.sent.size)
        assertEquals(f.v2, f.sent.single().third)
        assertEquals("newer unsent legacy correction", f.sent.single().second.serviceChanges.single().input!!.notes)
        assertEquals(1, f.sent.single().second.taskChanges.size)
        assertEquals("newer unsent legacy correction", f.live.services.single().notes)
        assertTrue(f.store.batchRows.isEmpty())
        assertTrue(f.store.serviceRows.isEmpty())
        assertTrue(f.store.taskRows.isEmpty())
    }

    @Test fun frozenWholeBatchDefersUpgradeSiblingAndPreservesItsReviewedBaselineThroughTheOriginalAcknowledgment() = runBlocking {
        val f = Fixture()
        val oldTaskRow = f.task()
        val oldTaskInput = f.taskInput(oldTaskRow)
        f.applyServerRequest(SaveFlightOperationsBatchRequest(taskChanges = listOf(FlightTaskBatchChange(f.taskId, oldTaskInput))), f.v1)
        val oldReviewedTask = f.live.tasks.single()
        val sibling = oldTaskRow.copy(inputJson = f.json.encodeToString(oldTaskInput.copy(notes = "unsent sibling correction")),
            reviewedTaskJson = f.json.encodeToString(oldReviewedTask))
        val coordinator = f.repository()
        coordinator.save { f.store.services.put(f.service()) }
        f.online = true
        f.loseNextResponse = true
        assertTrue(coordinator.syncPending())
        val frozen = f.store.batchRows.single()
        f.store.tasks.put(sibling)
        coordinator.adoptLegacyPending()
        assertEquals(frozen, f.store.batchRows.single())
        assertEquals(sibling, f.store.taskRows.single())
        assertFalse(coordinator.contains("task", f.flightId, f.taskId))
        assertTrue(coordinator.blocksFreshDrain(f.flightId))
        f.taskRepository(coordinator).syncPending()
        assertNull(f.store.taskRows.single().attemptOperation)
        f.applyServerRequest(SaveFlightOperationsBatchRequest(taskChanges = listOf(FlightTaskBatchChange(f.taskId,
            oldTaskInput.copy(notes = "another employee's later correction")))), f.v3)
        assertFalse(coordinator.syncPending())
        assertEquals(f.sent.first(), f.sent[1])
        assertEquals(sibling, f.store.taskRows.single())
        // Model the persisted, known-own-ACK lineage used by the section repository:
        // its version advances, while the earlier reviewed record must still be compared.
        f.store.tasks.put(sibling.copy(rowVersion = f.v3, needsTaskComparison = true))
        coordinator.adoptLegacyPending()
        assertTrue(coordinator.contains("task", f.flightId, f.taskId))
        assertFalse(coordinator.syncPending())
        assertEquals(2, f.sent.size)
        assertNotEquals("pending", f.store.batchRows.single().status)
        assertEquals(sibling.reviewedTaskJson, f.store.taskRows.single().reviewedTaskJson)
        assertEquals("unsent sibling correction", f.taskInput(f.store.taskRows.single()).notes)
        assertEquals("another employee's later correction", f.live.tasks.single().notes)
    }

    @Test fun acknowledgmentOfFrozenServiceCannotAdoptAColleaguesNewTaskBaselineForAnUnattemptedDesiredMember() = runBlocking {
        val f = Fixture()
        val originalTaskRow = f.task()
        val originalTask = f.taskInput(originalTaskRow)
        f.applyServerRequest(SaveFlightOperationsBatchRequest(taskChanges = listOf(FlightTaskBatchChange(f.taskId, originalTask))), f.v1)
        val originalReviewed = f.json.encodeToString(f.live.tasks.single())
        val coordinator = f.repository()
        coordinator.save { f.store.services.put(f.service()) }
        f.online = true
        f.loseNextResponse = true
        assertTrue(coordinator.syncPending())
        val frozen = f.store.batchRows.single().attemptInputJson
        f.files["unattempted-task-file"] = "%PDF-unattempted".toByteArray()
        val unattempted = f.task(files = mapOf(f.taskAttachmentId to "unattempted-task-file")).let { row ->
            row.copy(reviewedTaskJson = originalReviewed,
                inputJson = f.json.encodeToString(f.taskInput(row).copy(notes = "local correction reviewed before the colleague's write")))
        }
        coordinator.save { f.store.tasks.put(unattempted) }
        assertEquals(frozen, f.store.batchRows.single().attemptInputJson)
        f.applyServerRequest(SaveFlightOperationsBatchRequest(taskChanges = listOf(FlightTaskBatchChange(f.taskId,
            originalTask.copy(notes = "colleague changed the task while Service ACK was unknown")))), f.v3)

        assertTrue(coordinator.syncPending())
        assertEquals(f.sent.first(), f.sent[1])
        assertEquals(originalReviewed, f.store.taskRows.single().reviewedTaskJson)
        assertTrue(f.store.taskRows.single().needsTaskComparison)
        assertEquals(setOf("unattempted-task-file"), coordinator.fileReferences("task"))
        assertTrue(f.files.containsKey("unattempted-task-file"))
        assertFalse(coordinator.syncPending())
        assertEquals(2, f.sent.size)
        assertNotEquals("pending", f.store.batchRows.single().status)
        assertEquals(originalReviewed, f.store.taskRows.single().reviewedTaskJson)
        assertEquals("local correction reviewed before the colleague's write", f.taskInput(f.store.taskRows.single()).notes)
        assertEquals("colleague changed the task while Service ACK was unknown", f.live.tasks.single().notes)
    }

    @Test fun definitiveLegacyHttpRejectionReleasesOnlyItsAttemptProofSoTheHeldWholeGroupCanBeDiscarded() = runBlocking {
        for (statusCode in listOf(400, 403)) {
            val f = Fixture()
            val original = f.service()
            val legacy = original.copy(attemptOperation = "save", attemptInputJson = original.inputJson,
                attemptRowVersion = f.v1, attemptAttachmentFilesJson = "{}")
            f.store.services.put(legacy)
            f.store.tasks.put(f.task())
            val coordinator = f.repository()
            coordinator.adoptLegacyPending()
            f.online = true
            var sent = 0
            var rejected = 0
            val repository = FlightServicesRepository(f.store.services, UnusedMediaStore(), f.owner, { f.owner.value }, { true },
                sendSave = { _, _, input, version ->
                    sent++
                    assertEquals(f.serviceInput(original).normalized(), input)
                    assertEquals(f.v1, version)
                    throw ApiException(statusCode, "The server definitively rejected the legacy request")
                }, sendDelete = { _, _, _ -> error("Not a delete") }, acceptedSave = { _, _ -> }, acceptedDelete = { _, _ -> },
                schedule = {}, json = f.json, hasSavedArrival = { false }, acknowledgedPredecessorVersion = { _, version -> version },
                fetchFlight = { error("An already frozen legacy request must not be fetched/rebased") },
                grouped = { id, record -> coordinator.blocksFreshDrain(id) || coordinator.contains("service", id, record) },
                onAttemptRejected = { id, record -> rejected++; coordinator.legacyRejected("service", id, record) })
            repository.syncPending()
            assertEquals(1, sent)
            assertEquals(1, rejected)
            val retained = f.store.serviceRows.single()
            assertEquals("failed", retained.status)
            assertEquals(original.inputJson, retained.inputJson)
            assertNull(retained.attemptOperation)
            assertNull(retained.attemptInputJson)
            assertNull(retained.attemptRowVersion)
            assertNull(retained.attemptAttachmentFilesJson)
            coordinator.adoptLegacyPending()
            assertEquals("failed", f.store.batchRows.single().status)
            assertTrue(coordinator.contains("service", f.flightId, f.serviceId))
            assertTrue(coordinator.contains("task", f.flightId, f.taskId))
            assertTrue(coordinator.discard("task", f.flightId, f.taskId))
            assertTrue(f.store.batchRows.isEmpty())
            assertTrue(f.store.serviceRows.isEmpty())
            assertTrue(f.store.taskRows.isEmpty())
            assertTrue(f.sent.isEmpty())
            assertTrue(f.accepted.isEmpty())
        }
    }

    @Test fun failedLocalHydrationOfFrozenLegacyMediaKeepsUnknownProofAndBlocksWholeGroupDiscard() = runBlocking {
        val f = Fixture()
        val original = f.service(files = mapOf(f.attachmentId to "unavailable-frozen-file"))
        val legacy = original.copy(attemptOperation = "save", attemptInputJson = original.inputJson,
            attemptRowVersion = f.v1, attemptAttachmentFilesJson = original.attachmentFilesJson)
        f.store.services.put(legacy)
        f.store.tasks.put(f.task())
        val coordinator = f.repository()
        coordinator.adoptLegacyPending()
        f.online = true
        var sent = 0
        var rejected = 0
        val repository = FlightServicesRepository(f.store.services, UnusedMediaStore(), f.owner, { f.owner.value }, { true },
            sendSave = { _, _, _, _ -> sent++; error("Unavailable frozen bytes must never reach HTTP") },
            sendDelete = { _, _, _ -> error("Not a delete") }, acceptedSave = { _, _ -> }, acceptedDelete = { _, _ -> },
            schedule = {}, json = f.json, hasSavedArrival = { false }, acknowledgedPredecessorVersion = { _, version -> version },
            fetchFlight = { error("Frozen requests are not rebased") },
            grouped = { id, record -> coordinator.blocksFreshDrain(id) || coordinator.contains("service", id, record) },
            onAttemptRejected = { id, record -> rejected++; coordinator.legacyRejected("service", id, record) })
        repository.syncPending()
        assertEquals(0, sent)
        assertEquals(0, rejected)
        val retained = f.store.serviceRows.single()
        assertEquals("failed", retained.status)
        assertEquals(legacy.attemptOperation, retained.attemptOperation)
        assertEquals(legacy.attemptInputJson, retained.attemptInputJson)
        assertEquals(legacy.attemptRowVersion, retained.attemptRowVersion)
        assertEquals(legacy.attemptAttachmentFilesJson, retained.attemptAttachmentFilesJson)
        assertTrue(coordinator.syncPending())
        assertTrue(f.sent.isEmpty())
        try {
            coordinator.discard("task", f.flightId, f.taskId)
            fail("Local media failure is not proof that the original legacy HTTP request was rejected")
        } catch (_: IllegalStateException) { }
        assertEquals(legacy.attemptInputJson, f.store.serviceRows.single().attemptInputJson)
        assertEquals(1, f.store.batchRows.size)
    }

    @Test fun stagingCachedArrivalPatchHasNoWritesAndExplicitSaveRetargetsWithoutFetchingOrLosingRejectedIntent() = runBlocking {
        val f = Fixture()
        val original = f.arrival().copy(status = "conflict", error = "Arrival changed online")
        f.store.arrivals.put(original)
        val coordinator = f.repository()
        coordinator.adoptLegacyPending()
        f.applyServerRequest(SaveFlightOperationsBatchRequest(arrival = SaveFlightArrivalRequest(f.time, f.catalogId,
            "ONLINE", "colleague field three", "online remarks")), f.v2)
        val recorded = f.live.arrivalSubmissions.single().copy(submittedByEmployee = AssignedEmployee(f.taskId, "Colleague", "E2"))
        f.live = f.live.copy(arrivalSubmissions = listOf(recorded), fromCache = true,
            assignedEmployees = listOf(AssignedEmployee(f.staffId, "Employee", "E1")))
        val target = FlightOperationReviewTarget("arrival", changeId = original.changeId, useLocalSource = true)
        val before = f.store.snapshot()
        val local = f.json.decodeFromString<SaveFlightArrivalRequest>(original.inputJson).copy(aircraftTailNumber = "LOCAL")
        val merged = mergeArrivalReview(recorded.input().copy(targetSubmissionId = recorded.id), local)

        assertEquals(before, f.store.snapshot())
        assertTrue(f.sent.isEmpty())
        assertEquals(0, f.fetchCalls)
        coordinator.saveReviewed(f.live, target) { f.arrivalRepository(coordinator).save(f.live, merged) }
        val staged = f.store.arrivalRows.single()
        val stagedInput = f.json.decodeFromString<SaveFlightArrivalRequest>(staged.inputJson)
        assertEquals(recorded.id, stagedInput.targetSubmissionId)
        assertEquals("LOCAL", stagedInput.aircraftTailNumber)
        assertEquals("COLLEAGUE FIELD THREE", stagedInput.customerWorkReferenceNumber)
        assertEquals(f.v2, staged.rowVersion)
        assertEquals(recorded, f.json.decodeFromString<FlightArrivalSubmission>(checkNotNull(staged.reviewedArrivalJson)))
        assertTrue(f.sent.isEmpty())
        assertEquals(0, f.fetchCalls)

        f.online = true
        f.live = f.live.copy(rowVersion = f.v3, fromCache = false)
        f.repository().syncPending()
        assertEquals(f.v2, f.sent.single().third)
        assertEquals(stagedInput, f.sent.single().second.arrival)
        assertEquals(staged.inputJson, f.store.arrivalRows.single().inputJson)
        assertEquals("conflict", f.store.arrivalRows.single().status)
        assertTrue(f.accepted.isEmpty())
        assertEquals(0, f.fetchCalls)
        assertEquals(stagedInput, f.json.decodeFromString<SaveFlightArrivalRequest>(
            checkNotNull(f.decode(f.store.batchRows.single().inputJson).arrival).inputJson))
    }

    @Test fun repeatedReviewAfterRejectionAndRestartKeepsInheritedFieldsAbsentUntilTheEmployeeActuallyEditsThem() = runBlocking {
        val f = Fixture()
        val original = f.arrival().copy(status = "conflict")
        f.store.arrivals.put(original)
        f.repository().adoptLegacyPending()
        f.applyServerRequest(SaveFlightOperationsBatchRequest(arrival = SaveFlightArrivalRequest(f.time, f.catalogId,
            "ONLINE", "A3 inherited online")), f.v2)
        fun onlineInput() = f.live.arrivalSubmissions.single().let { it.input().copy(targetSubmissionId = it.id) }
        fun source(row: FlightArrivalPendingEntity) = f.json.decodeFromString<SaveFlightArrivalRequest>(
            checkNotNull(row.reviewSourceInputJson))
        suspend fun saveReview(input: SaveFlightArrivalRequest) {
            val row = f.store.arrivalRows.single()
            val coordinator = f.repository()
            val previousSource = f.json.decodeFromString<SaveFlightArrivalRequest>(row.reviewSourceInputJson ?: row.inputJson)
            val staged = mergeArrivalReview(onlineInput(), previousSource)
            coordinator.saveReviewed(f.live, FlightOperationReviewTarget("arrival", changeId = row.changeId,
                useLocalSource = true, stagedInputJson = f.json.encodeToString(staged))) {
                f.arrivalRepository(coordinator).save(f.live, input)
            }
        }
        suspend fun rejectAtNextServerReference(reference: String, version: String) {
            f.live = f.live.copy(rowVersion = version, arrivalSubmissions = listOf(
                f.live.arrivalSubmissions.single().copy(customerWorkReferenceNumber = reference)))
            f.online = true
            f.sendFailure = ApiException(409, "A colleague changed the flight after Review")
            f.repository().syncPending()
            assertEquals("conflict", f.store.arrivalRows.single().status)
            assertTrue(f.store.batchRows.single().attemptRejected)
        }

        saveReview(mergeArrivalReview(onlineInput(), f.json.decodeFromString(original.inputJson)))
        assertEquals("A3 INHERITED ONLINE", f.json.decodeFromString<SaveFlightArrivalRequest>(f.store.arrivalRows.single().inputJson).customerWorkReferenceNumber)
        assertNull(source(f.store.arrivalRows.single()).customerWorkReferenceNumber)
        rejectAtNextServerReference("C3 changed while Save was pending", f.v3)

        // A new coordinator has no in-memory origin information; only durable row metadata may be used.
        val secondMerged = mergeArrivalReview(onlineInput(), source(f.store.arrivalRows.single()))
        assertEquals("C3 CHANGED WHILE SAVE WAS PENDING", secondMerged.customerWorkReferenceNumber)
        saveReview(secondMerged)
        assertNull(source(f.store.arrivalRows.single()).customerWorkReferenceNumber)
        assertEquals(1, f.sent.size)
        rejectAtNextServerReference("E3 another colleague change", "AAAAAAAAAAQ=")

        val manual = mergeArrivalReview(onlineInput(), source(f.store.arrivalRows.single()))
            .copy(customerWorkReferenceNumber = "D3 explicitly entered by employee")
        saveReview(manual)
        assertEquals("D3 EXPLICITLY ENTERED BY EMPLOYEE", source(f.store.arrivalRows.single()).customerWorkReferenceNumber)
        rejectAtNextServerReference("F3 latest online value", "AAAAAAAAAAU=")
        val afterRestart = mergeArrivalReview(onlineInput(), source(f.store.arrivalRows.single()))
        assertEquals("D3 EXPLICITLY ENTERED BY EMPLOYEE", afterRestart.customerWorkReferenceNumber)
        assertEquals("QA-BATCH", afterRestart.aircraftTailNumber)
        assertTrue(f.accepted.isEmpty())
    }

    @Test fun ordinaryPendingEditAfterReviewPromotesOnlyActualManualDeltasIntoTheDurableSource() = runBlocking {
        val f = Fixture()
        val original = f.arrival().copy(status = "conflict")
        f.store.arrivals.put(original)
        val coordinator = f.repository()
        coordinator.adoptLegacyPending()
        f.applyServerRequest(SaveFlightOperationsBatchRequest(arrival = SaveFlightArrivalRequest(f.time, f.catalogId,
            "ONLINE", "A3 inherited reference", "A4 inherited remarks")), f.v2)
        fun source(row: FlightArrivalPendingEntity) = f.json.decodeFromString<SaveFlightArrivalRequest>(checkNotNull(row.reviewSourceInputJson))
        val online = f.live.arrivalSubmissions.single().let { it.input().copy(targetSubmissionId = it.id) }
        val staged = mergeArrivalReview(online, f.json.decodeFromString(original.inputJson))
        coordinator.saveReviewed(f.live, FlightOperationReviewTarget("arrival", changeId = original.changeId,
            useLocalSource = true, stagedInputJson = f.json.encodeToString(staged))) {
            f.arrivalRepository(coordinator).save(f.live, staged)
        }
        assertNull(source(f.store.arrivalRows.single()).customerWorkReferenceNumber)
        assertNull(source(f.store.arrivalRows.single()).remarks)

        // Reopening a pending editor uses ordinary Save, without another Review wrapper.
        val recreated = f.repository()
        val edited = f.json.decodeFromString<SaveFlightArrivalRequest>(f.store.arrivalRows.single().inputJson)
            .copy(customerWorkReferenceNumber = "D3 deliberately edited in ordinary editor")
        f.arrivalRepository(recreated).save(f.live, edited)
        assertEquals("D3 DELIBERATELY EDITED IN ORDINARY EDITOR", source(f.store.arrivalRows.single()).customerWorkReferenceNumber)
        assertNull(source(f.store.arrivalRows.single()).remarks)
        assertTrue(f.sent.isEmpty())

        f.live = f.live.copy(rowVersion = f.v3, arrivalSubmissions = listOf(f.live.arrivalSubmissions.single().copy(
            customerWorkReferenceNumber = "C3 changed online", remarks = "C4 latest colleague remarks")))
        f.online = true
        f.sendFailure = ApiException(409, "The reviewed version changed before synchronization")
        f.repository().syncPending()
        assertEquals("conflict", f.store.arrivalRows.single().status)
        val afterRestart = mergeArrivalReview(f.live.arrivalSubmissions.single().input(), source(f.store.arrivalRows.single()))
        assertEquals("D3 DELIBERATELY EDITED IN ORDINARY EDITOR", afterRestart.customerWorkReferenceNumber)
        assertEquals("C4 latest colleague remarks", afterRestart.remarks)
        assertTrue(f.accepted.isEmpty())
    }

    @Test fun failedExplicitReviewSaveRollsBackRetargetingEveryQueueDraftAndPrivateMedia() = runBlocking {
        val f = Fixture()
        f.files["old-signature"] = f.png
        f.files["old-service"] = "%PDF-kept".toByteArray()
        val arrival = f.arrival().copy(status = "conflict")
        f.store.arrivals.put(arrival)
        f.store.departures.put(f.departure("old-signature").copy(status = "failed"))
        f.store.services.put(f.service(files = mapOf(f.attachmentId to "old-service")).copy(status = "conflict"))
        f.store.tasks.put(f.task().copy(status = "failed"))
        val draft = FlightServiceFormDraftEntity(f.owner.value!!, f.flightId, "original form", "{}", f.time)
        f.store.services.putDraft(draft)
        val coordinator = f.repository()
        coordinator.adoptLegacyPending()
        val before = f.store.snapshot()
        f.scheduleCalls = 0

        try {
            coordinator.saveReviewed(f.live, FlightOperationReviewTarget("arrival", changeId = arrival.changeId)) {
                assertTrue(f.store.arrivalRows.isEmpty())
                f.store.arrivals.put(arrival.copy(changeId = "replacement", rowVersion = f.v2, status = "pending"))
                f.store.tasks.put(f.store.taskRows.single().copy(inputJson = "temporary"))
                f.store.services.putDraft(draft.copy(formJson = "temporary"))
                f.files["temporary-file"] = "invalid later media".toByteArray()
                throw IOException("Later attachment import failed")
            }
            fail("Explicit review Save must keep the previous durable intent on failure")
        } catch (_: IOException) { }

        assertEquals(before, f.store.snapshot())
        assertEquals(setOf("old-signature", "old-service"), f.files.keys)
        assertEquals(0, f.scheduleCalls)
        assertTrue(f.sent.isEmpty())
    }

    @Test fun reviewingFourMembersIncrementallyPreservesUnreviewedSiblingsAndPublishesOnlyWhenAllShareReviewedBase() = runBlocking {
        val f = Fixture()
        val arrival = f.arrival().copy(status = "conflict", error = "old arrival")
        val departure = f.departure().copy(status = "conflict", error = "old departure")
        val service = f.service().copy(status = "conflict", error = "old service")
        val task = f.task().copy(status = "conflict", error = "old task")
        f.store.arrivals.put(arrival); f.store.departures.put(departure)
        f.store.services.put(service); f.store.tasks.put(task)
        val coordinator = f.repository()
        coordinator.adoptLegacyPending()
        val adoptedDeparture = f.store.departureRows.single()
        val adoptedService = f.store.serviceRows.single()
        val adoptedTask = f.store.taskRows.single()
        f.applyServerRequest(SaveFlightOperationsBatchRequest(
            arrival = f.json.decodeFromString(arrival.inputJson), departure = f.json.decodeFromString(departure.inputJson),
            serviceChanges = listOf(FlightServiceBatchChange(f.serviceId, f.serviceInput(service))),
            taskChanges = listOf(FlightTaskBatchChange(f.taskId, f.taskInput(task)))), f.v2)
        f.online = true

        coordinator.saveReviewed(f.live, FlightOperationReviewTarget("arrival", changeId = arrival.changeId)) {
            f.arrivalRepository(coordinator).save(f.live, f.live.arrivalSubmissions.single().input())
        }
        assertEquals(adoptedDeparture, f.store.departureRows.single())
        assertEquals(adoptedService, f.store.serviceRows.single())
        assertEquals(adoptedTask, f.store.taskRows.single())
        coordinator.syncPending()
        assertEquals("conflict", f.store.batchRows.single().status)
        assertTrue(f.sent.isEmpty())

        coordinator.saveReviewed(f.live, FlightOperationReviewTarget("service", f.serviceId, service.changeId)) {
            f.serviceRepository(coordinator).save(f.live, f.serviceId, f.serviceInput(service).copy(notes = "reviewed service"))
        }
        assertEquals(adoptedTask, f.store.taskRows.single())
        assertEquals(adoptedDeparture, f.store.departureRows.single())
        coordinator.syncPending()
        assertTrue(f.sent.isEmpty())

        coordinator.saveReviewed(f.live, FlightOperationReviewTarget("task", f.taskId, task.changeId)) {
            f.taskRepository(coordinator).save(f.live, f.taskId, f.taskInput(task).copy(notes = "reviewed task"))
        }
        coordinator.syncPending()
        assertTrue(f.sent.isEmpty())
        coordinator.saveReviewed(f.live, FlightOperationReviewTarget("departure", changeId = departure.changeId)) {
            f.departureRepository(coordinator).save(f.live, SaveFlightDepartureRequest(f.live.departureSubmissions.single().atd,
                remarks = "reviewed departure"))
        }

        assertEquals(listOf(f.v2), f.decode(f.store.batchRows.single().inputJson).versions().distinct())
        assertEquals("pending", f.store.batchRows.single().status)
        assertTrue(f.sent.isEmpty())
        coordinator.syncPending()
        val request = f.sent.single().second
        assertNotNull(request.arrival)
        assertEquals("reviewed departure", request.departure?.remarks)
        assertEquals("reviewed service", request.serviceChanges.single().input?.notes)
        assertEquals("reviewed task", request.taskChanges.single().input?.notes)
        assertTrue(f.store.batchRows.isEmpty())
        assertTrue(f.store.arrivalRows.isEmpty()); assertTrue(f.store.departureRows.isEmpty())
        assertTrue(f.store.serviceRows.isEmpty()); assertTrue(f.store.taskRows.isEmpty())
    }

    @Test fun reviewDiscardRemovesOnlyTheExactMemberAndItsUnsharedFilesKeepingSiblingIntentHeld() = runBlocking {
        val f = Fixture()
        f.files["service-file"] = "%PDF-service".toByteArray()
        f.files["task-file"] = "%PDF-task".toByteArray()
        val service = f.service(files = mapOf(f.attachmentId to "service-file")).copy(status = "failed")
        val task = f.task(files = mapOf(f.taskAttachmentId to "task-file")).copy(status = "conflict")
        f.store.services.put(service); f.store.tasks.put(task)
        val coordinator = f.repository()
        coordinator.adoptLegacyPending()
        val adoptedTask = f.store.taskRows.single()

        coordinator.discardReviewed(f.flightId, FlightOperationReviewTarget("service", f.serviceId, service.changeId))

        assertTrue(f.store.serviceRows.isEmpty())
        assertEquals(adoptedTask, f.store.taskRows.single())
        assertEquals(listOf(adoptedTask), f.decode(f.store.batchRows.single().inputJson).tasks)
        assertTrue(f.decode(f.store.batchRows.single().inputJson).services.isEmpty())
        assertEquals("conflict", f.store.batchRows.single().status)
        assertEquals(setOf("task-file"), f.files.keys)
        assertTrue(f.sent.isEmpty())
        coordinator.discardReviewed(f.flightId, FlightOperationReviewTarget("task", f.taskId, task.changeId))
        assertTrue(f.store.batchRows.isEmpty())
        assertTrue(f.files.isEmpty())
    }

    @Test fun staleReviewCardIdentityCannotOverwriteOrDiscardAChangedLocalMember() = runBlocking {
        val f = Fixture()
        val service = f.service().copy(status = "conflict")
        f.store.services.put(service)
        val coordinator = f.repository()
        coordinator.adoptLegacyPending()
        val before = f.store.snapshot()
        var invoked = false
        val stale = FlightOperationReviewTarget("service", f.serviceId, "a-previous-card")
        try {
            coordinator.saveReviewed(f.live, stale) { invoked = true }
            fail("A stale card must not change newer local intent")
        } catch (_: IllegalStateException) { }
        try {
            coordinator.discardReviewed(f.flightId, stale)
            fail("A stale card must not discard newer local intent")
        } catch (_: IllegalStateException) { }
        assertFalse(invoked)
        assertEquals(before, f.store.snapshot())
        assertTrue(f.sent.isEmpty())
    }

    @Test fun unknownWholeAttemptBlocksExplicitReviewSaveAndMemberDiscardAcrossRestart() = runBlocking {
        val f = Fixture()
        val service = f.service()
        f.repository().save { f.store.services.put(service); f.store.tasks.put(f.task()) }
        f.online = true
        f.loseNextResponse = true
        f.repository().syncPending()
        val before = f.store.snapshot()
        val coordinator = f.repository()
        val target = FlightOperationReviewTarget("service", f.serviceId, service.changeId)
        var invoked = false
        try {
            coordinator.saveReviewed(f.live, target) { invoked = true }
            fail("The unknown original whole request must be recovered first")
        } catch (_: IllegalStateException) { }
        try {
            coordinator.discardReviewed(f.flightId, target)
            fail("A review card cannot discard an unknown whole request")
        } catch (_: IllegalStateException) { }
        assertFalse(invoked)
        assertEquals(before, f.store.snapshot())
        assertNotNull(f.store.batchRows.single().attemptInputJson)
        assertEquals(1, f.sent.size)
    }

    @Test fun unknownLegacyAttemptBlocksReviewOfItsGroupedSiblingWithoutChangingEitherRequest() = runBlocking {
        val f = Fixture()
        val service = f.service().let { it.copy(attemptOperation = "save", attemptInputJson = it.inputJson,
            attemptRowVersion = f.v1, attemptAttachmentFilesJson = it.attachmentFilesJson) }
        val task = f.task().copy(status = "conflict")
        f.store.services.put(service); f.store.tasks.put(task)
        val coordinator = f.repository()
        coordinator.adoptLegacyPending()
        val before = f.store.snapshot()
        var invoked = false
        val target = FlightOperationReviewTarget("task", f.taskId, task.changeId)
        try {
            coordinator.saveReviewed(f.live, target) { invoked = true }
            fail("A sibling cannot resolve around an unknown legacy request")
        } catch (_: IllegalStateException) { }
        try {
            coordinator.discardReviewed(f.flightId, target)
            fail("A sibling review card cannot discard around an unknown legacy request")
        } catch (_: IllegalStateException) { }
        assertFalse(invoked)
        assertEquals(before, f.store.snapshot())
        assertTrue(f.sent.isEmpty())
    }

    private class Fixture {
        val owner = MutableStateFlow<String?>("api|account")
        val store = MemoryBatchStore()
        val json = Json { encodeDefaults = true; ignoreUnknownKeys = true }
        val time = "2026-11-10T12:00:00Z"
        val flightId = "11111111-1111-1111-1111-111111111111"
        val serviceId = "22222222-2222-2222-2222-222222222222"
        val taskId = "33333333-3333-3333-3333-333333333333"
        val staffId = "44444444-4444-4444-4444-444444444444"
        val catalogId = "55555555-5555-5555-5555-555555555555"
        val attachmentId = "66666666-6666-6666-6666-666666666666"
        val taskAttachmentId = "77777777-7777-7777-7777-777777777777"
        val v1 = "AAAAAAAAAAE="
        val v2 = "AAAAAAAAAAI="
        val v3 = "AAAAAAAAAAM="
        val png = Base64.getDecoder().decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=")
        var online = false
        var loseNextResponse = false
        var sendFailure: Exception? = null
        var acceptanceFailure: Exception? = null
        var serviceHydrationError: Exception? = null
        var scheduleCalls = 0
        var fetchCalls = 0
        var cleanupDeferred = false
        val cleanupDeferrals = mutableListOf<Boolean>()
        val files = linkedMapOf<String, ByteArray>()
        private val encoded = mutableMapOf<String, String>()
        val sent = mutableListOf<Triple<String, SaveFlightOperationsBatchRequest, String>>()
        val accepted = mutableListOf<FlightOperationsBatchInput>()
        private var lastServerRequest: SaveFlightOperationsBatchRequest? = null
        var live = FlightDetail(flightId, "SV100", "customer", "SV", "Airline", "station", "AAA", "Station", "operation", "Transit",
            aircraftTypeId = catalogId, scheduledArrivalUtc = time, scheduledDepartureUtc = "2026-11-10T13:00:00Z",
            isPerLanding = false, createdAtUtc = time, rowVersion = v1, stationTimeZoneId = "Asia/Riyadh",
            currentStaffMemberId = staffId, canSaveArrival = true, canSaveDeparture = true, canSaveServices = true,
            allowedServiceIds = listOf(catalogId), canSaveTasks = true)
        val baseServiceInput get() = SaveFlightServiceRequest(catalogId, time, "2026-11-10T12:10:00Z", notes = "original service",
            performers = listOf(SaveFlightServicePerformerRequest(UUID.randomUUID().toString(), staffId, time, "2026-11-10T12:10:00Z")))

        fun service(id: String = serviceId, files: Map<String, String> = emptyMap()): FlightServicePendingEntity {
            val input = baseServiceInput.copy(attachments = files.keys.map { FlightServiceAttachmentInput(it, 2, "service.pdf", "application/pdf") })
            return FlightServicePendingEntity(owner.value!!, flightId, id, "SV100", UUID.randomUUID().toString(), "save",
                json.encodeToString(input), v1, null, time, attachmentFilesJson = json.encodeToString(files))
        }
        fun task(id: String = taskId, files: Map<String, String> = emptyMap()): FlightTaskPendingEntity {
            val input = SaveFlightTaskRequest(0, time, "2026-11-10T12:10:00Z", notes = "original task",
                performers = listOf(SaveFlightTaskPerformerRequest(UUID.randomUUID().toString(), staffId, time, "2026-11-10T12:10:00Z")),
                attachments = files.keys.map { FlightServiceAttachmentInput(it, 2, "task.pdf", "application/pdf") })
            return FlightTaskPendingEntity(owner.value!!, flightId, id, "SV100", UUID.randomUUID().toString(), "save",
                json.encodeToString(input), v1, null, time, attachmentFilesJson = json.encodeToString(files))
        }
        fun arrival() = FlightArrivalPendingEntity(owner.value!!, flightId, "SV100", "arrival-change",
            json.encodeToString(SaveFlightArrivalRequest(time, catalogId, "QA-BATCH")), v1, time, reviewedArrivalJson = "null")
        fun departure(signatureFile: String? = null) = FlightDeparturePendingEntity(owner.value!!, flightId, "SV100", staffId, "departure-change",
            json.encodeToString(SaveFlightDepartureRequest("2026-11-10T12:20:00Z", signatureSignedAtUtc = time)), v1, "[]", time, signatureFile)
        fun decode(value: String) = json.decodeFromString<FlightOperationsBatchInput>(value)
        fun serviceInput(row: FlightServicePendingEntity) = json.decodeFromString<SaveFlightServiceRequest>(checkNotNull(row.inputJson))
        fun taskInput(row: FlightTaskPendingEntity) = json.decodeFromString<SaveFlightTaskRequest>(checkNotNull(row.inputJson))
        fun removeFile(key: String) { files.remove(key); encoded.remove(key) }
        private fun file(key: String) = encoded.getOrPut(key) {
            Base64.getEncoder().encodeToString(files[key] ?: throw IOException("Private file unavailable"))
        }
        private fun attachments(items: List<FlightServiceAttachmentInput>?, keys: String): List<FlightServiceAttachmentInput>? {
            val paths = json.decodeFromString<Map<String, String>>(keys)
            return items?.map { item -> item.copy(content = paths[item.id]?.let(::file)) }
        }

        fun arrivalRepository(coordinator: FlightOperationsBatchesRepository) = FlightArrivalsRepository(
            store.arrivals, owner, { owner.value }, { online },
            send = { _, _, _ -> error("Reviewed Save cannot use the individual endpoint") },
            accepted = { _, _ -> }, schedule = {}, json = json, now = { Instant.parse(time) },
            fetchFlight = { live }, operationSave = { coordinator.save(it) })

        fun departureRepository(coordinator: FlightOperationsBatchesRepository) = FlightDeparturesRepository(
            store.departures, UnusedMediaStore(), owner, { owner.value }, { online },
            send = { _, _, _ -> error("Reviewed Save cannot use the individual endpoint") },
            accepted = { _, _ -> }, schedule = {}, json = json, hasSavedArrival = { false },
            acknowledgedArrivalVersion = { _, version -> version }, fetchFlight = { live }, now = { Instant.parse(time) },
            operationSave = { coordinator.save(it) })

        fun serviceRepository(coordinator: FlightOperationsBatchesRepository) = FlightServicesRepository(
            store.services, UnusedMediaStore(), owner, { owner.value }, { online },
            sendSave = { _, _, _, _ -> error("Grouped public Save cannot use the individual endpoint") },
            sendDelete = { _, _, _ -> error("Grouped public Delete cannot use the individual endpoint") },
            acceptedSave = { _, _ -> }, acceptedDelete = { _, _ -> }, schedule = {}, json = json,
            hasSavedArrival = { false }, acknowledgedPredecessorVersion = { _, version -> version }, fetchFlight = { live },
            now = { Instant.parse(time) }, grouped = { id, record -> coordinator.blocksFreshDrain(id) || coordinator.contains("service", id, record) },
            operationSave = { coordinator.save(it) })

        fun taskRepository(coordinator: FlightOperationsBatchesRepository) = FlightTasksRepository(
            store.tasks, UnusedMediaStore(), owner, { owner.value }, { online },
            sendSave = { _, _, _, _ -> error("Grouped public Save cannot use the individual endpoint") },
            sendDelete = { _, _, _ -> error("Grouped public Delete cannot use the individual endpoint") },
            acceptedSave = { _, _ -> }, acceptedDelete = { _, _ -> }, schedule = {}, json = json,
            hasSavedArrival = { false }, acknowledgedPredecessorVersion = { _, version -> version }, fetchFlight = { live },
            now = { Instant.parse(time) }, grouped = { id, record -> coordinator.blocksFreshDrain(id) || coordinator.contains("task", id, record) },
            operationSave = { coordinator.save(it) })

        fun repository(): FlightOperationsBatchesRepository {
            lateinit var repository: FlightOperationsBatchesRepository
            repository = FlightOperationsBatchesRepository(store, { owner.value }, { online }, { fetchCalls++; live },
                send = { id, request, version ->
                    sent += Triple(id, request, version)
                    sendFailure?.let { throw it }
                    if (lastServerRequest != request) {
                        if (version != live.rowVersion) throw ApiException(409, "The reviewed flight version is stale")
                        live = response(request, if (live.rowVersion == v1) v2 else v3)
                        lastServerRequest = request
                    }
                    if (loseNextResponse) { loseNextResponse = false; throw IOException("The whole response was lost") }
                    live
                }, accepted = { _, input -> acceptanceFailure?.let { throw it }; accepted += input },
                hydrateArrival = { json.decodeFromString(it.inputJson) },
                hydrateDeparture = { row -> json.decodeFromString<SaveFlightDepartureRequest>(row.inputJson).copy(signatureContent = row.signatureFile?.let(::file)) },
                hydrateService = { row -> serviceHydrationError?.let { throw it }; row.inputJson?.let {
                    serviceInput(row).let { input -> input.copy(attachments = attachments(input.attachments, row.attachmentFilesJson)) }
                } },
                hydrateTask = { row -> row.inputJson?.let {
                    taskInput(row).let { input -> input.copy(attachments = attachments(input.attachments, row.attachmentFilesJson)) }
                } },
                acknowledgedVersion = { _, version -> version }, deferCleanup = { cleanupDeferred = it; cleanupDeferrals += it },
                cleanup = {
                    check(!cleanupDeferred)
                    val keys = linkedSetOf<String>()
                    (store.serviceRows.map { it.attachmentFilesJson } + store.taskRows.map { it.attachmentFilesJson } + store.draftRows.map { it.attachmentFilesJson })
                        .forEach { keys += json.decodeFromString<Map<String, String>>(it).values }
                    keys += store.departureRows.flatMap { listOfNotNull(it.signatureFile, it.attemptSignatureFile) }
                    keys += repository.fileReferences("service") + repository.fileReferences("task") + repository.fileReferences("departure")
                    files.keys.retainAll(keys); encoded.keys.retainAll(keys)
                }, schedule = { scheduleCalls++ }, json = json)
            return repository
        }

        fun applyServerRequest(request: SaveFlightOperationsBatchRequest, version: String) {
            live = response(request, version)
        }

        private fun response(request: SaveFlightOperationsBatchRequest, version: String): FlightDetail {
            var result = live.copy(rowVersion = version)
            request.arrival?.let { input -> result = result.copy(arrivalSubmissions = listOf(FlightArrivalSubmission(
                id = "88888888-8888-8888-8888-888888888888", submittedByEmployee = AssignedEmployee(staffId, "Employee", "E1"),
                ata = input.ata, aircraftTypeId = input.aircraftTypeId, aircraftTypeModel = "Aircraft", aircraftTailNumber = input.aircraftTailNumber,
                receivedByEmployee = AssignedEmployee(staffId, "Employee", "E1"), customerWorkReferenceNumber = input.customerWorkReferenceNumber,
                remarks = input.remarks, isAccepted = true, createdAtUtc = time))) }
            request.departure?.let { input -> result = result.copy(departureSubmissions = listOf(FlightDepartureSubmission(
                id = "99999999-9999-9999-9999-999999999999", submittedByEmployee = AssignedEmployee(staffId, "Employee", "E1"),
                atd = input.atd, handledByEmployee = AssignedEmployee(staffId, "Employee", "E1"), remarks = input.remarks,
                isAccepted = true, createdAtUtc = time, signatureVersion = "saved-signature"))) }
            for (change in request.serviceChanges) {
                val remaining = result.services.filterNot { it.id == change.recordId }
                val input = change.input
                result = result.copy(services = remaining + if (input == null) emptyList() else listOf(FlightServiceRecord(change.recordId,
                    input.serviceId, "Service", input.startUtc, input.endUtc, input.customerReferenceNumber, input.notes, "user", createdAtUtc = time,
                    performers = input.performers.map { FlightServicePerformerRecord(it.id, it.staffMemberId, "Employee", "E1", it.startUtc, it.endUtc) },
                    canEdit = true, canSetEnd = true, attachments = input.attachments.orEmpty().map {
                        FlightServiceAttachmentRecord(it.id, it.kind, it.fileName, it.contentType, it.content?.let { content -> Base64.getDecoder().decode(content).size.toLong() } ?: 1)
                    })))
            }
            for (change in request.taskChanges) {
                val remaining = result.tasks.filterNot { it.id == change.recordId }
                val input = change.input
                result = result.copy(tasks = remaining + if (input == null) emptyList() else listOf(FlightTaskRecord(change.recordId,
                    input.taskType, input.startUtc, input.endUtc, input.ataChapterId, customerReferenceNumber = input.customerReferenceNumber,
                    notes = input.notes, createdByUserId = "user", createdAtUtc = time,
                    performers = input.performers.map { FlightTaskPerformerRecord(it.id, it.staffMemberId, "Employee", "E1", it.startUtc, it.endUtc, it.notes) },
                    canEdit = true, canSetEnd = true, canDelete = true, canChangeType = true,
                    attachments = input.attachments.orEmpty().map { FlightServiceAttachmentRecord(it.id, it.kind, it.fileName, it.contentType,
                        it.content?.let { content -> Base64.getDecoder().decode(content).size.toLong() } ?: 1) })))
            }
            return result
        }
    }

    private class UnusedMediaStore : ServiceAttachmentStore, TaskAttachmentStore, DepartureSignatureStore {
        override suspend fun save(content: ByteArray): String = error("No individual upload should prepare media")
        override suspend fun import(uri: String): String = error("No individual upload should import media")
        override suspend fun read(key: String): ByteArray = error("No individual upload should read media")
        override suspend fun retainOnly(referenced: Set<String>) = Unit
        override suspend fun previewFile(key: String): File? = null
    }

    private class MemoryBatchStore : FlightOperationsBatchStore {
        var transactionCalls = 0
        val batchRows = mutableListOf<FlightOperationsBatchEntity>()
        val arrivalRows = mutableListOf<FlightArrivalPendingEntity>()
        val departureRows = mutableListOf<FlightDeparturePendingEntity>()
        val serviceRows = mutableListOf<FlightServicePendingEntity>()
        val taskRows = mutableListOf<FlightTaskPendingEntity>()
        val draftRows = mutableListOf<FlightServiceFormDraftEntity>()
        data class Snapshot(val batches: List<FlightOperationsBatchEntity>, val arrivals: List<FlightArrivalPendingEntity>,
            val departures: List<FlightDeparturePendingEntity>, val services: List<FlightServicePendingEntity>,
            val tasks: List<FlightTaskPendingEntity>, val drafts: List<FlightServiceFormDraftEntity>)
        fun snapshot() = Snapshot(batchRows.toList(), arrivalRows.toList(), departureRows.toList(), serviceRows.toList(), taskRows.toList(), draftRows.toList())
        override suspend fun transaction(block: suspend () -> Unit) {
            transactionCalls++
            val before = snapshot()
            try { block() } catch (error: Throwable) {
                batchRows.replace(before.batches); arrivalRows.replace(before.arrivals); departureRows.replace(before.departures)
                serviceRows.replace(before.services); taskRows.replace(before.tasks); draftRows.replace(before.drafts)
                throw error
            }
        }
        private fun <T> MutableList<T>.replace(values: List<T>) { clear(); addAll(values) }
        override val batches = object : FlightOperationsBatchDao {
            override suspend fun rows(owner: String) = batchRows.filter { it.ownerSubject == owner }
            override suspend fun allRows() = batchRows.toList()
            override suspend fun put(row: FlightOperationsBatchEntity) { delete(row.ownerSubject, row.flightId); batchRows += row }
            override suspend fun delete(owner: String, flightId: String) { batchRows.removeAll { it.ownerSubject == owner && it.flightId == flightId } }
            override suspend fun clear() { batchRows.clear() }
        }
        override val arrivals = object : FlightArrivalDao {
            override fun observe(owner: String) = flowOf(arrivalRows.filter { it.ownerSubject == owner })
            override suspend fun rows(owner: String) = arrivalRows.filter { it.ownerSubject == owner }
            override suspend fun put(row: FlightArrivalPendingEntity) { arrivalRows.removeAll { it.ownerSubject == row.ownerSubject && it.flightId == row.flightId }; arrivalRows += row }
            override suspend fun delete(owner: String, flightId: String, changeId: String) { arrivalRows.removeAll { it.ownerSubject == owner && it.flightId == flightId && it.changeId == changeId } }
            override suspend fun clear() { arrivalRows.clear() }
        }
        override val departures = object : FlightDepartureDao {
            override fun observe(owner: String) = flowOf(departureRows.filter { it.ownerSubject == owner })
            override suspend fun rows(owner: String) = departureRows.filter { it.ownerSubject == owner }
            override suspend fun put(row: FlightDeparturePendingEntity) { departureRows.removeAll { it.ownerSubject == row.ownerSubject && it.flightId == row.flightId }; departureRows += row }
            override suspend fun delete(owner: String, flightId: String, changeId: String) { departureRows.removeAll { it.ownerSubject == owner && it.flightId == flightId && it.changeId == changeId } }
            override suspend fun signatureFiles() = departureRows.flatMap { listOfNotNull(it.signatureFile, it.attemptSignatureFile) }
            override suspend fun clear() { departureRows.clear() }
        }
        override val services = object : FlightServiceDao {
            override fun observe(owner: String) = flowOf(serviceRows.filter { it.ownerSubject == owner })
            override suspend fun rows(owner: String) = serviceRows.filter { it.ownerSubject == owner }
            override suspend fun allRows() = serviceRows.toList()
            override fun observeDrafts(owner: String) = flowOf(draftRows.filter { it.ownerSubject == owner })
            override suspend fun drafts(owner: String) = draftRows.filter { it.ownerSubject == owner }
            override suspend fun allDrafts() = draftRows.toList()
            override suspend fun putDraft(row: FlightServiceFormDraftEntity) { draftRows.removeAll { it.ownerSubject == row.ownerSubject && it.flightId == row.flightId }; draftRows += row }
            override suspend fun put(row: FlightServicePendingEntity) { serviceRows.removeAll { it.ownerSubject == row.ownerSubject && it.flightId == row.flightId && it.serviceRecordId == row.serviceRecordId }; serviceRows += row }
            override suspend fun delete(owner: String, flightId: String, recordId: String, changeId: String) { serviceRows.removeAll { it.ownerSubject == owner && it.flightId == flightId && it.serviceRecordId == recordId && it.changeId == changeId } }
            override suspend fun deleteForFlight(owner: String, flightId: String) { serviceRows.removeAll { it.ownerSubject == owner && it.flightId == flightId } }
            override suspend fun clear() { serviceRows.clear() }
            override suspend fun deleteDraft(owner: String, flightId: String) { draftRows.removeAll { it.ownerSubject == owner && it.flightId == flightId } }
            override suspend fun clearDrafts() { draftRows.clear() }
        }
        override val tasks = object : FlightTaskDao {
            override fun observe(owner: String) = flowOf(taskRows.filter { it.ownerSubject == owner })
            override suspend fun rows(owner: String) = taskRows.filter { it.ownerSubject == owner }
            override suspend fun allRows() = taskRows.toList()
            override suspend fun put(row: FlightTaskPendingEntity) { taskRows.removeAll { it.ownerSubject == row.ownerSubject && it.flightId == row.flightId && it.taskRecordId == row.taskRecordId }; taskRows += row }
            override suspend fun delete(owner: String, flightId: String, recordId: String, changeId: String) { taskRows.removeAll { it.ownerSubject == owner && it.flightId == flightId && it.taskRecordId == recordId && it.changeId == changeId } }
            override suspend fun deleteForFlight(owner: String, flightId: String) { taskRows.removeAll { it.ownerSubject == owner && it.flightId == flightId } }
            override suspend fun clear() { taskRows.clear() }
        }
    }
}
