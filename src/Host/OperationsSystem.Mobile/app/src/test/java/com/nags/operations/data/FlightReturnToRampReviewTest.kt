package com.nags.operations.data

import org.junit.Assert.*
import org.junit.Test

class FlightReturnToRampReviewTest {
    private val time = "2026-11-16T10:00:00Z"
    private val later = "2026-11-16T10:30:00Z"
    private val person = FlightServicePerformerRecord("person", "staff", "Employee", "E1", time)
    private val service = FlightServiceRecord("service", "catalog-service", "Transit", time,
        customerReferenceNumber = "REF", notes = "Original", performers = listOf(person),
        createdByUserId = "owner", createdAtUtc = time)
    private val resource = FlightTaskResourceRecord("tool", "catalog-tool", "Tool", 1, startUtc = time, notes = "Original")
    private val task = FlightTaskRecord("task", 0, time, notes = "Original", tools = listOf(resource),
        createdByUserId = "owner", createdAtUtc = time)
    private val baseline = FlightReturnToRampRecord("return", 1, time, "staff", "Employee", arrivalRemarks = "Original",
        services = listOf(service), tasks = listOf(task), createdByUserId = "owner", createdAtUtc = time)

    @Test fun retainsLocalClearAndAcceptsUntouchedActualAndWorkUpdates() {
        val local = baseline.input().copy(arrival = baseline.input().arrival.copy(remarks = " "))
        val closed = service.copy(endUtc = later, performers = listOf(person.copy(endUtc = later)))
        val online = baseline.copy(arrivedAtUtc = "2026-11-16T09:59:00Z", arrivalRemarks = "Colleague note",
            departedAtUtc = later, services = listOf(closed, service.copy(id = "new-online")))
        val result = mergeReturnToRampReview(online, local, baseline)
        assertNull(result.arrival.remarks)
        assertEquals(online.arrivedAtUtc, result.arrival.arrivedAtUtc)
        assertEquals(later, result.departure?.departedAtUtc)
        assertEquals(later, result.services.first { it.recordId == service.id }.input.performers.single().endUtc)
        assertEquals(setOf("service", "new-online"), result.services.map { it.recordId }.toSet())
    }

    @Test fun onlyChangedFieldsOverrideColleagueEditsInsideTheSameWorkRecord() {
        val local = baseline.input().copy(services = listOf(FlightReturnToRampServiceInput(service.id,
            service.input().copy(notes = "My correction"))))
        val online = baseline.copy(services = listOf(service.copy(customerReferenceNumber = "ONLINE", endUtc = later,
            performers = listOf(person.copy(endUtc = later), person.copy(id = "other-period", startUtc = later)))))
        val result = mergeReturnToRampReview(online, local, baseline).services.single().input
        assertEquals("My correction", result.notes)
        assertEquals("ONLINE", result.customerReferenceNumber)
        assertEquals(later, result.endUtc)
        assertEquals(2, result.performers.size)
        assertEquals(later, result.performers.first { it.id == person.id }.endUtc)
    }

    @Test fun deliberateWorkAndResourceRemovalsDoNotRemoveNewOnlineOccurrences() {
        val closedTask = task.copy(endUtc = later, notes = "Before", tools = listOf(resource.copy(endUtc = later)))
        val before = baseline.copy(tasks = listOf(closedTask))
        val desiredTask = closedTask.input().copy(taskType = 1, endUtc = null, notes = null, tools = emptyList())
        val local = before.input().copy(services = emptyList(), tasks = listOf(FlightReturnToRampTaskInput(task.id, desiredTask)))
        val online = before.copy(services = listOf(service, service.copy(id = "new-service")),
            tasks = listOf(closedTask.copy(notes = "Colleague", tools = listOf(resource, resource.copy(id = "new-tool"))), task.copy(id = "new-task")))
        val result = mergeReturnToRampReview(online, local, before)
        assertEquals(listOf("new-service"), result.services.map { it.recordId })
        assertEquals(setOf("task", "new-task"), result.tasks.map { it.recordId }.toSet())
        val edited = result.tasks.first { it.recordId == task.id }.input
        assertEquals(1, edited.taskType)
        assertNull(edited.endUtc)
        assertNull(edited.notes)
        assertEquals(listOf("new-tool"), edited.tools.map { it.id })
    }

    @Test fun keepsLocalSignatureAndNewMediaAndPreservesNewOnlineFiles() {
        val old = FlightServiceAttachmentRecord("old", 2, "old.pdf", "application/pdf", 20)
        val addedOnline = old.copy(id = "online", fileName = "online.pdf")
        val before = baseline.copy(services = listOf(service.copy(attachments = listOf(old))))
        val localFile = FlightServiceAttachmentInput("local", 2, "local.pdf", "application/pdf", content = "saved bytes")
        val local = before.input().copy(departure = FlightReturnToRampDepartureInput(later,
            signatureContent = "saved signature", signatureSignedAtUtc = later), services = listOf(FlightReturnToRampServiceInput(service.id,
            before.services.single().input().copy(attachments = listOf(localFile)))))
        val online = before.copy(departedAtUtc = later, hasSignature = true,
            services = listOf(before.services.single().copy(attachments = listOf(old, addedOnline))))
        val result = mergeReturnToRampReview(online, local, before)
        assertEquals("saved signature", result.departure?.signatureContent)
        assertEquals(later, result.departure?.signatureSignedAtUtc)
        val files = result.services.single().input.attachments.orEmpty()
        assertEquals(setOf("local", "online"), files.map { it.id }.toSet())
        assertEquals("saved bytes", files.first { it.id == "local" }.content)
        assertEquals("online.pdf", files.first { it.id == "online" }.fileName)
    }

    @Test fun signatureRemovalIsAnExplicitLocalChoice() {
        val before = baseline.copy(departedAtUtc = later, hasSignature = true)
        val local = before.input().copy(departure = before.input().departure!!.copy(removeSignature = true))
        val result = mergeReturnToRampReview(before.copy(signatureSignedAtUtc = later), local, before)
        val departure = checkNotNull(result.departure)
        assertTrue(departure.removeSignature)
        assertNull(departure.signatureContent)
    }

    @Test fun repeatedReviewsDoNotTurnPreviouslyInheritedOnlineFieldsIntoLocalEdits() {
        val local = baseline.input().copy(arrival = baseline.input().arrival.copy(remarks = "My change"))
        val online = baseline.copy(arrivedAtUtc = "2026-11-16T09:59:00Z", services = listOf(service.copy(customerReferenceNumber = "FIRST")))
        val first = mergeReturnToRampReview(online, local, baseline)
        val newer = online.copy(arrivedAtUtc = "2026-11-16T09:58:00Z", services = listOf(service.copy(customerReferenceNumber = "SECOND")))
        val second = mergeReturnToRampReview(newer, first, online)
        assertEquals("My change", second.arrival.remarks)
        assertEquals(newer.arrivedAtUtc, second.arrival.arrivedAtUtc)
        assertEquals("SECOND", second.services.single().input.customerReferenceNumber)
    }

    @Test fun respectsExternalDeletionWhenTheLocalRecordWasUntouched() {
        val online = baseline.copy(services = emptyList(), tasks = emptyList())
        val result = mergeReturnToRampReview(online, baseline.input(), baseline)
        assertTrue(result.services.isEmpty())
        assertTrue(result.tasks.isEmpty())
    }

    @Test fun equivalentTimeOffsetsAreNotLocalChanges() {
        val before = baseline.copy(arrivedAtUtc = "2026-11-16T13:00:00+03:00")
        val local = before.input().copy(arrival = before.input().arrival.copy(arrivedAtUtc = time))
        val online = before.copy(arrivedAtUtc = "2026-11-16T10:01:00Z")
        assertEquals(online.arrivedAtUtc, mergeReturnToRampReview(online, local, before).arrival.arrivedAtUtc)
    }

    @Test fun initialLocalArrivalDoesNotEraseOtherEmployeesNewRecordedWork() {
        val local = SaveFlightReturnToRampRequest(FlightReturnToRampArrivalInput(later))
        val result = mergeReturnToRampReview(baseline, local, null)
        assertEquals(later, result.arrival.arrivedAtUtc)
        assertEquals("Original", result.arrival.remarks)
        assertEquals(listOf(service.id), result.services.map { it.recordId })
        assertEquals(listOf(task.id), result.tasks.map { it.recordId })
    }

    @Test fun publishedFileIdentityKeepsItsRecordedMetadata() {
        val file = FlightServiceAttachmentRecord("published", 2, "recorded.pdf", "application/pdf", 20)
        val online = baseline.copy(services = listOf(service.copy(attachments = listOf(file))))
        val local = baseline.input().copy(services = listOf(FlightReturnToRampServiceInput(service.id,
            service.input().copy(attachments = listOf(FlightServiceAttachmentInput(file.id, 2, "changed.pdf", "application/pdf", "private bytes"))))))
        val result = mergeReturnToRampReview(online, local, baseline).services.single().input.attachments!!.single()
        assertEquals(file.fileName, result.fileName)
        assertNull(result.content)
    }

    @Test fun localQuantitySelectionDoesNotInheritAnOnlineDurationEnd() {
        val selected = resource.copy(resourceId = "quantity-tool", calculationType = 0, quantity = "2", startUtc = null)
        val local = baseline.input().copy(tasks = listOf(FlightReturnToRampTaskInput(task.id,
            task.copy(tools = listOf(selected)).input())))
        val online = baseline.copy(tasks = listOf(task.copy(tools = listOf(resource.copy(endUtc = later)))))
        val result = mergeReturnToRampReview(online, local, baseline).tasks.single().input.tools.single()
        assertEquals("quantity-tool", result.resourceId)
        assertEquals("2", result.quantity)
        assertNull(result.startUtc)
        assertNull(result.endUtc)
    }

    @Test fun localDurationEditDoesNotInheritAnOnlineQuantitySelection() {
        val closed = resource.copy(endUtc = later)
        val before = baseline.copy(tasks = listOf(task.copy(tools = listOf(closed))))
        val local = before.input().copy(tasks = listOf(FlightReturnToRampTaskInput(task.id,
            task.copy(tools = listOf(resource)).input())))
        val online = before.copy(tasks = listOf(task.copy(tools = listOf(resource.copy(resourceId = "quantity-tool",
            calculationType = 0, quantity = "3", startUtc = null, endUtc = null)))))
        val result = mergeReturnToRampReview(online, local, before).tasks.single().input.tools.single()
        assertEquals(resource.resourceId, result.resourceId)
        assertNull(result.quantity)
        assertEquals(time, result.startUtc)
        assertNull(result.endUtc)
    }

    @Test fun untouchedLocalCalculationAcceptsAnOnlineModeChange() {
        val local = baseline.input().copy(tasks = listOf(FlightReturnToRampTaskInput(task.id,
            task.copy(tools = listOf(resource.copy(notes = "My note"))).input())))
        val online = baseline.copy(tasks = listOf(task.copy(tools = listOf(resource.copy(resourceId = "quantity-tool",
            calculationType = 0, quantity = "3", startUtc = null, endUtc = null)))))
        val result = mergeReturnToRampReview(online, local, baseline).tasks.single().input.tools.single()
        assertEquals("quantity-tool", result.resourceId)
        assertEquals("3", result.quantity)
        assertNull(result.startUtc)
        assertNull(result.endUtc)
        assertEquals("My note", result.notes)
    }

    @Test fun sameModeDurationChangesStillMergeTheirIndependentBoundaries() {
        val localStart = "2026-11-16T10:01:00Z"
        val local = baseline.input().copy(tasks = listOf(FlightReturnToRampTaskInput(task.id,
            task.copy(tools = listOf(resource.copy(startUtc = localStart))).input())))
        val online = baseline.copy(tasks = listOf(task.copy(tools = listOf(resource.copy(endUtc = later)))))
        val result = mergeReturnToRampReview(online, local, baseline).tasks.single().input.tools.single()
        assertEquals(localStart, result.startUtc)
        assertEquals(later, result.endUtc)
        assertNull(result.quantity)
    }
}
