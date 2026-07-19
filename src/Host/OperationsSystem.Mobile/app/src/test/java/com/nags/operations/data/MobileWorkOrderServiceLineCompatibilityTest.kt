package com.nags.operations.data

import com.nags.operations.data.api.WorkOrderServiceLineInput
import kotlinx.serialization.json.Json
import org.junit.Assert.assertFalse
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class MobileWorkOrderServiceLineCompatibilityTest {
    private val json = Json { ignoreUnknownKeys = true }

    @Test
    fun legacy_cached_and_request_lines_default_return_to_ramp_to_false() {
        val cached = json.decodeFromString<WorkOrderServiceLineWireDto>(
            """{
                "id":"line-1",
                "serviceId":"service-1",
                "serviceName":"Baggage",
                "performedByStaffMemberId":"staff-1",
                "performedByName":"Staff One",
                "fromUtc":"2026-07-11T10:00:00Z",
                "toUtc":"2026-07-11T11:00:00Z"
            }""".trimIndent(),
        )
        val request = json.decodeFromString<WorkOrderServiceLineInput>(
            """{
                "serviceId":"service-1",
                "performedByStaffMemberId":"staff-1",
                "fromUtc":"2026-07-11T10:00:00Z",
                "toUtc":"2026-07-11T11:00:00Z"
            }""".trimIndent(),
        )

        assertFalse(cached.isReturnToRamp)
        assertFalse(request.isReturnToRamp)
    }

    @Test
    fun request_line_round_trips_return_to_ramp_flag() {
        val request = WorkOrderServiceLineInput(
            id = "line-1",
            serviceId = "service-1",
            performedByStaffMemberId = "staff-1",
            fromUtc = "2026-07-11T10:00:00Z",
            toUtc = "2026-07-11T11:00:00Z",
            isReturnToRamp = true,
        )

        val decoded = json.decodeFromString<WorkOrderServiceLineInput>(
            json.encodeToString(WorkOrderServiceLineInput.serializer(), request),
        )

        assertTrue(decoded.isReturnToRamp)
        assertEquals("line-1", decoded.id)
    }
}
