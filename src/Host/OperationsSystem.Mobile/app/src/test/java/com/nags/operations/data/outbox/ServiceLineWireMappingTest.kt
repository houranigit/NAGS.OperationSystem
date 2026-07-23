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
}
