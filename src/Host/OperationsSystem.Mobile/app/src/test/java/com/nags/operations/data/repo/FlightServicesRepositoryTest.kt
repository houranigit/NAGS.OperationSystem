package com.nags.operations.data.repo

import com.nags.operations.data.*
import com.nags.operations.data.db.dao.FlightServiceDao
import com.nags.operations.data.db.entities.FlightServicePendingEntity
import com.nags.operations.data.db.entities.FlightServiceFormDraftEntity
import java.io.IOException
import java.time.Instant
import java.util.Base64
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.async
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import org.junit.Assert.*
import org.junit.Test

class FlightServicesRepositoryTest {
    private val owner = MutableStateFlow<String?>("api|account")
    private val dao = ServiceMemoryDao()
    private val files = ServiceMemoryFiles()
    private val now = Instant.parse("2026-11-10T12:00:00Z")
    private val record = "11111111-1111-1111-1111-111111111111"
    private val second = "22222222-2222-2222-2222-222222222222"
    private val staff = "33333333-3333-3333-3333-333333333333"
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
    private var localBounds: FlightServiceActualBounds? = null
    private var beforeDelete: suspend (String, String, String) -> Unit = { _, _, _ -> }
    private var cacheAcknowledged: suspend (String, FlightServiceSaved, Map<String, ByteArray>) -> Unit = { _, _, _ -> }
    private val input = SaveFlightServiceRequest("44444444-4444-4444-4444-444444444444", now.toString(), notes = " note ",
        performers = listOf(SaveFlightServicePerformerRequest("55555555-5555-5555-5555-555555555555", staff, now.toString())))
    private val flight = FlightDetail("66666666-6666-6666-6666-666666666666", "SV100", "customer", "SV", "Airline", "station", "AAA",
        "Station", "operation", "Transit", scheduledArrivalUtc = now.toString(), scheduledDepartureUtc = now.plusSeconds(3600).toString(),
        isPerLanding = false, createdAtUtc = now.toString(), rowVersion = v1, stationTimeZoneId = "Asia/Riyadh",
        currentStaffMemberId = staff, canSaveServices = true, allowedServiceIds = listOf(input.serviceId))
    private var live = flight
    private val sent = mutableListOf<Triple<String, SaveFlightServiceRequest?, String>>()
    private fun saved(id: String, value: SaveFlightServiceRequest, version: String = v2) = FlightServiceSaved(
        FlightServiceRecord(id, value.serviceId, "Service", value.startUtc, value.endUtc, value.customerReferenceNumber, value.notes,
            "user", createdAtUtc = now.toString(), performers = value.performers.map {
                FlightServicePerformerRecord(it.id, it.staffMemberId, "Employee", "E1", it.startUtc, it.endUtc)
            }, canEdit = true, canSetEnd = true, attachments = value.attachments.orEmpty().map {
                FlightServiceAttachmentRecord(it.id, it.kind, it.fileName, it.contentType,
                    it.content?.let { bytes -> Base64.getDecoder().decode(bytes).size.toLong() } ?: 6L)
            }), version)
    private var sender: suspend (String, String, SaveFlightServiceRequest, String) -> FlightServiceSaved = { _, id, value, version ->
        sent += Triple(id, value, version)
        fail?.let { throw it }
        saved(id, value, if (version == v1) v2 else v3)
    }
    private fun repository() = FlightServicesRepository(dao, files, owner, { owner.value }, { online },
        sendSave = { a, b, c, d -> sender(a, b, c, d) }, sendDelete = { _, id, version ->
            sent += Triple(id, null, version); fail?.let { throw it }; FlightServiceDeleted(id, v3)
        }, acceptedSave = { _, result -> live = live.copy(rowVersion = result.rowVersion,
            services = live.services.filterNot { it.id == result.service.id } + result.service) },
        acceptedDelete = { _, result -> live = live.copy(rowVersion = result.rowVersion, services = live.services.filterNot { it.id == result.id }) },
        schedule = {}, json = Json, hasSavedArrival = { arrival }, acknowledgedPredecessorVersion = { _, reviewed ->
            if (reviewed == v1) predecessor ?: reviewed else reviewed
        }, fetchFlight = { live }, now = { now }, onAcknowledgedBeforeDelete = { a, b, c -> beforeDelete(a, b, c) },
        hasSavedCancellation = { cancellation }, onAttachmentsAcknowledged = { a, b, c -> cacheAcknowledged(a, b, c) },
        hasUnacknowledgedDepartureAttempt = { departureAttempt }, hasUnacknowledgedSiblingWork = { siblingAttempt }, effectiveLocalActuals = {
            localBounds ?: FlightServiceActualBounds(it.visibleArrival()?.ata, it.visibleDeparture()?.atd)
        })

    @Test fun assignedFullCorrectionUsesTheSameRecordAndPreservesOriginalCreatorWhileDisplayMetadataRefreshes() = runBlocking {
        val closed = input.copy(endUtc = now.plusSeconds(60).toString(), performers = input.performers.map { it.copy(endUtc = now.toString()) })
        val baseline = saved(record, closed).service.copy(createdByUserId = "original-author", createdByDisplayName = "Original author",
            endedByUserId = "original-closer", canEdit = true, canSetEnd = true, canChangeService = true, canDelete = false)
        val reviewed = flight.copy(services = listOf(baseline), allowedServiceIds = listOf(input.serviceId, second))
        val desired = baseline.input().copy(serviceId = second, notes = "Assigned correction", endUtc = now.plusSeconds(90).toString())
        val repo = repository()
        repo.save(reviewed, record, desired)
        val queued = dao.rows(owner.value!!).single()
        assertEquals(baseline, Json.decodeFromString<FlightServiceRecord>(queued.reviewedServiceJson!!))
        try { repo.delete(reviewed, record); fail("Expected creator/closer deletion guard") } catch (_: IllegalStateException) { }
        assertEquals(queued, dao.rows(owner.value!!).single())
        live = reviewed.copy(services = listOf(baseline.copy(createdByDisplayName = "Updated account display name",
            endedByDisplayName = "Authoritative closer")))
        sender = { _, id, value, version ->
            sent += Triple(id, value, version)
            FlightServiceSaved(baseline.copy(serviceId = value.serviceId, notes = value.notes, endUtc = value.endUtc), v2)
        }
        online = true
        assertFalse(repo.syncPending())
        assertEquals(record, sent.single().first)
        assertEquals(second, sent.single().second!!.serviceId)
        assertEquals("original-author", live.services.single().createdByUserId)
        assertEquals(baseline.createdAtUtc, live.services.single().createdAtUtc)
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun explicitDeleteCapabilityAllowsForeignClosedServiceAndTargetsOnlyItsStableRecord() = runBlocking {
        val closed = input.copy(endUtc = now.plusSeconds(60).toString(), performers = input.performers.map { it.copy(endUtc = now.toString()) })
        val original = saved(record, closed).service.copy(createdByUserId = "original-author", endedByUserId = "other-closer",
            canDelete = true, canEdit = true, canSetEnd = true)
        live = flight.copy(services = listOf(original))
        val repo = repository()
        repo.delete(live, record)
        assertEquals("delete", dao.rows(owner.value!!).single().operation)
        assertEquals(original, Json.decodeFromString<FlightServiceRecord>(dao.rows(owner.value!!).single().reviewedServiceJson!!))
        online = true
        assertFalse(repo.syncPending())
        assertEquals(listOf(Triple(record, null, v1)), sent)
        assertTrue(live.services.isEmpty())
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun deniedOrLostDeleteCapabilityRetainsForeignServiceForExplicitReview() = runBlocking {
        val original = saved(record, input).service.copy(createdByUserId = "another-author", canDelete = false)
        val denied = flight.copy(services = listOf(original))
        val repo = repository()
        assertTrue(runCatching { repo.delete(denied, record) }.isFailure)
        assertTrue(dao.rows(owner.value!!).isEmpty())
        val allowed = denied.copy(services = listOf(original.copy(canDelete = true)))
        repo.delete(allowed, record)
        live = denied
        online = true
        assertFalse(repo.syncPending())
        assertTrue(sent.isEmpty())
        assertEquals("conflict", dao.rows(owner.value!!).single().status)
        assertNull(dao.rows(owner.value!!).single().attemptOperation)
    }

    @Test fun unknownTaskAttemptDefersFreshServiceWithoutFreezingOrChangingItsInput() = runBlocking {
        val repo = repository()
        repo.save(flight, record, input)
        val queued = dao.rows(owner.value!!).single()
        online = true; siblingAttempt = true
        assertFalse(repo.syncPending())
        assertTrue(sent.isEmpty())
        assertEquals(queued, dao.rows(owner.value!!).single())
        siblingAttempt = false
        assertFalse(repo.syncPending())
        assertEquals(Json.decodeFromString<SaveFlightServiceRequest>(queued.inputJson!!), sent.single().second)
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun unknownTaskAttemptDoesNotPreventExactServiceAttemptReplay() = runBlocking {
        val repo = repository()
        repo.save(flight, record, input)
        online = true; fail = IOException("Response unavailable")
        assertTrue(repo.syncPending())
        val frozen = dao.rows(owner.value!!).single().attemptInputJson
        assertNotNull(frozen)
        fail = null; siblingAttempt = true
        assertFalse(repo.syncPending())
        assertEquals(2, sent.size)
        assertEquals(sent.first(), sent.last())
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun offlineBatchIsDurableAndOnlyLatestUnsentInputUploadsAfterRestart() = runBlocking {
        repository().saveBatch(flight, listOf(FlightServiceChange(record, input), FlightServiceChange(second, input)))
        val first = dao.rows(owner.value!!).first { it.serviceRecordId == record }
        repository().save(flight, record, input.copy(notes = " latest "))
        assertEquals(first.changeId, dao.rows(owner.value!!).first { it.serviceRecordId == record }.changeId)
        val restarted = repository()
        assertEquals(2, restarted.changes.first().size)
        online = true
        assertFalse(restarted.syncPending())
        assertEquals(listOf(v1, v2), sent.map { it.third })
        assertEquals("latest", sent.first { it.first == record }.second!!.notes)
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun invalidSecondChangePublishesNoneOfTheBatch() = runBlocking {
        try {
            repository().saveBatch(flight, listOf(FlightServiceChange(record, input), FlightServiceChange(second, input.copy(performers = emptyList()))))
            fail("Expected validation failure")
        } catch (_: IllegalStateException) { }
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun lostResponseRetriesFrozenAttemptBeforePublishingNewerDesiredInput() = runBlocking {
        val repo = repository()
        repo.save(flight, record, input)
        online = true
        fail = IOException("response lost")
        assertTrue(repo.syncPending())
        repo.save(flight, record, input.copy(notes = "newer"))
        fail = null
        assertTrue(repo.syncPending())
        assertEquals("note", sent.last().second!!.notes)
        assertEquals(v1, sent.last().third)
        assertFalse(repo.syncPending())
        assertEquals("newer", sent.last().second!!.notes)
        assertEquals(v2, sent.last().third)
    }

    @Test fun incomingOwnAcknowledgmentRequiresUnchangedReviewedServiceAndKeepsExternalConflicts() = runBlocking {
        val baseline = saved(record, input).service
        live = flight.copy(services = listOf(baseline.copy(notes = "other employee changed")), rowVersion = v2)
        val repo = repository()
        repo.save(flight.copy(services = listOf(baseline)), record, input.copy(notes = "my edit"))
        repo.onPriorWriteAcknowledged(flight.id, v1, v2)
        online = true
        assertFalse(repo.syncPending())
        assertEquals("conflict", dao.rows(owner.value!!).single().status)
        assertTrue(sent.isEmpty())
        repo.retry(flight.id, record)
        assertFalse(repo.syncPending())
        assertEquals(v2, dao.rows(owner.value!!).single().rowVersion)
        assertTrue(sent.isEmpty())
    }

    @Test fun failedDependencyCallbackRetainsReplayProofUntilDependentVersionIsDurable() = runBlocking {
        val repo = repository()
        repo.save(flight, record, input)
        beforeDelete = { _, _, _ -> throw IOException("dependent queue unavailable") }
        online = true
        assertTrue(repo.syncPending())
        assertNotNull(dao.rows(owner.value!!).single().attemptInputJson)
        beforeDelete = { _, _, _ -> }
        assertFalse(repo.syncPending())
        assertEquals(listOf(v1, v1), sent.map { it.third })
    }

    @Test fun queuedArrivalBlocksThisFlightWithoutSendingServices() = runBlocking {
        val repo = repository()
        repo.save(flight, record, input)
        arrival = true; online = true
        assertFalse(repo.syncPending())
        assertTrue(sent.isEmpty())
        assertEquals(1, dao.rows(owner.value!!).size)
    }

    @Test fun accountChangesHideAndDoNotSendAnotherOwnersRows() = runBlocking {
        val repo = repository()
        repo.save(flight, record, input)
        owner.value = "other-api|account"
        assertTrue(repo.changes.first().isEmpty())
        online = true
        assertFalse(repo.syncPending())
        assertTrue(sent.isEmpty())
        assertEquals(1, dao.rows("api|account").size)
        repo.pause()
        try { repo.save(flight, record, input); fail("Expected paused session") } catch (_: IllegalStateException) { }
    }

    @Test fun anotherOperatorsEndCannotBeChangedOrDeletedThroughCreatorPermission() = runBlocking {
        val baseline = saved(record, input.copy(endUtc = now.plusSeconds(60).toString(),
            performers = input.performers.map { it.copy(endUtc = now.toString()) })).service.copy(canSetEnd = false)
        val repo = repository()
        val reviewed = flight.copy(services = listOf(baseline))
        try { repo.save(reviewed, record, baseline.input().copy(endUtc = null)); fail("Expected protected end") } catch (_: IllegalStateException) { }
        try { repo.delete(reviewed, record); fail("Expected protected delete") } catch (_: IllegalStateException) { }
        repo.save(reviewed, record, baseline.input().copy(notes = "allowed core correction"))
        assertEquals(1, dao.rows(owner.value!!).size)
    }

    @Test fun noncreatorMayOnlySetAllowedEndWhileOtherInputIsUnchanged() = runBlocking {
        val baseline = saved(record, input.copy(performers = input.performers.map { it.copy(endUtc = now.toString()) })).service.copy(canEdit = false)
        val reviewed = flight.copy(services = listOf(baseline))
        val repo = repository()
        try { repo.save(reviewed, record, baseline.input().copy(notes = "forbidden")); fail("Expected core protection") } catch (_: IllegalStateException) { }
        repo.save(reviewed, record, baseline.input().copy(endUtc = now.plusSeconds(60).toString()))
        assertEquals(1, dao.rows(owner.value!!).size)
    }

    @Test fun closeOnlyServiceCanFillMissingStaffEndsButCannotChangeRecordedEndsOrOtherInput() = runBlocking {
        val people = input.performers + SaveFlightServicePerformerRequest(second, flight.id, now.toString(), now.plusSeconds(30).toString())
        val baseline = saved(record, input.copy(performers = people, attachments = listOf(
            FlightServiceAttachmentInput("dddddddd-dddd-dddd-dddd-dddddddddddd", 2, "retained.pdf", "application/pdf")))).service.copy(canEdit = false, canSetEnd = true,
            canChangeService = false, canDelete = false, canCloseOnly = true)
        live = flight.copy(services = listOf(baseline), isUnassignedSearchFlight = true, canAddServices = false)
        val desired = baseline.input().copy(endUtc = now.plusSeconds(120).toString(),
            performers = baseline.input().performers.map { it.copy(endUtc = it.endUtc ?: now.plusSeconds(60).toString()) })
        val repo = repository()

        repo.save(live, record, desired)

        val queued = dao.rows(owner.value!!).single()
        assertEquals(desired, Json.decodeFromString<SaveFlightServiceRequest>(queued.inputJson!!))
        val invalid = listOf(desired.copy(notes = "Changed core"), desired.copy(serviceId = second),
            desired.copy(attachments = desired.attachments!!.map { it.copy(fileName = "changed.pdf") }),
            desired.copy(performers = desired.performers.dropLast(1)),
            desired.copy(performers = desired.performers + SaveFlightServicePerformerRequest("99999999-9999-9999-9999-999999999999", "88888888-8888-8888-8888-888888888888", now.toString(), now.plusSeconds(60).toString())),
            desired.copy(performers = desired.performers.map { if (it.id == second) it.copy(endUtc = now.plusSeconds(45).toString()) else it }))
        for (changed in invalid) {
            assertTrue(runCatching { repo.save(live, record, changed) }.isFailure)
            assertEquals(queued, dao.rows(owner.value!!).single())
        }
        online = true
        assertFalse(repo.syncPending())
        assertEquals(desired, sent.single().second)
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun immutableCloseOnlyServiceDoesNotRequireAllowanceToAddThatService() = runBlocking {
        val baseline = saved(record, input).service.copy(canEdit = false, canSetEnd = true, canChangeService = false, canDelete = false, canCloseOnly = true)
        live = flight.copy(services = listOf(baseline), isUnassignedSearchFlight = true, allowedServiceIds = emptyList(), canAddServices = false)
        val desired = baseline.input().copy(endUtc = now.plusSeconds(120).toString(),
            performers = baseline.input().performers.map { it.copy(endUtc = now.plusSeconds(60).toString()) })
        val repo = repository()
        assertTrue(runCatching { repo.save(live, second, input) }.isFailure)
        assertTrue(dao.rows(owner.value!!).isEmpty())

        repo.save(live, record, desired)
        val queued = dao.rows(owner.value!!).single()
        assertTrue(runCatching { repo.save(live, record, desired.copy(serviceId = second)) }.isFailure)
        assertEquals(queued, dao.rows(owner.value!!).single())
        online = true
        assertFalse(repo.syncPending())
        assertEquals(desired, sent.single().second)
    }

    @Test fun serviceCannotCloseWhileAnyStaffPeriodIsOpenAndPendingInputSurvivesRejectedEdit() = runBlocking {
        val repo = repository()
        repo.save(flight, record, input)
        val original = dao.rows(owner.value!!).single()
        try {
            repo.save(flight, record, input.copy(endUtc = now.plusSeconds(60).toString()))
            fail("Expected open staff validation")
        } catch (_: IllegalStateException) { }
        assertEquals(original, dao.rows(owner.value!!).single())
        val finishedStaff = input.copy(performers = input.performers.map { it.copy(endUtc = now.toString()) })
        repo.save(flight, record, finishedStaff)
        repo.save(flight, record, finishedStaff.copy(endUtc = now.plusSeconds(60).toString()))
        assertEquals(original.changeId, dao.rows(owner.value!!).single().changeId)
        online = true
        assertFalse(repo.syncPending())
        assertNotNull(sent.single().second!!.endUtc)
        assertNotNull(sent.single().second!!.performers.single().endUtc)
    }

    @Test fun foreignServiceEndProtectsSelectionAndDeletionButNotCreatorsOtherFields() = runBlocking {
        val baseline = saved(record, input.copy(endUtc = now.plusSeconds(60).toString(),
            performers = input.performers.map { it.copy(endUtc = now.toString()) })).service.copy(
            endedByUserId = "other-user", canSetEnd = false)
        val reviewed = flight.copy(services = listOf(baseline), allowedServiceIds = listOf(input.serviceId, second))
        val repo = repository()
        try { repo.delete(reviewed, record); fail("Expected shared service protection") } catch (_: IllegalStateException) { }
        try { repo.save(reviewed, record, baseline.input().copy(serviceId = second)); fail("Expected selected service protection") }
        catch (_: IllegalStateException) { }
        assertTrue(dao.rows(owner.value!!).isEmpty())
        repo.save(reviewed, record, baseline.input().copy(notes = "my correction"))
        assertEquals("my correction", Json.decodeFromString<SaveFlightServiceRequest>(dao.rows(owner.value!!).single().inputJson!!).notes)
    }

    @Test fun pendingReviewedRightsCannotOverrideLatestForeignServiceEndProtection() = runBlocking {
        val baseline = saved(record, input.copy(performers = input.performers.map { it.copy(endUtc = now.toString()) })).service
        val reviewed = flight.copy(services = listOf(baseline), allowedServiceIds = listOf(input.serviceId, second))
        val repo = repository()
        repo.save(reviewed, record, baseline.input().copy(notes = "offline correction"))
        val pending = dao.rows(owner.value!!).single()
        val latest = reviewed.copy(services = listOf(baseline.copy(endUtc = now.plusSeconds(60).toString(),
            endedByUserId = "other-user", canSetEnd = false, canChangeService = false, canDelete = false)))
        try { repo.delete(latest, record); fail("Expected latest End protection") } catch (_: IllegalStateException) { }
        try { repo.save(latest, record, baseline.input().copy(serviceId = second)); fail("Expected latest selection protection") }
        catch (_: IllegalStateException) { }
        assertEquals(pending, dao.rows(owner.value!!).single())
    }

    @Test fun locallyPendingOwnEntryMayStillChangeItsSelectedService() = runBlocking {
        val eligible = flight.copy(allowedServiceIds = listOf(input.serviceId, second))
        val repo = repository()
        repo.save(eligible, record, input)
        val changeId = dao.rows(owner.value!!).single().changeId
        repo.save(eligible, record, input.copy(serviceId = second))
        val pending = dao.rows(owner.value!!).single()
        assertEquals(changeId, pending.changeId)
        assertEquals(second, Json.decodeFromString<SaveFlightServiceRequest>(pending.inputJson!!).serviceId)
    }

    @Test fun legacyMissingCapabilitiesMatchFreshEquivalentFlagsWhenPendingWorkSynchronizes() = runBlocking {
        val legacy = saved(record, input).service
        assertNull(legacy.canDelete)
        assertNull(legacy.canChangeService)
        val reviewed = flight.copy(services = listOf(legacy))
        val repo = repository()
        repo.save(reviewed, record, legacy.input().copy(notes = "offline edit"))
        live = reviewed.copy(services = listOf(legacy.copy(canDelete = true, canChangeService = true)))
        online = true
        assertFalse(repo.syncPending())
        assertEquals("offline edit", sent.single().second!!.notes)
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun frozenPreupgradeClosedServiceWithOpenStaffReachesServerForExactAcknowledgment() = runBlocking {
        val oldAttempt = input.copy(endUtc = now.plusSeconds(60).toString()).normalized()
        assertFalse(oldAttempt.validationErrors(v1).isEmpty())
        val frozen = Json.encodeToString(oldAttempt)
        dao.put(FlightServicePendingEntity(owner.value!!, flight.id, record, flight.flightNumber, "old-attempt", "save",
            frozen, v1, null, now.toString(), attemptOperation = "save", attemptInputJson = frozen,
            attemptRowVersion = v1, attemptAttachmentFilesJson = "{}"))
        online = true
        assertFalse(repository().syncPending())
        assertEquals(oldAttempt, sent.single().second)
        assertEquals(v1, sent.single().third)
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun frozenPreupgradeSelectionCorrectionWithForeignEndReachesServerForExactAcknowledgment() = runBlocking {
        val baseline = saved(record, input.copy(endUtc = now.plusSeconds(60).toString(),
            performers = input.performers.map { it.copy(endUtc = now.toString()) })).service.copy(
            endedByUserId = "other-user", canSetEnd = false)
        val oldAttempt = baseline.input().copy(serviceId = second)
        val frozen = Json.encodeToString(oldAttempt)
        dao.put(FlightServicePendingEntity(owner.value!!, flight.id, record, flight.flightNumber, "old-attempt", "save",
            frozen, v1, Json.encodeToString(baseline), now.toString(), attemptOperation = "save", attemptInputJson = frozen,
            attemptRowVersion = v1, attemptAttachmentFilesJson = "{}"))
        online = true
        assertFalse(repository().syncPending())
        assertEquals(oldAttempt, sent.single().second)
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun localUnsentCreateCanBeRemovedWithoutSendingAnyMutation() = runBlocking {
        val repo = repository()
        repo.save(flight, record, input)
        repo.delete(flight, record)
        assertTrue(dao.rows(owner.value!!).isEmpty())
        online = true
        assertFalse(repo.syncPending())
        assertTrue(sent.isEmpty())
    }

    @Test fun attachmentsSurviveRestartAndAnInFlightEditRetainsBothDesiredAndAttemptBytes() = runBlocking {
        val a = "77777777-7777-7777-7777-777777777777"
        val b = "88888888-8888-8888-8888-888888888888"
        fun attachment(id: String, text: String) = FlightServiceAttachmentInput(id, 2, "file.pdf", "application/pdf",
            content = Base64.getEncoder().encodeToString(text.toByteArray()))
        repository().save(flight, record, input.copy(attachments = listOf(attachment(a, "%PDF-a"))))
        assertEquals(1, files.content.size)
        val started = CompletableDeferred<Unit>(); val release = CompletableDeferred<Unit>()
        sender = { _, id, value, version -> started.complete(Unit); release.await(); saved(id, value, if (version == v1) v2 else v3) }
        val repo = repository(); online = true
        val draining = async { repo.syncPending() }
        started.await()
        repo.save(flight, record, input.copy(attachments = listOf(attachment(b, "%PDF-b"))))
        assertEquals(2, files.content.size)
        release.complete(Unit)
        assertTrue(draining.await())
        assertEquals(1, files.content.size)
        assertTrue(String(files.content.values.single()).endsWith("b"))
        assertFalse(repo.syncPending())
        assertTrue(files.content.isEmpty())
    }

    @Test fun failedFileImportDoesNotPublishPartOfABatchOrDeleteExistingAttemptFiles() = runBlocking {
        val attachment = FlightServiceAttachmentInput("77777777-7777-7777-7777-777777777777", 2, "file.pdf", "application/pdf",
            content = Base64.getEncoder().encodeToString("%PDF-a".toByteArray()))
        val repo = repository(); repo.save(flight, record, input.copy(attachments = listOf(attachment)))
        online = true; fail = IOException("lost response"); repo.syncPending()
        val before = dao.rows(owner.value!!).single()
        try {
            val missing = input.copy(attachments = listOf(attachment.copy(content = null, contentUri = "missing")))
            repo.saveBatch(flight, listOf(FlightServiceChange(record, input.copy(notes = "changed")), FlightServiceChange(second, missing)))
            fail("Expected import failure")
        } catch (_: IOException) { }
        assertEquals(before, dao.rows(owner.value!!).single())
        assertEquals(1, files.content.size)
    }

    @Test fun missingQueuedAttachmentRequiresReviewWithoutSendingOrRetryingForever() = runBlocking {
        val attachment = FlightServiceAttachmentInput("77777777-7777-7777-7777-777777777777", 2, "file.pdf", "application/pdf",
            content = Base64.getEncoder().encodeToString("%PDF-a".toByteArray()))
        val repo = repository()
        repo.save(flight, record, input.copy(attachments = listOf(attachment)))
        files.content.clear()
        online = true
        assertFalse(repo.syncPending())
        assertEquals("failed", dao.rows(owner.value!!).single().status)
        assertTrue(sent.isEmpty())
    }

    @Test fun acknowledgedPreviewGetsExactBytesBeforeCleanupAndCacheFailureCannotUndoSave() = runBlocking {
        val content = "%PDF-a".toByteArray()
        val attachment = FlightServiceAttachmentInput("77777777-7777-7777-7777-777777777777", 2, "file.pdf", "application/pdf",
            content = Base64.getEncoder().encodeToString(content))
        val repo = repository()
        repo.save(flight, record, input.copy(attachments = listOf(attachment)))
        var called = false
        cacheAcknowledged = { _, _, bytes ->
            called = true
            assertArrayEquals(content, bytes[attachment.id])
            assertEquals(1, files.content.size)
            throw IOException("optional preview cache unavailable")
        }
        online = true
        assertFalse(repo.syncPending())
        assertTrue(called)
        assertTrue(dao.rows(owner.value!!).isEmpty())
        assertTrue(files.content.isEmpty())
    }

    @Test fun ownerTransitionDuringSendCannotAcknowledgeOrReleaseOldOwnersIntent() = runBlocking {
        val started = CompletableDeferred<Unit>(); val release = CompletableDeferred<Unit>()
        sender = { _, id, value, _ -> started.complete(Unit); release.await(); saved(id, value) }
        val repo = repository()
        repo.save(flight, record, input)
        online = true
        val draining = async { repo.syncPending() }
        started.await()
        owner.value = "other-api|account"
        release.complete(Unit)
        assertFalse(draining.await())
        assertTrue(live.services.isEmpty())
        assertEquals(1, dao.rows("api|account").size)
        assertTrue(repo.changes.first().isEmpty())
    }

    @Test fun taskPermissionRetainsSharedDraftWithoutGrantingServicePublication() = runBlocking {
        val taskOnly = flight.copy(canSaveServices = false, allowedServiceIds = null, canSaveTasks = true)
        val draft = FlightServiceFormDraft("""{"tasks":[{"id":"$record","reference":"keep"}]}""")
        val repo = repository()
        repo.saveBatch(taskOnly, emptyList(), draft)
        assertEquals(draft.json, repository().formDraft(flight.id)!!.formJson)
        assertTrue(dao.rows(owner.value!!).isEmpty())
        try { repo.save(taskOnly, record, input); fail("Expected service access restriction") }
        catch (_: IllegalStateException) { }
        assertEquals(draft.json, repository().formDraft(flight.id)!!.formJson)
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun unstartedPlannedRowsAndExplicitRemovalsSurviveRestartWithoutPendingHttpWork() = runBlocking {
        val value = """{"services":[{"id":"$record","start":""}],"removedPlannedIds":["removed"]}"""
        val repo = repository()
        repo.saveBatch(flight, emptyList(), FlightServiceFormDraft(value))
        assertEquals(value, repository().formDraft(flight.id)!!.formJson)
        assertEquals(1, repository().drafts.first().size)
        assertFalse(repo.hasSavedInput(flight.id))
        online = true
        assertFalse(repo.syncPending())
        assertTrue(sent.isEmpty())
    }

    @Test fun startingDraftTransfersTheSameDurableFileIntoPendingIntentAtomically() = runBlocking {
        val attachment = FlightServiceAttachmentInput("77777777-7777-7777-7777-777777777777", 2, "file.pdf", "application/pdf",
            content = Base64.getEncoder().encodeToString("%PDF-a".toByteArray()))
        val repo = repository()
        repo.saveBatch(flight, emptyList(), FlightServiceFormDraft("{}", listOf(FlightServiceFormDraftAttachment(record, attachment))))
        val originalKey = files.content.keys.single()
        repo.saveBatch(flight, listOf(FlightServiceChange(record, input.copy(attachments = listOf(attachment.copy(content = null))))),
            FlightServiceFormDraft("{}"))
        assertEquals(setOf(originalKey), files.content.keys)
        assertEquals("{}", repo.formDraft(flight.id)!!.attachmentFilesJson)
        assertTrue(dao.rows(owner.value!!).single().attachmentFilesJson.contains(originalKey))
        online = true
        assertFalse(repo.syncPending())
        assertTrue(files.content.isEmpty())
    }

    @Test fun queueAcknowledgmentCannotDeleteFilesStillReferencedByLocalDraft() = runBlocking {
        val attachment = FlightServiceAttachmentInput("77777777-7777-7777-7777-777777777777", 2, "file.pdf", "application/pdf",
            content = Base64.getEncoder().encodeToString("%PDF-a".toByteArray()))
        val repo = repository()
        repo.saveBatch(flight, listOf(FlightServiceChange(record, input.copy(attachments = listOf(attachment)))),
            FlightServiceFormDraft("{}", listOf(FlightServiceFormDraftAttachment(record, attachment.copy(content = null)))))
        assertEquals(1, files.content.size)
        online = true
        assertFalse(repo.syncPending())
        assertEquals(1, files.content.size)
        repo.saveBatch(live, emptyList(), FlightServiceFormDraft("{}"))
        assertTrue(files.content.isEmpty())
    }

    @Test fun failingBatchCannotReplaceAnEarlierDraftOrItsMedia() = runBlocking {
        val repo = repository()
        repo.saveBatch(flight, emptyList(), FlightServiceFormDraft("{\"removedPlannedIds\":[\"keep\"]}"))
        val previous = repo.formDraft(flight.id)
        try {
            repo.saveBatch(flight, listOf(FlightServiceChange(record, input.copy(performers = emptyList()))), FlightServiceFormDraft("{}"))
            fail("Expected complete-batch validation")
        } catch (_: IllegalStateException) { }
        assertEquals(previous, repo.formDraft(flight.id))
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun strictValidationRetainsActualBoundsUnknownNotesAndEligibleCatalogRules() {
        assertTrue("startUtc" in input.copy(startUtc = now.minusSeconds(60).toString()).validationErrors(v1, actualArrivalUtc = now.toString()))
        assertTrue("endUtc" in input.copy(endUtc = now.plusSeconds(120).toString()).validationErrors(v1, actualDepartureUtc = now.plusSeconds(60).toString()))
        assertTrue("performers.${input.performers.single().id}.startUtc" in input.copy(performers = input.performers.map { it.copy(startUtc = now.plusSeconds(120).toString()) })
            .validationErrors(v1, actualDepartureUtc = now.plusSeconds(60).toString()))
        assertTrue("notes" in input.copy(serviceId = "40000000-0000-0000-0000-000000000003", notes = " ").validationErrors(v1))
        assertTrue("serviceId" in input.validationErrors(v1, allowedServiceIds = emptySet()))
        assertTrue(input.validationErrors(v1, actualArrivalUtc = now.toString(), actualDepartureUtc = now.toString(), allowedServiceIds = setOf(input.serviceId)).isEmpty())
    }

    @Test fun performerValidationReportsIndividualIdentityAndPeriodFieldsWithoutFalseMissingStaffErrors() {
        val performer = input.performers.single()
        val prefix = "performers.${performer.id}."
        val cases = listOf(
            performer.copy(staffMemberId = "invalid-staff") to setOf(prefix + "staffMemberId"),
            performer.copy(id = "invalid-row") to setOf("performers.invalid-row.id"),
            performer.copy(startUtc = "not a time") to setOf(prefix + "startUtc"),
            performer.copy(startUtc = "0001-01-01T00:00:00Z") to setOf(prefix + "startUtc"),
            performer.copy(startUtc = now.minusSeconds(1).toString()) to setOf(prefix + "startUtc"),
            performer.copy(endUtc = "not a time") to setOf(prefix + "endUtc"),
            performer.copy(endUtc = now.minusSeconds(1).toString()) to setOf(prefix + "endUtc"),
        )
        cases.forEach { (value, expected) ->
            assertEquals(expected, input.copy(performers = listOf(value)).validationErrors(v1))
        }
        assertEquals(setOf("performers"), input.copy(performers = emptyList()).validationErrors(v1))
        assertEquals(setOf("performers"), input.copy(performers = listOf(performer,
            performer.copy(id = "77777777-7777-7777-7777-777777777777"))).validationErrors(v1))
        assertEquals(setOf("performers"), input.copy(performers = listOf(performer,
            performer.copy(staffMemberId = "88888888-8888-8888-8888-888888888888"))).validationErrors(v1))
        assertTrue(input.copy(endUtc = now.toString(), performers = listOf(performer.copy(endUtc = now.toString())))
            .validationErrors(v1, now.toString(), now.toString()).isEmpty())
    }

    @Test fun freshEligibilityIsRequiredBeforeTheFirstAttemptButCannotReplaceAnUnknownAttempt() = runBlocking {
        val repo = repository()
        repo.save(flight, record, input)
        live = flight.copy(allowedServiceIds = emptyList())
        online = true
        assertFalse(repo.syncPending())
        assertEquals("failed", dao.rows(owner.value!!).single().status)
        assertTrue(sent.isEmpty())
        live = flight
        repo.retry(flight.id, record)
        fail = IOException("response lost")
        assertTrue(repo.syncPending())
        live = flight.copy(allowedServiceIds = emptyList())
        fail = null
        assertFalse(repo.syncPending())
        assertEquals(listOf(v1, v1), sent.map { it.third })
    }

    @Test fun olderScopeSnapshotsCannotPublishEvenDraftOnlyState() = runBlocking {
        val repo = repository()
        try {
            repo.saveBatch(flight.copy(allowedServiceIds = null), emptyList(), FlightServiceFormDraft("{}"))
            fail("Expected current scope metadata")
        } catch (_: IllegalStateException) { }
        assertTrue(repo.drafts.first().isEmpty())
        assertTrue(dao.rows(owner.value!!).isEmpty())
    }

    @Test fun lostReplyBlocksLaterSameFlightRowsAndFrozenAttemptWinsAfterRestartDespiteLaterSavedTime() = runBlocking {
        var loseReply = true
        sender = { _, id, value, version ->
            sent += Triple(id, value, version)
            val result = saved(id, value, if (id == record) v2 else v3)
            if (loseReply) {
                loseReply = false
                live = live.copy(rowVersion = v2, services = listOf(result.service))
                throw IOException("committed response lost")
            }
            result
        }
        val repo = repository()
        repo.saveBatch(flight, listOf(FlightServiceChange(record, input), FlightServiceChange(second, input)))
        online = true
        assertTrue(repo.syncPending())
        assertEquals(listOf(record), sent.map { it.first })
        assertTrue(dao.rows(owner.value!!).all { it.status == "pending" })
        val attempted = dao.rows(owner.value!!).first { it.serviceRecordId == record }
        dao.put(attempted.copy(savedAtUtc = now.plusSeconds(60).toString()))
        assertFalse(repository().syncPending())
        assertEquals(listOf(record, record, second), sent.map { it.first })
        assertEquals(listOf(v1, v1, v2), sent.map { it.third })
    }

    @Test fun callbackFailureStaysReplayableAndSiblingVersionIsDurableBeforeAcknowledgedRowDeletion() = runBlocking {
        val repo = repository()
        repo.saveBatch(flight, listOf(FlightServiceChange(record, input), FlightServiceChange(second, input)))
        beforeDelete = { _, _, _ -> throw IllegalStateException("dependent acknowledgment persistence failed") }
        online = true
        assertTrue(repo.syncPending())
        assertEquals(listOf(record), sent.map { it.first })
        assertTrue(dao.rows(owner.value!!).all { it.status == "pending" })
        beforeDelete = { _, _, _ -> }
        var proofDeleted = false
        dao.beforeDelete = { row ->
            if (row.serviceRecordId == record) {
                proofDeleted = true
                assertEquals(v2, dao.rows(owner.value!!).first { it.serviceRecordId == second }.rowVersion)
            }
        }
        assertFalse(repository().syncPending())
        assertTrue(proofDeleted)
    }

    @Test fun equivalentServerOffsetsDoNotMisclassifyUnchangedCoreOrProtectedEnd() = runBlocking {
        val offset = "2026-11-10T12:00:00+00:00"
        val end = "2026-11-10T12:01:00+00:00"
        val baseline = saved(record, input.copy(startUtc = offset, endUtc = end,
            performers = input.performers.map { it.copy(startUtc = offset, endUtc = offset) })).service.copy(canSetEnd = false)
        val repo = repository()
        val editorPeriods = input.performers.map { it.copy(startUtc = now.toString(), endUtc = now.toString()) }
        repo.save(flight.copy(services = listOf(baseline)), record, baseline.input().copy(startUtc = now.toString(),
            endUtc = now.plusSeconds(60).toString(), performers = editorPeriods, notes = "creator core correction"))
        repo.clear()
        val readonly = baseline.copy(endUtc = null, canEdit = false, canSetEnd = true)
        repo.save(flight.copy(services = listOf(readonly)), record, readonly.input().copy(startUtc = now.toString(),
            endUtc = now.plusSeconds(60).toString(), performers = editorPeriods))
        assertEquals(1, dao.rows(owner.value!!).size)
    }

    @Test fun headersRejectedByTheServerCannotReachHttpAndAndroidM4aAliasIsCanonicalized() = runBlocking {
        val bad = listOf(
            Triple(0, "image/png", byteArrayOf(137.toByte(), 80, 78, 71, 13, 10, 26, 10)),
            Triple(0, "image/jpeg", byteArrayOf(255.toByte(), 216.toByte(), 255.toByte())),
            Triple(1, "audio/mpeg", "ID3".toByteArray()),
            Triple(1, "audio/mp4", byteArrayOf(0, 0, 0, 0, 102, 116, 121, 112)),
        )
        val repo = repository()
        bad.forEach { (kind, mime, content) ->
            val attachment = FlightServiceAttachmentInput("77777777-7777-7777-7777-777777777777", kind, "file", mime,
                content = Base64.getEncoder().encodeToString(content))
            try { repo.save(flight, record, input.copy(attachments = listOf(attachment))); fail("Expected malformed header rejection") }
            catch (_: IllegalStateException) { }
            assertTrue(dao.rows(owner.value!!).isEmpty())
            assertTrue(files.content.isEmpty())
        }
        val valid = byteArrayOf(0, 0, 0, 0, 102, 116, 121, 112, 0, 0, 0, 0)
        val attachment = FlightServiceAttachmentInput("77777777-7777-7777-7777-777777777777", 1, "file.m4a", " audio/x-m4a ",
            content = Base64.getEncoder().encodeToString(valid))
        repo.save(flight, record, input.copy(attachments = listOf(attachment)))
        online = true
        assertFalse(repo.syncPending())
        assertEquals("audio/mp4", sent.single().second!!.attachments!!.single().contentType)
    }

    @Test fun retainedServerFilesDoNotConsumeTheInlineUploadAggregateBudget() = runBlocking {
        val old = listOf("77777777-7777-7777-7777-777777777777", "88888888-8888-8888-8888-888888888888").map {
            FlightServiceAttachmentRecord(it, 1, "voice.m4a", "audio/mp4", 25L * 1024 * 1024)
        }
        val baseline = saved(record, input).service.copy(attachments = old)
        val added = FlightServiceAttachmentInput("99999999-9999-9999-9999-999999999999", 2, "file.pdf", "application/pdf",
            content = Base64.getEncoder().encodeToString("%PDF-a".toByteArray()))
        val repo = repository()
        repo.save(flight.copy(services = listOf(baseline)), record, baseline.input().copy(attachments = baseline.input().attachments.orEmpty() + added))
        assertEquals(1, dao.rows(owner.value!!).size)
        assertEquals(1, files.content.size)
    }

    @Test fun freshServicesWaitForFrozenDepartureButFrozenServiceReplayCannotBeBlockedByNewIntent() = runBlocking {
        val repo = repository()
        repo.save(flight, record, input)
        online = true; departureAttempt = true
        assertFalse(repo.syncPending())
        assertTrue(sent.isEmpty())
        departureAttempt = false; fail = IOException("lost response")
        assertTrue(repo.syncPending())
        assertTrue(repo.hasUnacknowledgedAttempt(flight.id))
        arrival = true; departureAttempt = true; fail = null
        assertFalse(repo.syncPending())
        assertEquals(2, sent.size)
        assertFalse(repo.hasUnacknowledgedAttempt(flight.id))
    }

    @Test fun retainedMimeCasingDoesNotBecomeAFileReplacementDuringCanonicalization() = runBlocking {
        val retained = FlightServiceAttachmentRecord("77777777-7777-7777-7777-777777777777", 0, "file.png", "IMAGE/PNG", 12)
        val baseline = saved(record, input).service.copy(attachments = listOf(retained))
        val repo = repository()
        repo.save(flight.copy(services = listOf(baseline)), record, baseline.input().copy(notes = "core correction"))
        val persisted = Json.decodeFromString<SaveFlightServiceRequest>(dao.rows(owner.value!!).single().inputJson!!)
        assertEquals("image/png", persisted.attachments!!.single().contentType)
        assertTrue(files.content.isEmpty())
    }

    @Test fun durableParentRelaxationPermitsLocalSaveButCannotBypassSavedHttpBounds() = runBlocking {
        val employee = AssignedEmployee(staff, "Employee", "E1")
        val departure = FlightDepartureSubmission("departure", employee, now.plusSeconds(60).toString(), employee,
            isAccepted = true, createdAtUtc = now.toString())
        live = flight.copy(departureSubmissions = listOf(departure))
        localBounds = FlightServiceActualBounds(null, now.plusSeconds(180).toString())
        val repo = repository()
        repo.save(live, record, input.copy(endUtc = now.plusSeconds(120).toString(),
            performers = input.performers.map { it.copy(endUtc = now.plusSeconds(120).toString()) }))
        online = true; departureAttempt = true
        assertFalse(repo.syncPending())
        assertTrue(sent.isEmpty())
        departureAttempt = false
        assertFalse(repo.syncPending())
        assertEquals("failed", dao.rows(owner.value!!).single().status)
        assertTrue(sent.isEmpty())
        repo.retry(flight.id, record)
        live = live.copy(rowVersion = v2, departureSubmissions = listOf(departure.copy(atd = now.plusSeconds(180).toString())))
        repo.onPriorWriteAcknowledged(flight.id, v1, v2)
        assertFalse(repo.syncPending())
        assertEquals(v2, sent.single().third)
    }
}

private class ServiceMemoryDao : FlightServiceDao {
    var beforeDelete: suspend (FlightServicePendingEntity) -> Unit = { }
    private val state = MutableStateFlow<List<FlightServicePendingEntity>>(emptyList())
    private val drafts = MutableStateFlow<List<FlightServiceFormDraftEntity>>(emptyList())
    override fun observe(owner: String): Flow<List<FlightServicePendingEntity>> = state.map { list -> list.filter { it.ownerSubject == owner } }
    override suspend fun rows(owner: String) = state.value.filter { it.ownerSubject == owner }.sortedWith(compareBy({ it.savedAtUtc }, { it.flightId }, { it.serviceRecordId }))
    override suspend fun allRows() = state.value
    override fun observeDrafts(owner: String): Flow<List<FlightServiceFormDraftEntity>> = drafts.map { list -> list.filter { it.ownerSubject == owner } }
    override suspend fun drafts(owner: String) = drafts.value.filter { it.ownerSubject == owner }
    override suspend fun allDrafts() = drafts.value
    override suspend fun putDraft(row: FlightServiceFormDraftEntity) {
        drafts.value = drafts.value.filterNot { it.ownerSubject == row.ownerSubject && it.flightId == row.flightId } + row
    }
    override suspend fun put(row: FlightServicePendingEntity) { state.value = state.value.filterNot {
        it.ownerSubject == row.ownerSubject && it.flightId == row.flightId && it.serviceRecordId == row.serviceRecordId
    } + row }
    override suspend fun delete(owner: String, flightId: String, recordId: String, changeId: String) {
        state.value.firstOrNull { it.ownerSubject == owner && it.flightId == flightId && it.serviceRecordId == recordId && it.changeId == changeId }?.let { beforeDelete(it) }
        state.value = state.value.filterNot { it.ownerSubject == owner && it.flightId == flightId && it.serviceRecordId == recordId && it.changeId == changeId }
    }
    override suspend fun deleteForFlight(owner: String, flightId: String) { state.value = state.value.filterNot { it.ownerSubject == owner && it.flightId == flightId } }
    override suspend fun clear() { state.value = emptyList() }
    override suspend fun deleteDraft(owner: String, flightId: String) { drafts.value = drafts.value.filterNot { it.ownerSubject == owner && it.flightId == flightId } }
    override suspend fun clearDrafts() { drafts.value = emptyList() }
}
private class ServiceMemoryFiles : ServiceAttachmentStore {
    val content = linkedMapOf<String, ByteArray>()
    private var sequence = 0
    override suspend fun save(content: ByteArray): String = "file-${++sequence}".also { this.content[it] = content.copyOf() }
    override suspend fun import(uri: String): String = throw IOException("Source unavailable")
    override suspend fun read(key: String): ByteArray = content[key]?.copyOf() ?: throw IOException("File missing")
    override suspend fun retainOnly(referenced: Set<String>) { content.keys.retainAll(referenced) }
}
