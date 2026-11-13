package com.nags.operations.ui.flights

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.ZoneId

class FlightServiceOverlapValidationTest {
    private val zone = ZoneId.of("UTC")
    private fun service(id: String, start: String, end: String? = null, serviceId: String = "known") =
        FlightServiceDraft(id = id, serviceId = serviceId,
            period = FlightWorkPeriod(start = "2026-11-13T$start", end = end?.let { "2026-11-13T$it" }.orEmpty()))

    @Test fun adjacentPeriodsAndZeroDurationRemainValid() {
        val rows = listOf(service("a", "10:00", "11:00"), service("b", "11:00", "12:00"), service("c", "10:30", "10:30"))
        assertTrue(serviceOverlapValidation(rows, zone, setOf("a", "b", "c")).isEmpty())
    }

    @Test fun ongoingPeriodConflictsUntilItIsClosed() {
        val open = service("a", "10:00")
        val later = service("b", "12:00", "13:00")
        assertEquals(setOf("b"), serviceOverlapValidation(listOf(open, later), zone, setOf("b")).keys)
        assertTrue(serviceOverlapValidation(listOf(service("a", "10:00", "12:00"), later), zone, setOf("a", "b")).isEmpty())
        assertTrue(serviceOverlapValidation(listOf(service("earlier", "08:00", "10:00"), open), zone, setOf("earlier")).isEmpty())
    }

    @Test fun unrelatedHistoricalOverlapDoesNotBlockAnotherSave() {
        val rows = listOf(service("a", "10:00", "12:00"), service("b", "11:00", "13:00"), service("c", "14:00", "15:00"))
        assertTrue(serviceOverlapValidation(rows, zone, setOf("c")).isEmpty())
        assertEquals(setOf("a"), serviceOverlapValidation(rows, zone, setOf("a")).keys)
    }

    @Test fun unknownAndDifferentServicesMayOverlap() {
        val unknown = "40000000-0000-0000-0000-000000000003"
        val rows = listOf(service("a", "10:00", "12:00", unknown), service("b", "10:00", "12:00", unknown),
            service("c", "10:00", "12:00"), service("d", "10:00", "12:00", "another"))
        assertTrue(serviceOverlapValidation(rows, zone, rows.map { it.id }.toSet()).isEmpty())
    }
}
