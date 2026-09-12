package com.nags.operations.data.outbox

import com.nags.operations.data.api.WorkOrderTaskAttachmentInput
import org.junit.Assert.assertTrue
import org.junit.Assert.assertEquals
import org.junit.Test

class ServiceLineWireMappingTest {
    @Test
    fun worker_mapping_preserves_return_to_ramp_flag() {
        val wire = OutboxPayload.ServiceLineInput(
            id = "line-1",
            serviceId = "service-1",
            performedByStaffMemberIds = listOf("staff-1", "staff-2"),
            fromIso = "2026-07-11T10:00:00Z",
            toIso = "2026-07-11T11:00:00Z",
            description = null,
            isReturnToRamp = true,
        ).toWireServiceLine(
            wireAttachments = listOf(
                WorkOrderTaskAttachmentInput(
                    kind = "Document",
                    base64Content = "AQID",
                    fileName = "service.pdf",
                    contentType = "application/pdf",
                ),
            ),
        )

        assertTrue(wire.isReturnToRamp)
        assertEquals("line-1", wire.id)
        assertEquals(listOf("staff-1", "staff-2"), wire.performedByStaffMemberIds)
        assertEquals("service.pdf", wire.attachments.single().fileName)
    }
    @Test
    fun legacy_retry_keeps_assignment_absence_while_explicit_periods_survive_serialization() {
        val json = kotlinx.serialization.json.Json { encodeDefaults = true }
        val old = json.decodeFromString<OutboxPayload.ServiceLineInput>(
            """{"serviceId":"service-1","performedByStaffMemberIds":["staff-1"],"fromIso":"2026-07-11T10:00:00Z","toIso":"2026-07-11T11:00:00Z","description":null}""",
        )
        val legacyWire = old.toWireServiceLine()
        org.junit.Assert.assertNull(legacyWire.employeeAssignments)
        val oldJson = json.encodeToString(com.nags.operations.data.api.WorkOrderServiceLineInput.serializer(), legacyWire)
        org.junit.Assert.assertFalse(oldJson.contains("employeeAssignments"))

        val current = old.copy(employeeAssignments = listOf(
            OutboxPayload.EmployeeAssignmentInput("staff-1", "2026-07-11T10:15:00Z", "2026-07-11T10:45:00Z"),
        ))
        val persisted = json.decodeFromString<OutboxPayload.ServiceLineInput>(json.encodeToString(OutboxPayload.ServiceLineInput.serializer(), current))
        val wire = persisted.toWireServiceLine()
        assertEquals("2026-07-11T10:15:00Z", wire.employeeAssignments!!.single().fromUtc)
        assertEquals("2026-07-11T10:45:00Z", wire.employeeAssignments.single().toUtc)
    }

    @Test
    fun legacy_task_retry_keeps_assignment_absence() {
        val json = kotlinx.serialization.json.Json { encodeDefaults = true }
        val task = json.decodeFromString<OutboxPayload.TaskInput>(
            """{"taskType":"Major","description":null,"fromIso":"2026-07-11T10:00:00Z","toIso":"2026-07-11T11:00:00Z","employeeIds":["staff-1"]}""",
        )
        org.junit.Assert.assertNull(task.employeeAssignments.toWireAssignments())
    }
}
