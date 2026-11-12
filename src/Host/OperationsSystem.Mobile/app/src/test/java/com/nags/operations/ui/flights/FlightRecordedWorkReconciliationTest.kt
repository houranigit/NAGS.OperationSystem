package com.nags.operations.ui.flights

import org.junit.Assert.*
import org.junit.Test

class FlightRecordedWorkReconciliationTest {
    private val before = FlightWorkPeriod("2026-11-12T10:00", originalStartUtc = "2026-11-12T10:00:00Z")
    private val closed = before.copy(end = "2026-11-12T11:00", originalEndUtc = "2026-11-12T11:00:00Z")

    @Test fun serviceRefreshReplacesRecordedRowsAndRemovesDeletedRowsWithoutTouchingLocalDraftEditsOrOrder() {
        val firstLocal = FlightServiceDraft(id = "new-a", serviceId = "selected-a", reference = "Dirty reference",
            employees = listOf(FlightEmployeeDraft(id = "person-a", employeeName = "Selected employee")))
        val recorded = FlightServiceDraft(id = "recorded", period = before, remarks = "Old server notes")
        val secondLocal = FlightServiceDraft(id = "new-b", serviceId = "selected-b", remarks = "Unsaved notes")
        val deleted = FlightServiceDraft(id = "deleted", period = before)
        val canonical = recorded.copy(period = closed, remarks = "Teammate correction")
        val incoming = FlightServiceDraft(id = "incoming", period = before)
        val result = reconcileRecordedServices(listOf(firstLocal, recorded, secondLocal, deleted),
            listOf(canonical, FlightServiceDraft(id = "projected-planned"), incoming),
            previousKnown = setOf("recorded", "deleted"), nextKnown = setOf("recorded", "incoming"))
        assertEquals(listOf("new-a", "recorded", "new-b", "incoming"), result.map { it.id })
        assertSame(firstLocal, result[0])
        assertEquals(canonical, result[1])
        assertSame(secondLocal, result[2])
        assertEquals(closed, result[1].period)
        assertEquals("Dirty reference", result[0].reference)
    }

    @Test fun taskRefreshAdoptsCanonicalPartialEndAndNestedChangesWhileKeepingNewTaskDraftsDirty() {
        val local = FlightTaskDraft(id = "local", taskType = "Minor", remarks = "Unsaved task notes",
            tools = listOf(FlightResourceDraft(id = "local-tool", resourceName = "Unsaved selection")))
        val recorded = FlightTaskDraft(id = "recorded", taskType = "Major", period = before,
            employees = listOf(FlightEmployeeDraft(id = "person", period = before)))
        val canonical = recorded.copy(period = closed, employees = recorded.employees.map { it.copy(period = closed) },
            tools = listOf(FlightResourceDraft(id = "server-tool", resourceName = "Recorded tool", period = closed)))
        val result = reconcileRecordedTasks(listOf(recorded, local), listOf(canonical),
            previousKnown = setOf(recorded.id), nextKnown = setOf(recorded.id))
        assertEquals(canonical, result[0])
        assertSame(local, result[1])
        assertEquals(closed, result[0].employees.single().period)
        assertEquals("Unsaved selection", result[1].tools.single().resourceName)
    }

    @Test fun acknowledgedAddsUseTheirStableIdsWithoutDuplicatingOrReorderingOtherLocalDrafts() {
        val service = FlightServiceDraft(id = "queued-service", period = before, remarks = "Desired service")
        val serviceLocal = FlightServiceDraft(id = "service-local", remarks = "Still dirty")
        val acknowledgedService = service.copy(remarks = "Acknowledged service")
        val services = reconcileRecordedServices(listOf(service, serviceLocal), listOf(acknowledgedService, acknowledgedService),
            previousKnown = emptySet(), nextKnown = setOf(service.id))
        assertEquals(listOf(service.id, serviceLocal.id), services.map { it.id })
        assertEquals(acknowledgedService, services[0])
        assertSame(serviceLocal, services[1])
        val task = FlightTaskDraft(id = "queued-task", period = before)
        val taskLocal = FlightTaskDraft(id = "task-local", reference = "Unsaved reference")
        val tasks = reconcileRecordedTasks(listOf(taskLocal, task), listOf(task.copy(period = closed)),
            previousKnown = setOf(task.id), nextKnown = setOf(task.id))
        assertEquals(listOf(taskLocal.id, task.id), tasks.map { it.id })
        assertSame(taskLocal, tasks[0])
        assertEquals(closed, tasks[1].period)
    }

    @Test fun protectedReviewRowsRemainExactEvenWhenCanonicalRefreshChangesOrDeletesThem() {
        val service = FlightServiceDraft(id = "protected-service", period = before, remarks = "Explicit review edits")
        val services = reconcileRecordedServices(listOf(service), listOf(service.copy(period = closed)),
            previousKnown = setOf(service.id), nextKnown = setOf(service.id), protectedIds = setOf(service.id))
        assertEquals(1, services.size)
        assertSame(service, services.single())
        val task = FlightTaskDraft(id = "protected-task", period = before, reference = "Reviewed reference")
        val tasks = reconcileRecordedTasks(listOf(task), emptyList(), previousKnown = setOf(task.id),
            nextKnown = emptySet(), protectedIds = setOf(task.id))
        assertSame(task, tasks.single())
    }

    @Test fun previewCarryoverRequiresExactUploadedVersionAndNeverBorrowsEditorOwnedCapture() {
        val uploaded = "2026-11-12T10:30:00Z"
        val cached = FlightAttachmentDraft("same", "content://scoped-preview", "Old name", "application/pdf", uploadedAtUtc = uploaded)
        val changedVersion = cached.copy(id = "changed", uri = "content://obsolete-preview")
        val privateCapture = cached.copy(id = "private", uri = "file:///private-editor-capture", uploadedAtUtc = null, ownedCapture = true)
        val canonicalMedia = listOf(cached.copy(uri = "", name = "Canonical name"),
            changedVersion.copy(uri = "", uploadedAtUtc = "2026-11-12T10:31:00Z"),
            privateCapture.copy(uri = "", ownedCapture = false))
        val service = FlightServiceDraft(id = "service", attachments = listOf(cached, changedVersion, privateCapture))
        val canonicalService = service.copy(attachments = canonicalMedia)
        val mergedService = reconcileRecordedServices(listOf(service), listOf(canonicalService), setOf(service.id), setOf(service.id)).single()
        assertEquals("content://scoped-preview", mergedService.attachments[0].uri)
        assertEquals("Canonical name", mergedService.attachments[0].name)
        assertEquals("", mergedService.attachments[1].uri)
        assertEquals(canonicalMedia[1].uploadedAtUtc, mergedService.attachments[1].uploadedAtUtc)
        assertEquals("", mergedService.attachments[2].uri)
        assertFalse(mergedService.attachments[2].ownedCapture)
        val task = FlightTaskDraft(id = "task", attachments = service.attachments)
        val mergedTask = reconcileRecordedTasks(listOf(task), listOf(task.copy(attachments = canonicalMedia)), setOf(task.id), setOf(task.id)).single()
        assertEquals(mergedService.attachments, mergedTask.attachments)
    }

    @Test fun absentCanonicalKnownRowsRemoveOnlyRecordedOccurrencesAndIncomingRowsAppendOnce() {
        val local = FlightTaskDraft(id = "local", remarks = "Dirty")
        val removed = FlightTaskDraft(id = "removed", period = before)
        val incoming = FlightTaskDraft(id = "incoming", period = before)
        val result = reconcileRecordedTasks(listOf(local, removed), listOf(incoming, incoming),
            previousKnown = setOf(removed.id), nextKnown = setOf(incoming.id))
        assertEquals(listOf(local.id, incoming.id), result.map { it.id })
        assertSame(local, result[0])
        assertEquals(incoming, result[1])
    }
}
