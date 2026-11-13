package com.nags.operations.ui.flights

import com.nags.operations.data.*
import com.nags.operations.data.db.entities.FlightServicePendingEntity
import com.nags.operations.data.db.entities.FlightTaskPendingEntity
import java.time.ZoneId
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import org.junit.Assert.*
import org.junit.Test

class FlightWorkRecordProjectionTest {
    private val start = "2026-11-12T12:00:00Z"
    private val savedAt = "2026-11-12T12:30:00Z"
    private val service = FlightServiceRecord("service", "catalog-service", "Recorded service", start,
        createdByUserId = "original-user", createdByDisplayName = "Original creator", createdAtUtc = start,
        performers = listOf(FlightServicePerformerRecord("person", "staff", "Different performer", "E1", start)),
        attachments = listOf(FlightServiceAttachmentRecord("file", 2, "manual.pdf", "application/pdf", 100,
            uploadedByUserId = "uploader", uploadedAtUtc = start)))
    private val task = FlightTaskRecord("task", 0, start, createdByUserId = "task-original-user",
        createdByDisplayName = "Task creator", createdAtUtc = start,
        performers = listOf(FlightTaskPerformerRecord("person", "staff", "Different performer", "E1", start)),
        tools = listOf(FlightTaskResourceRecord("tool-occurrence", "tool", "Recorded tool", 1, startUtc = start)))
    private val flight = FlightDetail("flight", "TEST", "customer", customerName = "Airline", stationId = "station",
        stationIata = "JED", stationName = "Jeddah", operationTypeId = "operation", operationTypeName = "Transit",
        scheduledArrivalUtc = start, scheduledDepartureUtc = "2026-11-12T14:00:00Z", isPerLanding = false,
        createdAtUtc = start, rowVersion = "version", stationTimeZoneId = "Etc/UTC", services = listOf(service), tasks = listOf(task))
    private val pendingService = FlightServicePendingEntity("owner", flight.id, service.id, flight.flightNumber,
        "service-change", "save", Json.encodeToString(service.input().copy(notes = "Desired service notes")),
        flight.rowVersion, Json.encodeToString(service), savedAt)
    private val pendingTask = FlightTaskPendingEntity("owner", flight.id, task.id, flight.flightNumber,
        "task-change", "save", Json.encodeToString(task.input().copy(notes = "Desired task notes")),
        flight.rowVersion, Json.encodeToString(task), savedAt)

    @Test fun queuedCorrectionsKeepOriginalCreatorTimeAndSavedDeviceTimestampAcrossSnapshotReplacement() {
        for (detail in listOf(flight, flight.copy(services = emptyList(), tasks = emptyList()))) {
            val serviceMetadata = flightServiceRecordMetadata(detail, listOf(pendingService)).getValue(service.id)
            assertEquals("Original creator", serviceMetadata.createdByDisplayName)
            assertEquals("original-user", serviceMetadata.createdByUserId)
            assertEquals(start, serviceMetadata.createdAtUtc)
            assertEquals(savedAt, serviceMetadata.savedAtUtc)
            assertTrue(serviceMetadata.published)
            assertTrue(serviceMetadata.queued)
            val taskMetadata = flightTaskRecordMetadata(detail, listOf(pendingTask)).getValue(task.id)
            assertEquals("Task creator", taskMetadata.createdByDisplayName)
            assertEquals("task-original-user", taskMetadata.createdByUserId)
            assertEquals(start, taskMetadata.createdAtUtc)
            assertTrue(taskMetadata.published)
        }
        val absentNames = flight.copy(services = listOf(service.copy(createdByDisplayName = null)))
        assertNull(flightServiceRecordMetadata(absentNames, emptyList()).getValue(service.id).createdByDisplayName)
    }

    @Test fun queuedNewOccurrencesRemainDistinctFromPublishedWorkAndNeverInventCreatorFromPerformers() {
        val queuedService = pendingService.copy(serviceRecordId = "new-service", reviewedServiceJson = null)
        val queuedTask = pendingTask.copy(taskRecordId = "new-task", reviewedTaskJson = null, status = "conflict")
        val services = flightServiceRecordMetadata(flight, listOf(queuedService, pendingService.copy(flightId = "unrelated")))
        val tasks = flightTaskRecordMetadata(flight, listOf(queuedTask))
        assertEquals(setOf(service.id, "new-service"), services.keys)
        assertFalse(services.getValue("new-service").published)
        assertNull(services.getValue("new-service").createdAtUtc)
        assertNull(services.getValue("new-service").createdByUserId)
        assertNull(services.getValue("new-service").createdByDisplayName)
        assertEquals(savedAt, services.getValue("new-service").savedAtUtc)
        assertFalse(tasks.getValue("new-task").published)
        assertEquals("conflict", tasks.getValue("new-task").pendingStatus)
        assertFalse(services.containsKey("unstarted-local-draft"))
    }

    @Test fun reviewedProjectionUsesExactRecordSnapshotForNamesAndMediaWithoutRevertingDesiredEdits() {
        val empty = flight.copy(services = emptyList(), tasks = emptyList())
        val zone = ZoneId.of("Etc/UTC")
        val serviceDraft = initialServiceEditorDraft(empty, FlightOperationsCatalogs(), listOf(pendingService), null, zone).services.single()
        assertEquals("Recorded service", serviceDraft.serviceName)
        assertEquals("Desired service notes", serviceDraft.remarks)
        assertEquals("Different performer", serviceDraft.employees.single().employeeName)
        assertEquals(start, serviceDraft.attachments.single().uploadedAtUtc)
        val taskDraft = initialTaskEditorDraft(empty, FlightOperationsCatalogs(), listOf(pendingTask), null, zone).single()
        assertEquals("Desired task notes", taskDraft.remarks)
        assertEquals("Recorded tool", taskDraft.tools.single().resourceName)
        assertEquals(1, taskDraft.tools.single().calculationType)
        assertNull(pendingService.copy(serviceRecordId = "wrong-record").reviewedRecord())
        assertNull(pendingTask.copy(taskRecordId = "wrong-record").reviewedRecord())
        assertNull(pendingTask.copy(reviewedTaskJson = "invalid").reviewedRecord())
    }

    @Test fun queuedEndChangesNeverAttributeDesiredTimeToThePreviousCloser() {
        val end = "2026-11-12T13:00:00Z"
        val closedService = service.copy(endUtc = end, endedByUserId = "closer", endedByDisplayName = "Original closer")
        val closedTask = task.copy(endUtc = end, endedByUserId = "task-closer", endedByDisplayName = "Original task closer")
        val detail = flight.copy(services = listOf(closedService), tasks = listOf(closedTask))
        val unchangedService = pendingService.copy(inputJson = Json.encodeToString(closedService.input().copy(notes = "Core only")))
        val unchangedTask = pendingTask.copy(inputJson = Json.encodeToString(closedTask.input().copy(notes = "Core only")))
        assertEquals("Original closer", flightServiceRecordMetadata(detail, listOf(unchangedService)).getValue(service.id).endedByDisplayName)
        assertEquals("Original task closer", flightTaskRecordMetadata(detail, listOf(unchangedTask)).getValue(task.id).endedByDisplayName)
        val changedService = unchangedService.copy(inputJson = Json.encodeToString(closedService.input().copy(endUtc = "2026-11-12T13:30:00Z")))
        val clearedTask = unchangedTask.copy(inputJson = Json.encodeToString(closedTask.input().copy(endUtc = null)))
        val serviceMetadata = flightServiceRecordMetadata(detail, listOf(changedService)).getValue(service.id)
        val taskMetadata = flightTaskRecordMetadata(detail, listOf(clearedTask)).getValue(task.id)
        assertNull(serviceMetadata.endedByUserId)
        assertNull(serviceMetadata.endedByDisplayName)
        assertNull(taskMetadata.endedByUserId)
        assertEquals("Original creator", serviceMetadata.createdByDisplayName)
        assertEquals("Task creator", taskMetadata.createdByDisplayName)
    }

    @Test fun failedLocalCorrectionsRetainOnlyTheLatestPublishedUpdaterAndNeverUseDeviceSaveTimeAsAnUpdate() {
        val updatedService = service.copy(updatedAtUtc = "2026-11-12T12:20:00Z", updatedByDisplayName = "Service updater")
        val updatedTask = task.copy(updatedAtUtc = "2026-11-12T12:25:00Z", updatedByDisplayName = "Task updater")
        val retainedService = pendingService.copy(reviewedServiceJson = Json.encodeToString(updatedService), status = "conflict")
        val retainedTask = pendingTask.copy(reviewedTaskJson = Json.encodeToString(updatedTask), status = "failed")
        for (detail in listOf(flight.copy(services = listOf(updatedService), tasks = listOf(updatedTask)),
            flight.copy(services = emptyList(), tasks = emptyList()))) {
            val serviceMetadata = flightServiceRecordMetadata(detail, listOf(retainedService)).getValue(service.id)
            val taskMetadata = flightTaskRecordMetadata(detail, listOf(retainedTask)).getValue(task.id)
            assertEquals("Original creator", serviceMetadata.createdByDisplayName)
            assertEquals("Service updater", serviceMetadata.updatedByDisplayName)
            assertEquals(updatedService.updatedAtUtc, serviceMetadata.updatedAtUtc)
            assertEquals("Task creator", taskMetadata.createdByDisplayName)
            assertEquals("Task updater", taskMetadata.updatedByDisplayName)
            assertEquals(updatedTask.updatedAtUtc, taskMetadata.updatedAtUtc)
            assertNotEquals(savedAt, taskMetadata.updatedAtUtc)
        }
        val newService = pendingService.copy(serviceRecordId = "new-service", reviewedServiceJson = null)
        val newTask = pendingTask.copy(taskRecordId = "new-task", reviewedTaskJson = null)
        assertNull(flightServiceRecordMetadata(flight, listOf(newService)).getValue("new-service").updatedAtUtc)
        assertNull(flightServiceRecordMetadata(flight, listOf(newService)).getValue("new-service").updatedByDisplayName)
        assertNull(flightTaskRecordMetadata(flight, listOf(newTask)).getValue("new-task").updatedAtUtc)
        assertNull(flightTaskRecordMetadata(flight, listOf(newTask)).getValue("new-task").updatedByDisplayName)
    }


    @Test fun pendingAndFailedDeletionsHaveExactDisplayCardsWithoutRestoringTheDesiredWorkGraph() {
        val catalogs = FlightOperationsCatalogs()
        val zone = ZoneId.of(flight.stationTimeZoneId)
        for (status in listOf("pending", "conflict")) {
            val deletedService = pendingService.copy(operation = "delete", inputJson = null, status = status)
            val deletedTask = pendingTask.copy(operation = "delete", inputJson = null, status = status)
            for (detail in listOf(flight, flight.copy(services = emptyList(), tasks = emptyList()))) {
                val desiredServices = initialServiceEditorDraft(detail, catalogs, listOf(deletedService), null, zone).services
                val desiredTasks = initialTaskEditorDraft(detail, catalogs, listOf(deletedTask), null, zone)
                val displayedServices = pendingDeletedServiceDrafts(detail,
                    listOf(deletedService, deletedService, pendingService.copy(flightId = "other")), catalogs, zone)
                val displayedTasks = pendingDeletedTaskDrafts(detail, listOf(deletedTask, pendingTask), catalogs, zone)

                assertTrue(desiredServices.isEmpty())
                assertTrue(desiredTasks.isEmpty())
                assertEquals(listOf(service.id), displayedServices.map { it.id })
                assertEquals("Recorded service", displayedServices.single().serviceName)
                assertEquals(listOf(task.id), displayedTasks.map { it.id })
                assertEquals(start, displayedTasks.single().period.originalStartUtc)
                assertEquals("delete", flightServiceRecordMetadata(detail, listOf(deletedService)).getValue(service.id).pendingOperation)
                assertEquals("delete", flightTaskRecordMetadata(detail, listOf(deletedTask)).getValue(task.id).pendingOperation)
            }
        }
        val vanishedService = pendingService.copy(operation = "delete", inputJson = null, reviewedServiceJson = null, serviceRecordId = "vanished-service")
        val vanishedTask = pendingTask.copy(operation = "delete", inputJson = null, reviewedTaskJson = null, taskRecordId = "vanished-task")
        assertEquals("vanished-service", pendingDeletedServiceDrafts(flight, listOf(vanishedService), catalogs, zone).single().id)
        assertEquals("vanished-task", pendingDeletedTaskDrafts(flight, listOf(vanishedTask), catalogs, zone).single().id)
        assertTrue(pendingDeletedServiceDrafts(flight, listOf(pendingService), catalogs, zone).isEmpty())
        assertTrue(pendingDeletedTaskDrafts(flight, listOf(pendingTask), catalogs, zone).isEmpty())
    }

}
