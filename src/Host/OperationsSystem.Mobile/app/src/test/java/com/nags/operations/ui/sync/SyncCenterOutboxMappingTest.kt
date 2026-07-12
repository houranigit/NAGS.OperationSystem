package com.nags.operations.ui.sync

import com.nags.operations.data.db.entities.WorkOrderOutboxEntity
import com.nags.operations.data.outbox.OutboxPayload
import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class SyncCenterOutboxMappingTest {
    private val json = Json { encodeDefaults = true }

    @Test
    fun failed_existing_flight_is_retryable_and_uses_cached_flight_number() {
        val row = entity(
            status = WorkOrderOutboxEntity.STATUS_FAILED,
            attempts = 3,
            error = "Validation failed",
            payload = OutboxPayload(
                kind = OutboxPayload.Kind.ForFlight,
                workOrder = minimalWorkOrder(actualFlightNumber = "changed-number"),
            ),
        )

        val mapped = mapOutboxRow(row, mapOf("flight-id" to "NAGS 204"))

        assertEquals("Create work order", mapped.kindLabel)
        assertEquals("Flight NAGS 204", mapped.flightLabel)
        assertEquals(OutboxStatus.Failed, mapped.status)
        assertEquals(3, mapped.attempts)
        assertEquals("Validation failed", mapped.lastError)
        assertTrue(mapped.canRetry)
        assertTrue(mapped.canDiscard)
    }

    @Test
    fun conflict_scratch_uses_payload_flight_number_and_cannot_be_retried() {
        val row = entity(
            status = WorkOrderOutboxEntity.STATUS_CONFLICT,
            attempts = 1,
            error = "The flight was changed elsewhere.",
            payload = OutboxPayload(
                kind = OutboxPayload.Kind.ScratchAdHoc,
                workOrder = minimalWorkOrder(actualFlightNumber = null),
                scratchFlight = OutboxPayload.ScratchFlightInput(
                    customerId = "customer-id",
                    flightNumber = "ADH 12",
                    staIso = "2026-07-11T10:00:00Z",
                    stdIso = "2026-07-11T11:00:00Z",
                ),
            ),
        )

        val mapped = mapOutboxRow(row, emptyMap())

        assertEquals("Create ad-hoc work order", mapped.kindLabel)
        assertEquals("Flight ADH 12", mapped.flightLabel)
        assertEquals(OutboxStatus.Conflict, mapped.status)
        assertFalse(mapped.canRetry)
        assertTrue(mapped.canDiscard)
    }

    @Test
    fun pending_unknown_payload_stays_visible_without_recovery_actions() {
        val row = WorkOrderOutboxEntity(
            clientMutationId = "mutation-id",
            flightId = "12345678-rest",
            flightKind = WorkOrderOutboxEntity.FLIGHT_KIND_MY,
            clientFlightId = null,
            payloadJson = "not-json",
            attachmentsDir = null,
            status = WorkOrderOutboxEntity.STATUS_PENDING,
            attempts = 0,
            lastError = null,
            createdAtEpochMs = 1L,
            updatedAtEpochMs = 1L,
            serverWorkOrderId = null,
        )

        val mapped = mapOutboxRow(row, emptyMap())

        assertEquals("Queued operation", mapped.kindLabel)
        assertEquals("Flight 12345678", mapped.flightLabel)
        assertEquals(OutboxStatus.Pending, mapped.status)
        assertFalse(mapped.canRetry)
        assertFalse(mapped.canDiscard)
    }

    private fun entity(
        status: Int,
        attempts: Int,
        error: String?,
        payload: OutboxPayload,
    ) = WorkOrderOutboxEntity(
        clientMutationId = "mutation-id",
        flightId = "flight-id",
        flightKind = WorkOrderOutboxEntity.FLIGHT_KIND_MY,
        clientFlightId = null,
        payloadJson = json.encodeToString(OutboxPayload.serializer(), payload),
        attachmentsDir = null,
        status = status,
        attempts = attempts,
        lastError = error,
        createdAtEpochMs = 1L,
        updatedAtEpochMs = 1L,
        serverWorkOrderId = null,
    )

    private fun minimalWorkOrder(actualFlightNumber: String?) = OutboxPayload.WorkOrderInput(
        type = "Completion",
        actualFlightNumber = actualFlightNumber,
        aircraftTypeId = "aircraft-id",
        aircraftTailNumber = null,
        ataIso = "2026-07-11T10:00:00Z",
        atdIso = "2026-07-11T11:00:00Z",
        remarks = null,
        serviceLines = emptyList(),
        tasks = emptyList(),
    )
}
