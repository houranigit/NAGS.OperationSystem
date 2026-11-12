package com.nags.operations.ui.flights

import com.nags.operations.data.FlightServiceAttachmentRecord
import java.time.ZoneId
import org.junit.Assert.*
import org.junit.Test

class FlightQueuedAttachmentAcknowledgmentsTest {
    private val queued = FlightAttachmentDraft("queued-file", "file:///private-queue-file", "old.pdf", "application/pdf")
    private val uploaded = FlightServiceAttachmentRecord(queued.id, 2, "canonical.pdf", "application/pdf", 100,
        uploadedAtUtc = "2026-11-12T13:00:00Z")

    @Test fun acknowledgementPromotesRetainedQueuedMetadataAndReleasesStaleQueuePreviewUri() {
        val promoted = promoteAcknowledgedWorkAttachments(listOf(queued), listOf(uploaded), setOf(queued.id)).single()
        assertEquals(uploaded.uploadedAtUtc, promoted.uploadedAtUtc)
        assertEquals(uploaded.fileName, promoted.name)
        assertEquals("", promoted.uri)
        assertFalse(promoted.ownedCapture)
        val hydrated = promoted.copy(uri = "file:///account-uploaded-cache")
        assertSame(hydrated, promoteAcknowledgedWorkAttachments(listOf(hydrated), listOf(uploaded), setOf(queued.id)).single())
    }

    @Test fun removedAttachmentsNeverReappearAndUnrelatedNewCapturesRemainOwnedByTheirEditor() {
        val capture = queued.copy(id = "new-capture", uri = "file:///editor-capture", ownedCapture = true)
        val unrelated = queued.copy(id = "other-file")
        val promoted = promoteAcknowledgedWorkAttachments(listOf(capture, unrelated),
            listOf(uploaded, uploaded.copy(id = capture.id)), setOf(queued.id, capture.id))
        assertEquals(listOf(capture.id, unrelated.id), promoted.map { it.id })
        assertSame(capture, promoted[0])
        assertSame(unrelated, promoted[1])
        assertTrue(promoteAcknowledgedWorkAttachments(emptyList(), listOf(uploaded), setOf(queued.id)).isEmpty())
    }

    @Test fun promotingCurrentAndBaselineMetadataDoesNotClaimDirtyCoreEditsOrCreateAnUnchangedEdit() {
        val original = FlightServiceDraft(id = "service", remarks = "Original notes", attachments = listOf(queued))
        val edited = original.copy(remarks = "Unsaved notes")
        fun promote(value: FlightServiceDraft) = value.copy(attachments = promoteAcknowledgedWorkAttachments(value.attachments, listOf(uploaded), setOf(queued.id)))
        val promotedOriginal = promote(original)
        val promotedEdited = promote(edited)
        assertNotEquals(serviceEditorFingerprint(listOf(promotedOriginal), emptyList(), emptyList()),
            serviceEditorFingerprint(listOf(promotedEdited), emptyList(), emptyList()))
        val task = FlightTaskDraft(id = "task", taskType = "Major", attachments = listOf(queued))
        val promotedTask = task.copy(attachments = promoteAcknowledgedWorkAttachments(task.attachments, listOf(uploaded), setOf(queued.id)))
        assertEquals(taskEditorFingerprint(listOf(promotedTask)), taskEditorFingerprint(listOf(promotedTask.copy(
            attachments = promotedTask.attachments.map { it.copy(uri = "file:///uploaded-preview") }))))
        assertEquals("Unsaved notes", promotedEdited.remarks)
    }

    @Test fun queuedIdsStayRetainedForSaveWhilePendingRowDisappearsBeforeMetadataIsObserved() {
        val period = FlightWorkPeriod("2026-11-12T12:00", originalStartUtc = "2026-11-12T12:00:00Z")
        val service = FlightServiceDraft(id = "service", period = period, attachments = listOf(queued))
        val task = FlightTaskDraft(id = "task", taskType = "Major", period = period, attachments = listOf(queued))
        val initialQueuedIds = setOf(queued.id)
        val retainedAfterAck = emptySet<String>() + initialQueuedIds
        assertNull(service.serviceInput(ZoneId.of("Etc/UTC"), retainedAfterAck).attachments!!.single().contentUri)
        assertNull(task.taskInput(ZoneId.of("Etc/UTC"), retainedAfterAck).attachments!!.single().contentUri)
        val promotedService = service.copy(attachments = promoteAcknowledgedWorkAttachments(service.attachments, listOf(uploaded), initialQueuedIds))
        assertNull(promotedService.serviceInput(ZoneId.of("Etc/UTC")).attachments!!.single().contentUri)
    }
}
