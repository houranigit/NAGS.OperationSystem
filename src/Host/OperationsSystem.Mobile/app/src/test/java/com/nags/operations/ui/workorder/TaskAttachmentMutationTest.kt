package com.nags.operations.ui.workorder

import org.junit.Assert.assertEquals
import org.junit.Test

class TaskAttachmentMutationTest {
    private val attachment = TaskAttachmentDraft(
        kind = "Image",
        contentType = "image/jpeg",
        fileName = "ramp.jpg",
        base64 = "AQID",
        capturedAtIso = "2026-07-11T12:00:00Z",
        sizeBytes = 3,
    )

    @Test
    fun staleFieldEditPreservesAttachmentThatFinishedInBackground() {
        val current = TaskFormRow(
            localKey = 7,
            description = "Before edit",
            attachments = listOf(attachment),
        )
        val staleEditedRow = TaskFormRow(
            localKey = 7,
            description = "Typed while photo compressed",
            attachments = emptyList(),
        )

        val merged = current.mergeNonAttachmentEdit(staleEditedRow)

        assertEquals("Typed while photo compressed", merged.description)
        assertEquals(listOf(attachment), merged.attachments)
    }

    @Test
    fun attachmentAppendMutatesCurrentTaskWithoutRevertingOtherFields() {
        val form = CreateWorkOrderFormState(
            tasks = listOf(TaskFormRow(localKey = 7, description = "Current description")),
        )

        val updated = form.withTaskAttachmentAdded(7, attachment)

        assertEquals("Current description", updated.tasks.single().description)
        assertEquals(listOf(attachment), updated.tasks.single().attachments)
    }
}
