package com.nags.operations.data.outbox

import org.junit.Assert.assertTrue
import org.junit.Assert.assertEquals
import org.junit.Test

class ServiceLineWireMappingTest {
    @Test
    fun worker_mapping_preserves_return_to_ramp_flag() {
        val wire = OutboxPayload.ServiceLineInput(
            id = "line-1",
            serviceId = "service-1",
            performedByStaffMemberId = "staff-1",
            fromIso = "2026-07-11T10:00:00Z",
            toIso = "2026-07-11T11:00:00Z",
            description = null,
            isReturnToRamp = true,
        ).toWireServiceLine()

        assertTrue(wire.isReturnToRamp)
        assertEquals("line-1", wire.id)
    }
}
