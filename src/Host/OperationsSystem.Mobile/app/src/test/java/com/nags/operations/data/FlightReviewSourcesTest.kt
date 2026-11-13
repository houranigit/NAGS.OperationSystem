package com.nags.operations.data

import org.junit.Assert.*
import org.junit.Test

class FlightReviewSourcesTest {
    private val start = "2026-11-13T12:00:00Z"
    private val end = "2026-11-13T13:00:00Z"
    private fun id(value: Int) = "00000000-0000-0000-0000-${value.toString().padStart(12, '0')}"

    @Test fun inheritedArrivalFieldStaysAbsentAcrossRepeatedReviewsAndManualChangeBecomesLocal() {
        val online = SaveFlightArrivalRequest(start, id(1), "ONLINE", "A3", "online remarks", targetSubmissionId = id(2))
        val source = SaveFlightArrivalRequest(start, id(1), "LOCAL")
        val staged = mergeArrivalReview(online, source)

        val retained = arrivalReviewSourceAfterSave(online, source, staged)

        assertNull(retained.customerWorkReferenceNumber)
        assertNull(retained.remarks)
        assertEquals(id(2), retained.targetSubmissionId)
        val newer = online.copy(customerWorkReferenceNumber = "C3", remarks = "newer remarks")
        val next = mergeArrivalReview(newer, retained)
        assertEquals("C3", next.customerWorkReferenceNumber)
        assertEquals("newer remarks", next.remarks)
        val edited = arrivalReviewSourceAfterSave(newer, retained, next.copy(customerWorkReferenceNumber = "D3"))
        assertEquals("D3", mergeArrivalReview(newer.copy(customerWorkReferenceNumber = "E3"), edited).customerWorkReferenceNumber)
    }

    @Test fun originallyProvidedEqualValueRemainsProvidedAndEquivalentTimeFormattingDoesNotPromoteInheritance() {
        val online = SaveFlightArrivalRequest(start, id(1), "ONLINE", "same", targetSubmissionId = id(2))
        val source = SaveFlightArrivalRequest("", "", "LOCAL", "same")
        val saved = mergeArrivalReview(online, source).copy(ata = "2026-11-13T15:00:00+03:00")

        val retained = arrivalReviewSourceAfterSave(online, source, saved)

        assertEquals("", retained.ata)
        assertEquals("", retained.aircraftTypeId)
        assertEquals("SAME", retained.customerWorkReferenceNumber)
        assertEquals("SAME", mergeArrivalReview(online.copy(customerWorkReferenceNumber = "colleague"), retained).customerWorkReferenceNumber)
    }

    @Test fun customStagedBaselineCapturesManualChangesBeforeOverwriteWithoutPromotingOtherOnlineValues() {
        val online = SaveFlightArrivalRequest(start, id(1), "ONLINE", "A3", "online remarks")
        val source = SaveFlightArrivalRequest("", "", "LOCAL")
        val expectedStaged = mergeArrivalReview(online, source)
        val final = mergeArrivalReview(online.copy(customerWorkReferenceNumber = "manual before overwrite"), source)

        val retained = arrivalReviewSourceAfterSave(online, source, final, expectedStaged)

        assertEquals("MANUAL BEFORE OVERWRITE", retained.customerWorkReferenceNumber)
        assertNull(retained.remarks)
        assertEquals("", retained.ata)
    }

    @Test fun semanticStaffRemappingRetainsSparseFieldsAndDoesNotPromoteUntouchedOnlineStaff() {
        val employee = SaveFlightServicePerformerRequest(id(11), id(21), start, end)
        val other = employee.copy(id = id(12), staffMemberId = id(22))
        val online = SaveFlightServiceRequest(id(1), start, end, notes = "online", performers = listOf(employee, other))
        val source = SaveFlightServiceRequest("", "", performers = listOf(employee.copy(id = id(99), startUtc = "", endUtc = null)))
        val saved = mergeServiceReview(online, source)

        val retained = serviceReviewSourceAfterSave(online, source, saved)

        assertNull(retained.notes)
        assertEquals(1, retained.performers.size)
        assertEquals(id(99), retained.performers.single().id)
        assertEquals("", retained.performers.single().startUtc)
        assertNull(retained.performers.single().endUtc)
        val changedEnd = "2026-11-13T13:30:00Z"
        val next = mergeServiceReview(online.copy(performers = listOf(employee.copy(endUtc = changedEnd), other)), retained)
        assertEquals(changedEnd, next.performers.single { it.staffMemberId == id(21) }.endUtc)
        val manual = serviceReviewSourceAfterSave(online, source, saved.copy(performers = saved.performers.map {
            if (it.staffMemberId == id(22)) it.copy(endUtc = changedEnd) else it
        }))
        val manualOther = manual.performers.single { it.id == id(12) }
        assertEquals("", manualOther.staffMemberId)
        assertEquals("", manualOther.startUtc)
        assertEquals(changedEnd, manualOther.endUtc)
        val nextSelection = mergeServiceReview(online.copy(performers = listOf(employee,
            other.copy(staffMemberId = id(23)))), manual)
        assertEquals(id(23), nextSelection.performers.single { it.id == id(12) }.staffMemberId)
        assertEquals(changedEnd, nextSelection.performers.single { it.id == id(12) }.endUtc)
    }

    @Test fun taskResourcesRetainZeroAndSparseSemanticRowsWhileInheritedTypeAndRowsStayAbsent() {
        val duration = SaveFlightTaskResourceRequest(id(31), id(41), startUtc = start, endUtc = end, notes = "A3")
        val quantity = SaveFlightTaskResourceRequest(id(32), id(42), quantity = "3")
        val other = quantity.copy(id = id(33), resourceId = id(43))
        val online = SaveFlightTaskRequest(1, start, end, tools = listOf(duration), materials = listOf(quantity, other))
        val source = SaveFlightTaskRequest(0, "", tools = listOf(duration.copy(id = id(91), startUtc = null, endUtc = null, notes = null)),
            materials = listOf(quantity.copy(id = id(92), quantity = "0")))
        val saved = mergeTaskReview(online, source)

        val retained = taskReviewSourceAfterSave(online, source, saved)

        assertEquals(0, retained.taskType)
        assertEquals("0", retained.materials.single().quantity)
        assertNull(retained.tools.single().notes)
        assertNull(retained.tools.single().endUtc)
        assertEquals(id(91), retained.tools.single().id)
        val next = mergeTaskReview(online.copy(tools = listOf(duration.copy(notes = "C3"))), retained)
        assertEquals("C3", next.tools.single().notes)
        val empty = SaveFlightTaskRequest(-1, "")
        val noOverwrite = taskReviewSourceAfterSave(online, empty, online, online)
        assertEquals(-1, noOverwrite.taskType)
        assertTrue(noOverwrite.tools.isEmpty())
        assertTrue(noOverwrite.materials.isEmpty())
    }

    @Test fun editingOnlyInheritedChildNotesDoesNotClaimItsStaffOrResourceSelection() {
        val person = SaveFlightTaskPerformerRequest(id(11), id(21), start, end, "online")
        val resource = SaveFlightTaskResourceRequest(id(31), id(41), quantity = "3", notes = "online")
        val online = SaveFlightTaskRequest(0, start, end, performers = listOf(person), tools = listOf(resource))
        val source = SaveFlightTaskRequest(-1, "")
        val saved = online.copy(performers = listOf(person.copy(notes = "local person")),
            tools = listOf(resource.copy(notes = "local resource")))

        val retained = taskReviewSourceAfterSave(online, source, saved, online)

        assertEquals("", retained.performers.single().staffMemberId)
        assertEquals("", retained.tools.single().resourceId)
        assertNull(retained.tools.single().quantity)
        val next = mergeTaskReview(online.copy(performers = listOf(person.copy(staffMemberId = id(22))),
            tools = listOf(resource.copy(resourceId = id(42), quantity = "5"))), retained)
        assertEquals(id(22), next.performers.single().staffMemberId)
        assertEquals("local person", next.performers.single().notes)
        assertEquals(id(42), next.tools.single().resourceId)
        assertEquals("5", next.tools.single().quantity)
        assertEquals("local resource", next.tools.single().notes)
    }

    @Test fun mediaOriginStoresOnlyProvidedMetadataAndNeverInlineBytesOrInheritedAttachments() {
        val onlineFile = FlightServiceAttachmentInput(id(51), 2, "online.pdf", "application/pdf")
        val localFile = onlineFile.copy(id = id(52), fileName = "local.pdf", content = "private bytes", contentUri = "content://private")
        val online = SaveFlightServiceRequest(id(1), start, attachments = listOf(onlineFile))
        val source = SaveFlightServiceRequest("", "", attachments = listOf(localFile))
        val retained = serviceReviewSourceAfterSave(online, source, mergeServiceReview(online, source))
        assertEquals(listOf(localFile.copy(content = null, contentUri = null)), retained.attachments)

        val onlineDeparture = SaveFlightDepartureRequest(end, remarks = "online", targetSubmissionId = id(2))
        val signatureSource = SaveFlightDepartureRequest("", signatureSignedAtUtc = start)
        val staged = onlineDeparture.copy(signatureSignedAtUtc = start)
        val departure = departureReviewSourceAfterSave(onlineDeparture, signatureSource,
            staged.copy(signatureContent = "private PNG", signatureSignedAtUtc = "2026-11-13T15:00:00+03:00"), staged)
        assertNull(departure.signatureContent)
        assertEquals(start, departure.signatureSignedAtUtc)
        assertEquals("", departure.atd)
        assertNull(departure.remarks)
        assertEquals(id(2), departure.targetSubmissionId)
    }
}
