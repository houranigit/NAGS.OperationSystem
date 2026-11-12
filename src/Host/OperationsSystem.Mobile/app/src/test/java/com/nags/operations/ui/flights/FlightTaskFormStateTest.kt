package com.nags.operations.ui.flights

import com.nags.operations.R
import com.nags.operations.data.*
import com.nags.operations.data.db.entities.FlightTaskPendingEntity
import java.time.ZoneId
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import org.junit.Assert.*
import org.junit.Test

class FlightTaskFormStateTest {
    private val recordId = "11111111-1111-1111-1111-111111111111"
    private val staffId = "22222222-2222-2222-2222-222222222222"
    private val performerId = "33333333-3333-3333-3333-333333333333"
    private val resourceId = "44444444-4444-4444-4444-444444444444"
    private val resourceRecordId = "55555555-5555-5555-5555-555555555555"
    private val chapterId = "66666666-6666-6666-6666-666666666666"
    private val pdfId = "77777777-7777-7777-7777-777777777777"
    private val start = "2026-11-11T11:00:00Z"
    private val end = "2026-11-11T12:00:00Z"
    private val zone = ZoneId.of("Etc/UTC")
    private val catalogs = FlightOperationsCatalogs(stationStaff = listOf(StaffCatalogItem(staffId,
        "Current employee", "E1", "station", "AAA", "Engineer")), tools = listOf(ResourceCatalogItem(resourceId, "Tool", "Duration")),
        materials = listOf(ResourceCatalogItem(resourceId, "Material", "Quantity")))
    private val record = FlightTaskRecord(recordId, 0, start, performers = listOf(FlightTaskPerformerRecord(performerId,
        staffId, "Original employee", "E1", start)), createdByUserId = "creator", createdAtUtc = start,
        canEdit = true, canSetEnd = true, canDelete = true, canChangeType = true)
    private val flight = FlightDetail("flight", "SV100", "customer", customerName = "Airline", stationId = "station",
        stationIata = "AAA", stationName = "Station", operationTypeId = "operation", operationTypeName = "Transit",
        scheduledArrivalUtc = "2026-11-11T10:00:00Z", scheduledDepartureUtc = "2026-11-11T20:00:00Z",
        isPerLanding = false, createdAtUtc = "2026-11-11T08:00:00Z", rowVersion = "AAAAAAAAAAE=",
        stationTimeZoneId = zone.id, currentStaffMemberId = staffId, tasks = listOf(record), canSaveTasks = true)

    private fun draft(value: FlightTaskRecord = record) = initialTaskEditorDraft(flight.copy(tasks = listOf(value)),
        catalogs, emptyList(), null, zone).single()

    private fun pending(input: SaveFlightTaskRequest?, operation: String = "save") = FlightTaskPendingEntity("owner", flight.id,
        recordId, flight.flightNumber, "change", operation, input?.let { Json.encodeToString(it) }, flight.rowVersion,
        Json.encodeToString(record), start)

    @Test fun legacyQuantityPeriodsStayVisibleUntilAValidExplicitQuantityConfirmsTheirReplacement() {
        val period = FlightWorkPeriod("2026-11-11T11:15", "2026-11-11T11:45")
        val legacy = FlightResourceDraft(resourceId = resourceId, resourceName = "Material", period = period)
        assertTrue(taskResourceQuantityReviewRequired(legacy, 0))
        val invalid = taskResourceQuantityEdited(legacy, "1.001")
        assertEquals(period, invalid.period)
        assertNull(invalid.calculationType)
        assertEquals(period, taskResourceQuantityEdited(invalid, "").period)
        assertEquals(period, taskResourceQuantityEdited(legacy, "1E100000000").period)
        val reviewed = taskResourceQuantityEdited(legacy, "9999999999999999.99")
        assertEquals(0, reviewed.calculationType)
        assertEquals("9999999999999999.99", reviewed.quantity)
        assertEquals(FlightWorkPeriod(), reviewed.period)
        assertFalse(taskResourceQuantityReviewRequired(reviewed, 0))
    }

    @Test fun resourceDefaultsFollowCatalogAndKeepAnExistingCapturedUsageWhenReselected() {
        val parent = FlightWorkPeriod("2026-11-11T11:00", "2026-11-11T12:00", start, end)
        val quantity = taskResourceSelection(FlightResourceDraft(), catalogs.materials.single(), parent, zone, FlightWorkTimeBounds())
        assertEquals("1", quantity.quantity)
        assertEquals(0, quantity.calculationType)
        assertEquals(FlightWorkPeriod(), quantity.period)
        val duration = taskResourceSelection(FlightResourceDraft(), catalogs.tools.single(), parent, zone, FlightWorkTimeBounds())
        assertEquals(parent, duration.period)
        assertEquals(1, duration.calculationType)
        assertEquals("", duration.quantity)
        assertEquals(duration, taskResourceSelection(duration, catalogs.tools.single().copy(calculationType = "Quantity"),
            parent, zone, FlightWorkTimeBounds()))
    }

    @Test fun invalidServiceStyleClosingErrorsStayOnTaskAndOpenStaffFields() {
        val item = draft().copy(period = FlightWorkPeriod("2026-11-11T11:00", "2026-11-11T12:00"))
        val errors = taskEditorValidation(item, item.taskInput(zone), flight, catalogs, flight.rowVersion, null, null)
        assertEquals(R.string.task_validation_open_periods, errors["endUtc"])
        assertEquals(R.string.task_validation_staff_end_required, errors["performers.$performerId.endUtc"])
        assertFalse(errors.containsKey("performers"))
        val closed = item.copy(employees = item.employees.map { it.copy(period = it.period.copy(end = "2026-11-11T12:00")) })
        assertTrue(taskEditorValidation(closed, closed.taskInput(zone), flight, catalogs, flight.rowVersion, null, null).isEmpty())
    }

    @Test fun unchangedSavedSnapshotsRetainChapterAndResourceMetadataAfterCatalogChanges() {
        val published = record.copy(ataChapterId = chapterId, ataChapterCode = "12", ataChapterTitle = "Recorded title",
            tools = listOf(FlightTaskResourceRecord(resourceRecordId, resourceId, "Recorded tool", 1, startUtc = start)))
        val changedCatalog = catalogs.copy(tools = listOf(ResourceCatalogItem(resourceId, "Renamed tool", "Quantity")),
            ataChapters = listOf(AtaChapterCatalogItem(chapterId, "category", "Category", "99", "Renamed chapter")))
        val item = initialTaskEditorDraft(flight.copy(tasks = listOf(published)), changedCatalog, emptyList(), null, zone).single()
        assertEquals("Recorded title", item.ataChapterTitle)
        assertEquals("Recorded tool", item.tools.single().resourceName)
        assertEquals(1, item.tools.single().calculationType)
        assertEquals(published.input(), item.taskInput(zone))
        assertTrue(taskEditorValidation(item, item.taskInput(zone), flight.copy(tasks = listOf(published)),
            changedCatalog.copy(ataChapters = emptyList()), flight.rowVersion, null, null).isEmpty())
    }

    @Test fun pendingInputRetainsPdfForNullAttachmentListButRespectsExplicitRemovalAndDeletion() {
        val published = record.copy(attachments = listOf(FlightServiceAttachmentRecord(pdfId, 2, "task.pdf", "application/pdf", 594,
            uploadedAtUtc = end)))
        val withSaved = flight.copy(tasks = listOf(published))
        val retained = initialTaskEditorDraft(withSaved, catalogs, listOf(pending(published.input().copy(attachments = null))), null, zone).single()
        assertEquals(pdfId, retained.attachments.single().id)
        assertEquals(end, retained.attachments.single().uploadedAtUtc)
        val removed = initialTaskEditorDraft(withSaved, catalogs, listOf(pending(published.input().copy(attachments = emptyList()))), null, zone).single()
        assertTrue(removed.attachments.isEmpty())
        val stored = Json.encodeToString(FlightServiceEditorDraft(zone.id, tasks = listOf(retained)))
        assertTrue(initialTaskEditorDraft(withSaved, catalogs, listOf(pending(null, "delete")), stored, zone).isEmpty())
    }

    @Test fun taskFingerprintIgnoresHydrationAndDisplayChangesWithoutHidingActualEdits() {
        val original = draft().copy(attachments = listOf(FlightAttachmentDraft(pdfId, "", "task.pdf", "application/pdf", uploadedAtUtc = end)),
            tools = listOf(FlightResourceDraft(resourceId = resourceId, resourceName = "Old label", period = FlightWorkPeriod("2026-11-11T11:00"), calculationType = 1)))
        val hydrated = original.copy(ataChapterCode = "new display", ataChapterTitle = "new title",
            employees = original.employees.map { it.copy(employeeName = "Renamed employee", employeeCode = "changed") },
            tools = original.tools.map { it.copy(resourceName = "Renamed tool") },
            attachments = original.attachments.map { it.copy(uri = "content://private-preview", ownedCapture = true) })
        assertEquals(taskEditorFingerprint(listOf(original)), taskEditorFingerprint(listOf(hydrated)))
        assertNotEquals(taskEditorFingerprint(listOf(original)), taskEditorFingerprint(listOf(hydrated.copy(remarks = "Changed notes"))))
        val changedPeriod = hydrated.copy(tools = hydrated.tools.map { it.copy(period = it.period.copy(end = "2026-11-11T12:00")) })
        assertNotEquals(taskEditorFingerprint(listOf(original)), taskEditorFingerprint(listOf(changedPeriod)))
    }

    @Test fun rawUnstartedDraftsRezoneWithoutDiscardingInvalidValuesAndAreNotPublished() {
        val local = FlightTaskDraft(recordId, "Major", period = FlightWorkPeriod("bad date"), remarks = "Local",
            materials = listOf(FlightResourceDraft(resourceId = resourceId, period = FlightWorkPeriod("2026-11-11T11:00"))))
        val stored = Json.encodeToString(FlightServiceEditorDraft(zone.id, tasks = listOf(local)))
        val restored = initialTaskEditorDraft(flight.copy(tasks = emptyList()), catalogs, emptyList(), stored, ZoneId.of("Asia/Riyadh")).single()
        assertEquals("bad date", restored.period.start)
        assertEquals("2026-11-11T14:00", restored.materials.single().period.start)
        assertNull(restored.materials.single().calculationType)
        val unstarted = restored.copy(period = FlightWorkPeriod())
        assertTrue(taskEditorChanges(listOf(unstarted), emptySet(), emptyMap(), emptyList(), emptySet(), emptySet(), zone).isEmpty())
        val invalidChanges = taskEditorChanges(listOf(restored), emptySet(), emptyMap(), emptyList(), emptySet(), emptySet(), zone)
        assertEquals(1, invalidChanges.size)
        assertTrue(invalidChanges.single().input!!.startUtc.isBlank())
    }

    @Test fun departureRequiresStartedTaskStaffAndDurationResourcesClosedButExcludesQuantityAndUnstartedDrafts() {
        val open = draft()
        val errors = departureTaskCompletionValidation(listOf(open, FlightTaskDraft()), listOf(record), emptyList(), emptyList(), zone)
        assertEquals(R.string.departure_task_end_required, errors[recordId]?.get("endUtc"))
        assertEquals(R.string.departure_task_staff_end_required, errors[recordId]?.get("performers.$performerId.endUtc"))
        val closed = open.copy(period = open.period.copy(end = "2026-11-11T12:00"),
            employees = open.employees.map { it.copy(period = it.period.copy(end = "2026-11-11T12:00")) },
            tools = listOf(FlightResourceDraft(resourceRecordId, resourceId, "Tool", FlightWorkPeriod("2026-11-11T14:00"), calculationType = 1)),
            materials = listOf(FlightResourceDraft(chapterId, resourceId, "Material", calculationType = 0, quantity = "2.25")))
        val change = FlightTaskChange(recordId, closed.taskInput(zone))
        val incomplete = departureTaskCompletionValidation(listOf(closed), listOf(record), emptyList(), listOf(change), zone)[recordId]!!
        assertEquals(mapOf("tools.$resourceRecordId.endUtc" to R.string.departure_task_resource_end_required), incomplete)
        val formErrors = taskEditorValidation(closed, change.input!!, flight, catalogs, flight.rowVersion, start, "2026-11-11T13:00:00Z")
        assertEquals(R.string.task_validation_open_periods, formErrors["endUtc"])
        assertEquals(R.string.task_validation_resource_end_required, formErrors["tools.$resourceRecordId.endUtc"])
        assertFalse(formErrors.containsKey("tools.$resourceRecordId.startUtc"))
        assertTrue(taskActualBoundsValidation(listOf(closed), zone, start, "2026-11-11T13:00:00Z").isEmpty())
        val complete = closed.copy(tools = closed.tools.map { it.copy(period = FlightWorkPeriod("2026-11-11T11:00", "2026-11-11T12:00")) })
        assertTrue(departureTaskCompletionValidation(listOf(complete, FlightTaskDraft()), listOf(record), emptyList(),
            listOf(FlightTaskChange(recordId, complete.taskInput(zone))), zone).isEmpty())
        assertTrue(taskEditorValidation(complete, complete.taskInput(zone), flight, catalogs, flight.rowVersion, start, "2026-11-11T13:00:00Z").isEmpty())
    }

    @Test fun knownTaskCannotDisappearIntoLocalDraftWhenItsStartIsCleared() {
        val original = draft()
        val baseline = mapOf(recordId to taskInputFingerprint(original.taskInput(zone)))
        val cleared = original.copy(period = FlightWorkPeriod())
        val changes = taskEditorChanges(listOf(cleared), setOf(recordId), baseline, emptyList(), emptySet(), emptySet(), zone)
        assertEquals(1, changes.size)
        assertEquals(R.string.task_validation_start, taskEditorValidation(cleared, changes.single().input!!, flight,
            catalogs, flight.rowVersion, null, null)["startUtc"])
    }

    @Test fun incompatibleQuantityInADurationDraftRemainsAvailableForReviewAndHasAVisibleFieldError() {
        val item = draft().copy(tools = listOf(FlightResourceDraft(resourceRecordId, resourceId, "Tool",
            FlightWorkPeriod("2026-11-11T11:00"), calculationType = 1, quantity = "2")))
        val input = item.taskInput(zone)
        assertEquals("2", input.tools.single().quantity)
        assertEquals(start, input.tools.single().startUtc)
        assertEquals(R.string.task_validation_duration_quantity,
            taskEditorValidation(item, input, flight, catalogs, flight.rowVersion, null, null)["tools.$resourceRecordId.quantity"])
    }

    @Test fun closingTaskRequiresDurationEndsAcrossEveryResourceCategory() {
        val openPeriod = FlightWorkPeriod("2026-11-11T11:00")
        val item = draft().copy(period = FlightWorkPeriod("2026-11-11T11:00", "2026-11-11T12:00"),
            employees = draft().employees.map { it.copy(period = it.period.copy(end = "2026-11-11T12:00")) },
            tools = listOf(FlightResourceDraft(resourceRecordId, resourceId, "Tool", openPeriod, calculationType = 1)),
            materials = listOf(FlightResourceDraft(chapterId, resourceId, "Material", openPeriod, calculationType = 1)),
            generalSupports = listOf(FlightResourceDraft(pdfId, resourceId, "Support", openPeriod, calculationType = 1)))
        val durationCatalogs = catalogs.copy(materials = listOf(ResourceCatalogItem(resourceId, "Material", "Duration")),
            generalSupports = listOf(ResourceCatalogItem(resourceId, "Support", "Duration")))
        val errors = taskEditorValidation(item, item.taskInput(zone), flight, durationCatalogs, flight.rowVersion, null, null)
        assertEquals(R.string.task_validation_open_periods, errors["endUtc"])
        listOf("tools.$resourceRecordId.endUtc", "materials.$chapterId.endUtc", "generalSupports.$pdfId.endUtc").forEach {
            assertEquals(R.string.task_validation_resource_end_required, errors[it])
        }
    }

    @Test fun explicitReviewMergesAnUntouchedEndFromAnotherCloserWhileKeepingOwnDetails() {
        val edited = draft().copy(reference = "MY-REF", remarks = "My pending notes",
            attachments = listOf(FlightAttachmentDraft(pdfId, "content://pending", "my.pdf", "application/pdf")))
        val latest = record.copy(endUtc = end, endedByUserId = "other-user", canSetEnd = false, canDelete = false, canChangeType = false)
        val result = reviewTaskEnds(listOf(edited), listOf(record), listOf(latest), zone)
        assertTrue(result.conflictedTaskIds.isEmpty())
        val merged = result.items.single()
        assertEquals("2026-11-11T12:00", merged.period.end)
        assertEquals(end, merged.period.originalEndUtc)
        assertEquals(edited.copy(period = merged.period), merged)
    }

    @Test fun explicitReviewRetainsAnEditedConflictingEndUntilUseRecordedEndIsChosen() {
        val edited = draft().copy(period = FlightWorkPeriod("2026-11-11T11:00", "2026-11-11T11:30"), remarks = "Keep these notes")
        val latest = record.copy(endUtc = end, endedByUserId = "other-user", canSetEnd = false)
        val result = reviewTaskEnds(listOf(edited), listOf(record), listOf(latest), zone)
        assertEquals(setOf(recordId), result.conflictedTaskIds)
        assertEquals(edited, result.items.single())
        val confirmed = reviewTaskEnds(listOf(edited), listOf(record), listOf(latest), zone, useRecordedEnds = true)
        assertTrue(confirmed.conflictedTaskIds.isEmpty())
        assertEquals("2026-11-11T12:00", confirmed.items.single().period.end)
        assertEquals(edited.remarks, confirmed.items.single().remarks)
        assertEquals(edited.period.start, confirmed.items.single().period.start)
    }

    @Test fun reviewLeavesOwnedEndEditsAloneAndComparesUnchangedTimesByInstant() {
        val previous = record.copy(endUtc = end)
        val draft = draft(previous).copy(remarks = "My change")
        val latestForeign = previous.copy(endUtc = "2026-11-11T16:00:00+03:00", endedByUserId = "other-user", canSetEnd = false)
        val unchanged = reviewTaskEnds(listOf(draft), listOf(previous), listOf(latestForeign), zone)
        assertTrue(unchanged.conflictedTaskIds.isEmpty())
        assertEquals("2026-11-11T13:00", unchanged.items.single().period.end)
        val ownEdit = draft.copy(period = draft.period.copy(end = "2026-11-11T12:30"))
        val latestOwned = latestForeign.copy(canSetEnd = true)
        assertEquals(ownEdit, reviewTaskEnds(listOf(ownEdit), listOf(previous), listOf(latestOwned), zone, useRecordedEnds = true).items.single())
    }

    @Test fun anUnsavedResourceWhoseCatalogTypeChangesGetsAVisibleCorrectionPath() {
        val item = draft().copy(tools = listOf(FlightResourceDraft(resourceRecordId, resourceId, "Tool",
            FlightWorkPeriod("2026-11-11T11:00"), calculationType = 1)))
        val changedCatalogs = catalogs.copy(tools = listOf(ResourceCatalogItem(resourceId, "Tool", "Quantity")))
        val errors = taskEditorValidation(item, item.taskInput(zone), flight, changedCatalogs, flight.rowVersion, null, null)
        assertEquals(R.string.task_validation_resource_type_changed, errors["tools.$resourceRecordId.resourceId"])
    }
}
