package com.nags.operations.data

import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.Locale

class FlightInputCanonicalizationTest {
    @Test fun identifiersUseInvariantUppercaseWithoutChangingRemarks() {
        val previous = Locale.getDefault()
        try {
            Locale.setDefault(Locale.forLanguageTag("tr-TR"))
            val input = SaveFlightArrivalRequest("2026-11-13T10:00:00Z", "aircraft", " hi-123 ", " pkg-i ", "Mixed Case")
            val normalized = input.normalized()
            assertEquals("HI-123", normalized.aircraftTailNumber)
            assertEquals("PKG-I", normalized.customerWorkReferenceNumber)
            assertEquals("Mixed Case", normalized.remarks)
            assertEquals(normalized, normalized.normalized())
            assertEquals(" hi-123 ", input.aircraftTailNumber)
        } finally { Locale.setDefault(previous) }
    }

    @Test fun serviceAndTaskReferencesNormalizeWithoutUppercasingNotes() {
        val service = SaveFlightServiceRequest("service", "2026-11-13T10:00:00Z", customerReferenceNumber = " ref-i ", notes = "Mixed Case").normalized()
        val task = SaveFlightTaskRequest(0, "2026-11-13T10:00:00Z", customerReferenceNumber = " ref-i ", notes = "Mixed Case").normalized()
        assertEquals("REF-I", service.customerReferenceNumber)
        assertEquals("REF-I", task.customerReferenceNumber)
        assertEquals("Mixed Case", service.notes)
        assertEquals("Mixed Case", task.notes)
    }

    @Test fun decodingFrozenInputDoesNotRewriteItsBusinessValues() {
        val frozen = """{"ata":"2026-11-13T10:00:00Z","aircraftTypeId":"aircraft","aircraftTailNumber":"hZ-test","customerWorkReferenceNumber":"pkg-a"}"""
        val input = Json.decodeFromString<SaveFlightArrivalRequest>(frozen)
        assertEquals("hZ-test", input.aircraftTailNumber)
        assertEquals("pkg-a", input.customerWorkReferenceNumber)
        assertEquals("HZ-TEST", input.normalized().aircraftTailNumber)
        assertEquals("hZ-test", input.aircraftTailNumber)
    }

    @Test fun oldDepartureCacheRetainsSignatureWhileExplicitUnsignedDoesNot() {
        val legacy = """{"id":"departure","submittedByEmployee":{"staffMemberId":"staff","fullName":"Employee","employeeId":"1"},"atd":"2026-11-13T10:00:00Z","handledByEmployee":{"staffMemberId":"staff","fullName":"Employee","employeeId":"1"},"isAccepted":true,"createdAtUtc":"2026-11-13T10:00:00Z"}"""
        assertTrue(Json.decodeFromString<FlightDepartureSubmission>(legacy).hasSignature)
        assertFalse(Json.decodeFromString<FlightDepartureSubmission>(legacy.dropLast(1) + ",\"hasSignature\":false}").hasSignature)
    }

    @Test fun oldAdHocDetailCacheRecognizesTheFixedOperationTypeWithoutGrantingPlanningEdits() {
        val legacy = """{"id":"flight","flightNumber":"TEST1","customerId":"customer","customerName":"Airline","stationId":"station","stationIata":"AAA","stationName":"Station","operationTypeId":"30000000-0000-0000-0000-000000000001","operationTypeName":"AdHoc","scheduledArrivalUtc":"2026-11-13T10:00:00Z","scheduledDepartureUtc":"2026-11-13T12:00:00Z","isPerLanding":false,"createdAtUtc":"2026-11-13T08:00:00Z","rowVersion":"AAAAAAAAAAE=","stationTimeZoneId":"Etc/UTC"}"""
        val flight = Json.decodeFromString<FlightDetail>(legacy)
        assertTrue(flight.isAdHocFlight)
        assertFalse(flight.canEditFlightDetails)
        assertTrue(flight.assignedEmployees.isEmpty())
        assertFalse(flight.canSaveArrival)
    }
}
