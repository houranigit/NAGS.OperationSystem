package com.nags.operations.data.outbox

import org.junit.Assert.assertEquals
import org.junit.Assert.fail
import org.junit.Test

class OutboxAttachmentStitchingTest {
    @Test
    fun durableAttachmentsAreStitchedInServiceThenTaskOrder() {
        val payload = payloadWithSlots(serviceCounts = listOf(1, 1), taskCounts = listOf(1))
        val durable = listOf(
            attachment("0-service-one.jpg"),
            attachment("1-service-two.jpg"),
            attachment("2-task-one.jpg"),
        )

        val stitched = payload.withDurableAttachmentsInServiceThenTaskOrder(durable)
        val workOrder = requireNotNull(stitched.workOrder)

        assertEquals(
            listOf("0-service-one.jpg", "1-service-two.jpg"),
            workOrder.serviceLines.flatMap { it.attachments }.map { it.relativePath },
        )
        assertEquals(
            listOf("2-task-one.jpg"),
            workOrder.tasks.flatMap { it.attachments }.map { it.relativePath },
        )
    }

    @Test
    fun attachmentCountMismatchIsRejectedInsteadOfSilentlyTruncating() {
        val payload = payloadWithSlots(serviceCounts = listOf(1), taskCounts = listOf(1))

        try {
            payload.withDurableAttachmentsInServiceThenTaskOrder(
                listOf(attachment("0-only-one.jpg")),
            )
            fail("Expected mismatched attachment slots to be rejected.")
        } catch (_: IllegalArgumentException) {
            // Expected.
        }
    }

    private fun payloadWithSlots(
        serviceCounts: List<Int>,
        taskCounts: List<Int>,
    ): OutboxPayload = OutboxPayload(
        kind = OutboxPayload.Kind.ForFlight,
        workOrder = OutboxPayload.WorkOrderInput(
            type = "Completion",
            actualFlightNumber = "MOB100",
            aircraftTypeId = "aircraft-1",
            aircraftTailNumber = null,
            ataIso = "2026-07-11T10:00:00Z",
            atdIso = "2026-07-11T12:00:00Z",
            remarks = null,
            serviceLines = serviceCounts.mapIndexed { index, count ->
                OutboxPayload.ServiceLineInput(
                    serviceId = "service-$index",
                    performedByStaffMemberIds = listOf("staff-1"),
                    fromIso = "2026-07-11T10:00:00Z",
                    toIso = "2026-07-11T11:00:00Z",
                    description = null,
                    attachments = List(count) { attachment("") },
                )
            },
            tasks = taskCounts.map { count ->
                OutboxPayload.TaskInput(
                    taskType = "Major",
                    description = null,
                    fromIso = "2026-07-11T10:00:00Z",
                    toIso = "2026-07-11T11:00:00Z",
                    employeeIds = listOf("staff-1"),
                    attachments = List(count) { attachment("") },
                )
            },
        ),
    )

    private fun attachment(relativePath: String) = OutboxPayload.AttachmentInput(
        relativePath = relativePath,
        kind = "Image",
        contentType = "image/jpeg",
        fileName = relativePath.ifBlank { "placeholder.jpg" },
        capturedAtIso = "2026-07-11T10:30:00Z",
        sizeBytes = 3,
    )
}
