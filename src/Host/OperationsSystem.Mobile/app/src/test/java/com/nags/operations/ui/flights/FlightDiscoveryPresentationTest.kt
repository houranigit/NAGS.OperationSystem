package com.nags.operations.ui.flights

import com.nags.operations.data.FlightListItem
import java.time.LocalDate
import java.util.TimeZone
import org.junit.Assert.*
import org.junit.Test

class FlightDiscoveryPresentationTest {
    private fun row(id: String, arrival: String) = FlightListItem(id, "SV123", customerName = "Airline",
        stationIataCode = "JED", operationTypeName = "Transit", scheduledArrivalUtc = arrival,
        scheduledDepartureUtc = arrival, isPerLanding = false, stationTimeZoneId = "Asia/Riyadh")

    @Test fun exactSearchGroupsByStationDateRegardlessOfDeviceTimeZone() {
        val previous = TimeZone.getDefault()
        try {
            TimeZone.setDefault(TimeZone.getTimeZone("America/Los_Angeles"))
            val first = row("one", "2026-11-12T22:00:00Z")
            val second = row("two", "2026-11-12T19:00:00Z")
            val third = row("three", "2026-11-13T01:00:00Z")
            val rows = flightDiscoveryRows(listOf(first, second, third, first), groupDates = true)
            assertEquals(listOf("date:Asia/Riyadh:2026-11-13", "one", "three",
                "date:Asia/Riyadh:2026-11-12", "two"), rows.map { it.key })
            assertEquals(LocalDate.of(2026, 11, 13), (rows.first() as FlightDiscoveryFeedRow.Heading).date)
        } finally {
            TimeZone.setDefault(previous)
        }
    }

    @Test fun historyAndRecentKeepServerInteractionOrderWithoutDateHeadingsOrDuplicateCards() {
        val recent = row("recent", "2026-11-12T20:00:00Z")
        val older = row("older", "2026-11-12T22:00:00Z")
        val rows = flightDiscoveryRows(listOf(recent, older, recent), groupDates = false)
        assertEquals(listOf("recent", "older"), rows.map { it.key })
        assertTrue(rows.all { it is FlightDiscoveryFeedRow.Flight })
        assertTrue(flightDiscoveryRows(emptyList(), groupDates = true).isEmpty())
    }
}
