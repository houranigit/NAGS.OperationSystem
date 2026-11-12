package com.nags.operations.data

import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import org.junit.Assert.*
import org.junit.Test

class FlightTaskModelsTest {
    private val version = "AAAAAAAAAAE="
    private val start = "2026-11-11T11:00:00Z"
    private val end = "2026-11-11T12:00:00Z"
    private val id = "11111111-1111-1111-1111-111111111111"
    private val resource = "22222222-2222-2222-2222-222222222222"
    private val performer = SaveFlightTaskPerformerRequest("33333333-3333-3333-3333-333333333333",
        "44444444-4444-4444-4444-444444444444", start)
    private val input = SaveFlightTaskRequest(0, start, performers = listOf(performer))
    private val quantityTypes = FlightTaskResourceCatalogs(materials = mapOf(resource to 0))
    private val durationTypes = FlightTaskResourceCatalogs(tools = mapOf(resource to 1))

    @Test fun bothTypesRequireIdentifiedUniquePerformersAndValidPeriods() {
        for (type in 0..1) {
            assertTrue(input.copy(taskType = type).validationErrors(version).isEmpty())
            assertEquals(setOf("performers"), input.copy(taskType = type, performers = emptyList()).validationErrors(version))
        }
        assertEquals(setOf("taskType"), input.copy(taskType = 2).validationErrors(version))
        assertTrue("performers" in input.copy(performers = listOf(performer, performer)).validationErrors(version))
        assertTrue("performers.${performer.id}.startUtc" in input.copy(performers = listOf(performer.copy(startUtc = "bad"))).validationErrors(version))
    }

    @Test fun taskClosingRequiresStaffAndDurationResourceEndsWhileQuantityHasNoPeriod() {
        val openTool = SaveFlightTaskResourceRequest(id, resource, startUtc = start)
        val closed = input.copy(endUtc = end, performers = listOf(performer.copy(endUtc = end)), tools = listOf(openTool))
        assertEquals(setOf("endUtc", "tools.$id.endUtc"), closed.validationErrors(version, resourceCatalogs = durationTypes))
        assertTrue(closed.copy(tools = listOf(openTool.copy(endUtc = end))).validationErrors(version, resourceCatalogs = durationTypes).isEmpty())
        assertEquals(setOf("endUtc", "performers.${performer.id}.endUtc"), input.copy(endUtc = end).validationErrors(version))
        assertTrue(closed.copy(tools = emptyList(), materials = listOf(SaveFlightTaskResourceRequest(id, resource, quantity = "1")))
            .validationErrors(version, resourceCatalogs = quantityTypes).isEmpty())
    }

    @Test fun openDurationFromMayExceedParentEndUntilTaskClosureRequiresItsEnd() {
        val openTool = SaveFlightTaskResourceRequest(id, resource, startUtc = "2026-11-11T13:00:00Z")
        val open = input.copy(tools = listOf(openTool))
        assertTrue(open.validationErrors(version, resourceCatalogs = durationTypes).isEmpty())
        val closed = open.copy(endUtc = end, performers = listOf(performer.copy(endUtc = end)))
        assertEquals(setOf("endUtc", "tools.$id.endUtc"), closed.validationErrors(version, resourceCatalogs = durationTypes))
        assertEquals(setOf("tools.$id.endUtc"), closed.copy(tools = listOf(openTool.copy(endUtc = "2026-11-11T14:00:00Z")))
            .validationErrors(version, resourceCatalogs = durationTypes))
        assertTrue(closed.validationErrors(version, resourceCatalogs = durationTypes, requireClosedChildren = false).isEmpty())
    }

    @Test fun actualDepartureRequiresCompletedTaskStaffAndDurationResourcePeriods() {
        val open = input.copy(tools = listOf(SaveFlightTaskResourceRequest(id, resource, startUtc = start)))
        assertEquals(setOf("endUtc", "performers.${performer.id}.endUtc", "tools.$id.endUtc"),
            open.validationErrors(version, actualDepartureUtc = end, resourceCatalogs = durationTypes))
        val completed = open.copy(endUtc = end, performers = listOf(performer.copy(endUtc = end)),
            tools = open.tools.map { it.copy(endUtc = end) })
        assertTrue(completed.validationErrors(version, actualDepartureUtc = end, resourceCatalogs = durationTypes).isEmpty())
    }

    @Test fun quantityKeepsDecimalPrecisionAndRejectsMixedBranchesAndInvalidValues() {
        for (valid in listOf("0.01", "1", "9999999999999999.99", "1.000")) {
            val task = input.copy(materials = listOf(SaveFlightTaskResourceRequest(id, resource, quantity = valid)))
            assertTrue(valid, task.validationErrors(version, resourceCatalogs = quantityTypes).isEmpty())
        }
        for (invalid in listOf("0", "-1", "0.001", "10000000000000000", "1e100000000", "1e-100000000", "NaN", "")) {
            val task = input.copy(materials = listOf(SaveFlightTaskResourceRequest(id, resource, quantity = invalid))).normalized()
            assertEquals(invalid, setOf("materials.$id.quantity"), task.validationErrors(version, resourceCatalogs = quantityTypes))
        }
        val mixed = input.copy(materials = listOf(SaveFlightTaskResourceRequest(id, resource, "1", start, end)))
        assertEquals(setOf("materials.$id.startUtc", "materials.$id.endUtc"), mixed.validationErrors(version, resourceCatalogs = quantityTypes))
        assertEquals("9999999999999999.99", input.copy(materials = listOf(SaveFlightTaskResourceRequest(id, resource,
            "9999999999999999.99"))).normalized().materials.single().quantity)
    }

    @Test fun authoritativeResourceBranchesAndStableHistoricalSnapshotsArePreserved() {
        val tool = SaveFlightTaskResourceRequest(id, resource, startUtc = start)
        val value = input.copy(tools = listOf(tool))
        assertTrue(value.validationErrors(version, resourceCatalogs = durationTypes).isEmpty())
        assertEquals(setOf("tools.$id.resourceId"), value.validationErrors(version, resourceCatalogs = FlightTaskResourceCatalogs()))
        val saved = FlightTaskRecord("task", 0, start, createdByUserId = "creator", createdAtUtc = start,
            tools = listOf(FlightTaskResourceRecord(id, resource, "Historical tool", 1, startUtc = start)))
        assertTrue(value.validationErrors(version, resourceCatalogs = FlightTaskResourceCatalogs(), baseline = saved).isEmpty())
        assertEquals(setOf("tools.55555555-5555-5555-5555-555555555555.resourceId"), value.copy(tools = listOf(
            tool.copy(id = "55555555-5555-5555-5555-555555555555"))).validationErrors(version, resourceCatalogs = FlightTaskResourceCatalogs(), baseline = saved))
    }

    @Test fun quantityWireRoundTripIsExactAndEquivalentResourceOrderNormalizes() {
        val quantity = "9999999999999999.99"
        val resourceInput = SaveFlightTaskResourceRequest(id, resource, quantity = quantity)
        val value = input.copy(materials = listOf(resourceInput)).normalized()
        val raw = Json.encodeToString(value)
        assertTrue(raw.contains("\"quantity\":\"$quantity\""))
        assertEquals(value, Json.decodeFromString<SaveFlightTaskRequest>(raw))
        val saved = FlightTaskRecord("task", 0, start, createdByUserId = "creator", createdAtUtc = start,
            materials = listOf(FlightTaskResourceRecord(id, resource, "Material", 0, quantity = quantity)))
        assertEquals(quantity, saved.input().materials.single().quantity)
    }

    @Test fun nestedActualBoundsUnknownResourceNotesAndTextLimitsStayFieldSpecific() {
        val unknown = "60000000-0000-0000-0000-000000000001"
        val value = input.copy(tools = listOf(SaveFlightTaskResourceRequest(id, unknown, startUtc = start)),
            notes = "x".repeat(2001), performers = listOf(performer.copy(notes = "x".repeat(2001))))
        val errors = value.validationErrors(version, actualArrivalUtc = end,
            resourceCatalogs = FlightTaskResourceCatalogs(tools = mapOf(unknown to 1)))
        assertEquals(setOf("startUtc", "notes", "performers.${performer.id}.notes", "performers.${performer.id}.startUtc",
            "tools.$id.notes"), errors)
        assertTrue(input.copy(tools = listOf(SaveFlightTaskResourceRequest(id, unknown, startUtc = start, notes = "Explained")))
            .validationErrors(version, resourceCatalogs = FlightTaskResourceCatalogs(tools = mapOf(unknown to 1))).isEmpty())
    }
}
