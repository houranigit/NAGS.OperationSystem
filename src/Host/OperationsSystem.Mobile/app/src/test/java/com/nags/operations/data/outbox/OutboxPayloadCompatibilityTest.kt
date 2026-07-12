package com.nags.operations.data.outbox

import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
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
    }
}
