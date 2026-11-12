package com.nags.operations.data

import kotlinx.serialization.json.Json
import org.junit.Assert.*
import org.junit.Test

class FlightServiceModelsTest {
    private val version = "AAAAAAAAAAE="
    private val start = "2026-11-11T11:00:00Z"
    private val end = "2026-11-11T12:00:00Z"
    private val performer = SaveFlightServicePerformerRequest("11111111-1111-1111-1111-111111111111",
        "22222222-2222-2222-2222-222222222222", start)
    private val input = SaveFlightServiceRequest("33333333-3333-3333-3333-333333333333", start,
        performers = listOf(performer))
    private val record = FlightServiceRecord("44444444-4444-4444-4444-444444444444", input.serviceId,
        "Service", start, createdByUserId = "creator", createdAtUtc = start, canEdit = true, canSetEnd = true)

    @Test fun openWorkIsAllowedButServiceEndRequiresEveryStaffEnd() {
        assertTrue(input.validationErrors(version).isEmpty())
        assertEquals(setOf("endUtc", "performers.${performer.id}.endUtc"), input.copy(endUtc = end).validationErrors(version))
        val finished = performer.copy(endUtc = end)
        assertTrue(input.copy(performers = listOf(finished)).validationErrors(version).isEmpty())
        assertTrue(input.copy(endUtc = end, performers = listOf(finished)).validationErrors(version).isEmpty())
        assertEquals(setOf("endUtc", "performers.${performer.id}.endUtc"), input.copy(endUtc = end,
            performers = listOf(finished.copy(id = "55555555-5555-5555-5555-555555555555"), performer.copy(staffMemberId = "66666666-6666-6666-6666-666666666666")))
            .validationErrors(version))
    }

    @Test fun legacyCapabilitiesDeriveProtectionFromCurrentForeignServiceEndAndRespectExplicitDenial() {
        assertTrue(record.serviceSelectionEditable())
        assertTrue(record.serviceDeletable())
        val ownEnd = record.copy(endUtc = end, endedByUserId = "CREATOR")
        assertTrue(ownEnd.serviceSelectionEditable())
        assertTrue(ownEnd.serviceDeletable())
        val foreignEnd = ownEnd.copy(endedByUserId = "other")
        assertFalse(foreignEnd.serviceSelectionEditable())
        assertFalse(foreignEnd.serviceDeletable())
        assertTrue(foreignEnd.copy(endUtc = null, endedByUserId = null).serviceSelectionEditable())
        val foreignFile = record.copy(attachments = listOf(FlightServiceAttachmentRecord("file", 2, "file.pdf",
            "application/pdf", 594, uploadedByUserId = "other")), canChangeService = true, canDelete = true)
        assertTrue(foreignFile.serviceSelectionEditable())
        assertTrue(foreignFile.serviceDeletable())
        assertFalse(record.copy(canChangeService = false).serviceSelectionEditable())
        assertFalse(record.copy(canDelete = false).serviceDeletable())
        assertFalse(record.copy(canEdit = false, canChangeService = true, canDelete = true).serviceSelectionEditable())
        assertFalse(record.copy(canEdit = false, canChangeService = true, canDelete = true).serviceDeletable())
    }

    @Test fun omittedCapabilitiesInOldCacheRemainUsableAndDoNotGrantRightsToOtherEmployees() {
        val cached = """{"id":"${record.id}","serviceId":"${record.serviceId}","serviceName":"Service","startUtc":"$start","createdByUserId":"creator","createdAtUtc":"$start","canEdit":true,"canSetEnd":true}"""
        val restored = Json.decodeFromString<FlightServiceRecord>(cached)
        assertNull(restored.canChangeService)
        assertNull(restored.canDelete)
        assertTrue(restored.serviceSelectionEditable())
        assertTrue(restored.serviceDeletable())
        assertFalse(restored.copy(canEdit = false).serviceSelectionEditable())
        assertFalse(restored.copy(canEdit = false).serviceDeletable())
    }
}
