package com.nags.operations.data.outbox

import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class OutboxPayloadCompatibilityTest {
    private val json = Json { ignoreUnknownKeys = true }

    @Test
    fun legacy_payload_without_base_revision_still_decodes() {
        val payload = json.decodeFromString<OutboxPayload>(
            """{"kind":"CancelFlight","cancelFlight":{"canceledAtIso":"2026-07-11T10:00:00Z","reason":"Weather"}}""",
        )

        assertNull(payload.baseRowVersion)
    }

    @Test
    fun update_payload_round_trips_base_revision() {
        val payload = OutboxPayload(
            kind = OutboxPayload.Kind.UpdateExisting,
            workOrder = OutboxPayload.WorkOrderInput(
                type = "Completion",
                actualFlightNumber = "MOB100",
                aircraftTypeId = "aircraft-1",
                aircraftTailNumber = null,
                ataIso = "2026-07-11T10:00:00Z",
                atdIso = "2026-07-11T12:00:00Z",
                remarks = null,
                serviceLines = emptyList(),
                tasks = emptyList(),
            ),
            baseRowVersion = "AQID",
        )

        val decoded = json.decodeFromString<OutboxPayload>(json.encodeToString(OutboxPayload.serializer(), payload))

        assertEquals("AQID", decoded.baseRowVersion)
        assertEquals(0, decoded.workOrder?.serviceLineIdentityVersion)
    }

    @Test
    fun legacy_service_line_migrates_single_performer_and_defaults_return_to_ramp_to_false() {
        val payload = decodeOutboxPayload(
            json,
            """{
                "kind":"ForFlight",
                "workOrder":{
                  "type":"Completion",
                  "actualFlightNumber":"MOB100",
                  "aircraftTypeId":null,
                  "aircraftTailNumber":null,
                  "ataIso":null,
                  "atdIso":null,
                  "remarks":null,
                  "serviceLines":[{
                    "serviceId":"service-1",
                    "performedByStaffMemberId":"staff-1",
                    "fromIso":"2026-07-11T10:00:00Z",
                    "toIso":"2026-07-11T11:00:00Z",
                    "description":null
                  }],
                  "tasks":[]
                }
            }""".trimIndent(),
        )
        val line = payload.workOrder!!.serviceLines.single()

        assertFalse(line.isReturnToRamp)
        assertEquals(listOf("staff-1"), line.performedByStaffMemberIds)
    }

    @Test
    fun return_to_ramp_service_line_round_trips_through_outbox_json() {
        val line = OutboxPayload.ServiceLineInput(
            id = "line-1",
            serviceId = "service-1",
            performedByStaffMemberIds = listOf("staff-1", "staff-2"),
            fromIso = "2026-07-11T10:00:00Z",
            toIso = "2026-07-11T11:00:00Z",
            description = null,
            isReturnToRamp = true,
        )

        val decoded = json.decodeFromString<OutboxPayload.ServiceLineInput>(
            json.encodeToString(OutboxPayload.ServiceLineInput.serializer(), line),
        )

        assertTrue(decoded.isReturnToRamp)
        assertEquals("line-1", decoded.id)
        assertEquals(listOf("staff-1", "staff-2"), decoded.performedByStaffMemberIds)
    }

    @Test
    fun legacy_task_without_return_to_ramp_flag_defaults_to_false() {
        val task = json.decodeFromString<OutboxPayload.TaskInput>(
            """{
                "taskType":"Major",
                "description":null,
                "fromIso":"2026-07-11T10:00:00Z",
                "toIso":"2026-07-11T11:00:00Z",
                "employeeIds":["staff-1"]
            }""".trimIndent(),
        )

        assertFalse(task.isReturnToRamp)
    }

    @Test
    fun return_to_ramp_task_round_trips_through_outbox_json() {
        val task = OutboxPayload.TaskInput(
            taskType = "Major",
            description = null,
            fromIso = "2026-07-11T10:00:00Z",
            toIso = "2026-07-11T11:00:00Z",
            employeeIds = listOf("staff-1"),
            isReturnToRamp = true,
        )

        val decoded = json.decodeFromString<OutboxPayload.TaskInput>(
            json.encodeToString(OutboxPayload.TaskInput.serializer(), task),
        )

        assertTrue(decoded.isReturnToRamp)
    }
}
