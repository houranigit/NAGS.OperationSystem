package com.nags.operations.ui.workorder

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
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

    @Test
    fun staleServiceFieldEditPreservesAttachmentThatFinishedInBackground() {
        val current = ServiceLineFormRow(
            localKey = 9,
            description = "Before edit",
            attachments = listOf(attachment),
            existingAttachmentNames = listOf("server.pdf"),
        )
        val staleEditedRow = ServiceLineFormRow(
            localKey = 9,
            description = "Typed while photo compressed",
        )

        val merged = current.mergeNonAttachmentEdit(staleEditedRow)

        assertEquals("Typed while photo compressed", merged.description)
        assertEquals(listOf(attachment), merged.attachments)
        assertEquals(listOf("server.pdf"), merged.existingAttachmentNames)
    }

    @Test
    fun serviceAttachmentAddAndRemoveMutateOnlyTheTargetLine() {
        val otherAttachment = attachment.copy(fileName = "other.jpg")
        val form = CreateWorkOrderFormState(
            serviceLines = listOf(
                ServiceLineFormRow(localKey = 7, description = "Target"),
                ServiceLineFormRow(
                    localKey = 8,
                    description = "Other",
                    attachments = listOf(otherAttachment),
                ),
            ),
        )

        val added = form.withServiceLineAttachmentAdded(7, attachment)
        val removed = added.withServiceLineAttachmentRemoved(7, attachment)

        assertEquals(listOf(attachment), added.serviceLines.first().attachments)
        assertEquals(listOf(otherAttachment), added.serviceLines.last().attachments)
        assertTrue(removed.serviceLines.first().attachments.isEmpty())
        assertEquals("Target", removed.serviceLines.first().description)
    }

    @Test
    fun existingAndNewServiceAttachmentsShareTheLimit() {
        val form = CreateWorkOrderFormState(
            serviceLines = listOf(
                ServiceLineFormRow(
                    localKey = 7,
                    existingAttachmentNames = List(WorkOrderFormLimits.ServiceAttachments) {
                        "existing-$it.pdf"
                    },
                ),
            ),
        )

        val unchanged = form.withServiceLineAttachmentAdded(7, attachment)

        assertTrue(unchanged.serviceLines.single().attachments.isEmpty())
    }

    @Test
    fun outboxCollectionUsesDeterministicServiceThenTaskOrder() {
        val serviceFirst = attachment.copy(fileName = "service-1.jpg")
        val serviceSecond = attachment.copy(fileName = "service-2.jpg")
        val taskFirst = attachment.copy(fileName = "task-1.jpg")
        val form = CreateWorkOrderFormState(
            serviceLines = listOf(
                ServiceLineFormRow(localKey = 1, attachments = listOf(serviceFirst)),
                ServiceLineFormRow(localKey = 2, attachments = listOf(serviceSecond)),
            ),
            tasks = listOf(
                TaskFormRow(localKey = 3, attachments = listOf(taskFirst)),
            ),
        )

        assertEquals(
            listOf("service-1.jpg", "service-2.jpg", "task-1.jpg"),
            collectAttachmentsForOutbox(form).map { it.fileName },
        )
    }
}
