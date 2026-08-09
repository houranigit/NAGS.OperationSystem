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

    @Test
    fun grouped_occurrence_attachments_are_stitched_after_normal_rows() {
        val base = payloadWithSlots(serviceCounts = listOf(1), taskCounts = listOf(1))
        val nestedService = serviceWithSlots("nested-service", 1)
        val nestedTask = taskWithSlots(1)
        val payload = base.copy(
            workOrder = requireNotNull(base.workOrder).copy(
                returnToRamps = listOf(
                    OutboxPayload.ReturnToRampInput(
                        fromIso = "2026-08-08T10:00:00Z",
                        toIso = "2026-08-08T11:00:00Z",
                        serviceLines = listOf(nestedService),
                        tasks = listOf(nestedTask),
                    ),
                ),
            ),
        )
        val stitched = payload.withDurableAttachmentsInServiceThenTaskOrder(
            listOf(
                attachment("0-normal-service.jpg"),
                attachment("1-normal-task.jpg"),
                attachment("2-rtr-service.jpg"),
                attachment("3-rtr-task.jpg"),
            ),
        )
        val occurrence = requireNotNull(stitched.workOrder).returnToRamps.single()
        assertEquals("2-rtr-service.jpg", occurrence.serviceLines.single().attachments.single().relativePath)
        assertEquals("3-rtr-task.jpg", occurrence.tasks.single().attachments.single().relativePath)
    }

    @Test
    fun standalone_occurrence_stitches_without_work_order_payload() {
        val payload = OutboxPayload(
            kind = OutboxPayload.Kind.ReturnToRamp,
            returnToRamp = OutboxPayload.ReturnToRampInput(
                fromIso = "2026-08-08T10:00:00Z",
                toIso = "2026-08-08T11:00:00Z",
                serviceLines = listOf(serviceWithSlots("service", 1)),
                tasks = listOf(taskWithSlots(1)),
            ),
        )

        val stitched = payload.withDurableAttachmentsInServiceThenTaskOrder(
            listOf(attachment("0-service.jpg"), attachment("1-task.jpg")),
        )

        assertEquals(
            listOf("0-service.jpg", "1-task.jpg"),
            requireNotNull(stitched.returnToRamp).let { occurrence ->
                occurrence.serviceLines.flatMap { it.attachments }.map { it.relativePath } +
                    occurrence.tasks.flatMap { it.attachments }.map { it.relativePath }
            },
        )
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

    private fun serviceWithSlots(id: String, count: Int) = OutboxPayload.ServiceLineInput(
        serviceId = id,
        performedByStaffMemberIds = listOf("staff-1"),
        fromIso = "2026-08-08T10:00:00Z",
        toIso = "2026-08-08T10:30:00Z",
        description = null,
        attachments = List(count) { attachment("") },
    )

    private fun taskWithSlots(count: Int) = OutboxPayload.TaskInput(
        taskType = "Minor",
        description = null,
        fromIso = "2026-08-08T10:00:00Z",
        toIso = "2026-08-08T10:30:00Z",
        employeeIds = listOf("staff-1"),
        attachments = List(count) { attachment("") },
    )
}
