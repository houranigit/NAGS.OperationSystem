package com.nags.operations.data

import org.junit.Assert.*
import org.junit.Test

class FlightReviewMergesTest {
    private val start = "2026-11-10T12:00:00Z"
    private val changedStart = "2026-11-10T12:01:00Z"
    private val end = "2026-11-10T12:10:00Z"
    private val version = "AAAAAAAAAAE="
    private fun id(value: Int) = "00000000-0000-0000-0000-${value.toString().padStart(12, '0')}"

    @Test fun arrivalOverwritesPopulatedFieldsAndRetainsTheOnlineFieldMissingLocally() {
        val online = SaveFlightArrivalRequest(start, id(1), "ONLINE", "online field three", "online remarks",
            targetSubmissionId = id(2))
        val local = SaveFlightArrivalRequest(changedStart, " ", " LOCAL ", null, " local remarks ",
            acknowledgeConflictingSubmission = true, targetSubmissionId = id(3))

        val merged = mergeArrivalReview(online, local)

        assertEquals(changedStart, merged.ata)
        assertEquals(id(1), merged.aircraftTypeId)
        assertEquals("LOCAL", merged.aircraftTailNumber)
        assertEquals("online field three", merged.customerWorkReferenceNumber)
        assertEquals("local remarks", merged.remarks)
        assertEquals(id(2), merged.targetSubmissionId)
        assertFalse(merged.acknowledgeConflictingSubmission)
        assertEquals(online, mergeArrivalReview(online, local.copy(ata = "", aircraftTailNumber = " ", remarks = "\n")))
    }

    @Test fun departureRetainsOnlineSignatureAndSigningTimeTogetherUnlessLocalSignatureIsProvided() {
        val online = SaveFlightDepartureRequest(end, "recorded PNG", start, "recorded remarks", targetSubmissionId = id(2))
        val emptyLocal = SaveFlightDepartureRequest(" ", " ", changedStart, null,
            acknowledgeConflictingSubmission = true, targetSubmissionId = id(3))

        assertEquals(online, mergeDepartureReview(online, emptyLocal))
        val replacement = mergeDepartureReview(online, emptyLocal.copy(atd = changedStart,
            signatureContent = "new PNG", signatureSignedAtUtc = end, remarks = "new remarks"))
        assertEquals(changedStart, replacement.atd)
        assertEquals("new PNG", replacement.signatureContent)
        assertEquals(end, replacement.signatureSignedAtUtc)
        assertEquals("new remarks", replacement.remarks)
        assertEquals(id(2), replacement.targetSubmissionId)
        assertFalse(replacement.acknowledgeConflictingSubmission)
    }

    @Test fun serviceKeepsMissingParentAndStaffEndsAndUnmentionedOnlinePerformers() {
        val retained = SaveFlightServicePerformerRequest(id(11), id(21), start, end)
        val other = SaveFlightServicePerformerRequest(id(12), id(22), start, end)
        val online = SaveFlightServiceRequest(id(1), start, end, "online reference", "online field three",
            listOf(retained, other))
        val local = online.copy(serviceId = "", startUtc = changedStart, endUtc = null,
            customerReferenceNumber = "local reference", notes = " ", performers = listOf(
                retained.copy(id = id(99), startUtc = changedStart, endUtc = " "),
                SaveFlightServicePerformerRequest(id(13), id(23), changedStart, end)))

        val merged = mergeServiceReview(online, local)

        assertEquals(id(1), merged.serviceId)
        assertEquals(changedStart, merged.startUtc)
        assertEquals(end, merged.endUtc)
        assertEquals("local reference", merged.customerReferenceNumber)
        assertEquals("online field three", merged.notes)
        assertEquals(setOf(id(11), id(12), id(13)), merged.performers.map { it.id }.toSet())
        assertEquals(retained.copy(startUtc = changedStart), merged.performers.single { it.staffMemberId == id(21) })
        assertEquals(other, merged.performers.single { it.staffMemberId == id(22) })
        assertEquals(3, merged.performers.map { it.staffMemberId }.distinct().size)
    }

    @Test fun stableChildIdCanChangeStaffWithoutDroppingUnmentionedOnlineRows() {
        val online = SaveFlightServiceRequest(id(1), start, performers = listOf(
            SaveFlightServicePerformerRequest(id(11), id(21), start),
            SaveFlightServicePerformerRequest(id(12), id(22), start)))
        val local = online.copy(performers = listOf(online.performers.first().copy(staffMemberId = id(23))))

        val merged = mergeServiceReview(online, local)

        assertEquals(listOf(id(11), id(12)), merged.performers.map { it.id })
        assertEquals(listOf(id(23), id(22)), merged.performers.map { it.staffMemberId })
    }

    @Test fun attachmentsPreserveEveryPublishedIdentityAndAppendNewLocalBytesAcrossServiceAndTask() {
        val published = FlightServiceAttachmentInput(id(31), 2, "recorded.pdf", "application/pdf", "recorded bytes")
        val onlineOnly = published.copy(id = id(32), fileName = "colleague.pdf")
        val injectedReplacement = published.copy(kind = 0, fileName = "replacement.png", contentType = "image/png", content = "different bytes")
        val localNew = FlightServiceAttachmentInput(id(33), 2, "local.pdf", "application/pdf", "local bytes", "content://private/local")
        val onlineService = SaveFlightServiceRequest(id(1), start, attachments = listOf(published, onlineOnly))
        val onlineTask = SaveFlightTaskRequest(1, start, attachments = listOf(published, onlineOnly))
        val expected = listOf(published, onlineOnly, localNew)

        assertEquals(expected, mergeServiceReview(onlineService,
            onlineService.copy(attachments = listOf(injectedReplacement, localNew))).attachments)
        assertEquals(expected, mergeTaskReview(onlineTask,
            onlineTask.copy(attachments = listOf(injectedReplacement, localNew))).attachments)
        assertEquals(onlineService.attachments, mergeServiceReview(onlineService, onlineService.copy(attachments = emptyList())).attachments)
        assertEquals(onlineTask.attachments, mergeTaskReview(onlineTask, onlineTask.copy(attachments = null)).attachments)
    }

    @Test fun taskMajorZeroIsPopulatedAndMissingOptionalFieldsAndStaffEndsKeepOnlineValues() {
        val performer = SaveFlightTaskPerformerRequest(id(11), id(21), start, end, "online staff notes")
        val online = SaveFlightTaskRequest(1, start, end, id(41), "online reference", "online field three", listOf(performer))
        val local = SaveFlightTaskRequest(0, changedStart, ataChapterId = " ", notes = "local notes",
            performers = listOf(performer.copy(id = id(99), startUtc = "", endUtc = null, notes = " ")))

        val merged = mergeTaskReview(online, local)

        assertEquals(0, merged.taskType)
        assertEquals(changedStart, merged.startUtc)
        assertEquals(end, merged.endUtc)
        assertEquals(id(41), merged.ataChapterId)
        assertEquals("online reference", merged.customerReferenceNumber)
        assertEquals("local notes", merged.notes)
        assertEquals(listOf(performer), merged.performers)
    }

    @Test fun resourcePatchesRetainOnlineIdsAndEndsAndExplicitZeroIsValidatedInsteadOfFallingBack() {
        val duration = SaveFlightTaskResourceRequest(id(51), id(61), startUtc = start, endUtc = end, notes = "online notes")
        val quantity = SaveFlightTaskResourceRequest(id(52), id(62), quantity = "3")
        val untouched = SaveFlightTaskResourceRequest(id(53), id(63), quantity = "5")
        val online = SaveFlightTaskRequest(0, start, end, performers = listOf(SaveFlightTaskPerformerRequest(id(11), id(21), start, end)),
            tools = listOf(duration), materials = listOf(quantity, untouched), generalSupports = listOf(duration.copy(id = id(54))))
        val local = online.copy(tools = listOf(duration.copy(id = id(91), startUtc = changedStart, endUtc = null, notes = "")),
            materials = listOf(quantity.copy(id = id(92), quantity = "0", startUtc = changedStart, endUtc = end)),
            generalSupports = emptyList())

        val merged = mergeTaskReview(online, local)

        assertEquals(listOf(duration.copy(startUtc = changedStart)), merged.tools)
        assertEquals(quantity.copy(quantity = "0"), merged.materials.single { it.resourceId == id(62) })
        assertEquals(untouched, merged.materials.single { it.resourceId == id(63) })
        assertEquals(online.generalSupports, merged.generalSupports)
        assertTrue(merged.validationErrors(version).contains("materials.${id(52)}.quantity"))
        val changedToDuration = mergeTaskReview(online, online.copy(materials = listOf(
            quantity.copy(quantity = null, startUtc = start, endUtc = end)))).materials.single { it.resourceId == id(62) }
        assertNull(changedToDuration.quantity)
        assertEquals(start, changedToDuration.startUtc)
        assertEquals(end, changedToDuration.endUtc)
    }
}
