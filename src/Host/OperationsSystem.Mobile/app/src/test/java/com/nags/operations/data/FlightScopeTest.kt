package com.nags.operations.data

import java.time.Instant
import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test

class FlightScopeTest {
    private val now = Instant.parse("2026-11-05T12:00:00Z")

    @Test
    fun intervalOverlapIncludesLongFlightsAndBothTouchingBoundaries() {
        assertTrue(row("2026-11-01T00:00:00Z", "2026-11-05T00:00:00Z").isInFlightWindow(now))
        assertTrue(row("2026-11-06T00:00:00Z", "2026-11-06T00:00:00Z").isInFlightWindow(now))
        assertFalse(row("2026-11-01T00:00:00Z", "2026-11-04T23:59:59.999Z").isInFlightWindow(now))
        assertFalse(row("2026-11-06T00:00:00.001Z", "2026-11-06T01:00:00Z").isInFlightWindow(now))
        assertFalse(row("invalid", "2026-11-05T12:00:00Z").isInFlightWindow(now))
        assertFalse(row("2026-11-05T12:00:00Z", "2026-11-05T11:00:00Z").isInFlightWindow(now))
    }

    @Test
    fun numericOffsetsAndFractionalSecondsPreserveFlightWindowBoundaries() {
        val at = parseFlightInstant("2026-11-05T15:00:00.1234567+03:00")
        assertEquals(Instant.parse("2026-11-05T12:00:00.1234567Z"), at)
        assertTrue(row("2026-11-06T03:00:00.1234567+03:00",
            "2026-11-06T03:00:00.1234567+03:00").isInFlightWindow(at))
        assertFalse(row("2026-11-06T00:00:00.1234568+00:00",
            "2026-11-06T01:00:00+00:00").isInFlightWindow(at))
    }

    @Test
    fun serverScopeAcceptsNumericOffsetsWithoutRelaxingItsWindow() {
        val page = FlightPage(stationId = "station", asOfUtc = "2026-11-05T12:00:00.1234567+00:00",
            windowStartUtc = "2026-11-05T03:00:00.1234567+03:00",
            windowEndUtc = "2026-11-06T00:00:00.1234567+00:00")

        assertNotNull(page.scopeOrNull())
        assertEquals(null, page.copy(windowEndUtc = "2026-11-06T00:00:00.1234568+00:00").scopeOrNull())
    }

    @Test
    fun mobileContractReadsScopeAndServerMembershipWithoutInferringOperationNames() {
        val page = Json.decodeFromString<FlightPage>(
            """{"items":[{"id":"one","flightNumber":"123","customerName":"Customer",
              "stationIataCode":"JED","stationTimeZoneId":"Asia/Riyadh","stationId":"station","operationTypeName":"Custom label",
              "scheduledArrivalUtc":"2026-11-05T12:00:00Z","scheduledDepartureUtc":"2026-11-05T13:00:00Z",
              "isPerLanding":true,"isMyFlight":false,"isMyAdHocFlight":true}],
              "stationId":"station","asOfUtc":"2026-11-05T12:00:00Z",
              "windowStartUtc":"2026-11-05T00:00:00Z","windowEndUtc":"2026-11-06T00:00:00Z"}""",
        )

        assertNotNull(page.scopeOrNull())
        val mixed = page.items.single()
        assertEquals("station", mixed.stationId)
        assertTrue(mixed.isPerLanding)
        assertTrue(mixed.matches(FlightCategory.AdHoc))
        assertFalse(mixed.matches(FlightCategory.PerLanding))
        assertFalse(mixed.matches(FlightCategory.MyFlights))
        assertTrue(mixed.matches(null))
    }

    private fun row(sta: String, std: String) = FlightListItem("one", "123", customerName = "Customer",
        stationIataCode = "JED", operationTypeName = "Transit", scheduledArrivalUtc = sta,
        scheduledDepartureUtc = std, isPerLanding = false, stationTimeZoneId = "Asia/Riyadh")
}
