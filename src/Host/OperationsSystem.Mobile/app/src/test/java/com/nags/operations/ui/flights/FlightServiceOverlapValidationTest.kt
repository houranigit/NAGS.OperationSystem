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

    @Test fun unknownServiceUsesTheSameNonoverlapRuleWhileDifferentServicesMayOverlap() {
        val unknown = "40000000-0000-0000-0000-000000000003"
        val rows = listOf(service("a", "10:00", "12:00", unknown), service("b", "10:00", "12:00", unknown),
            service("c", "10:00", "12:00"), service("d", "10:00", "12:00", "another"))
        assertEquals(setOf("a", "b"), serviceOverlapValidation(rows, zone, rows.map { it.id }.toSet()).keys)
    }

    @Test fun identicalZeroDurationOccurrencesAreNotDifferentWindows() {
        val rows = listOf(service("a", "10:30", "10:30"), service("b", "10:30", "10:30"))
        assertEquals(setOf("a", "b"), serviceOverlapValidation(rows, zone, setOf("a", "b")).keys)
        assertTrue(serviceOverlapValidation(listOf(rows.first(), service("b", "10:31", "10:31")), zone, setOf("a", "b")).isEmpty())
    }

    @Test fun offsetEquivalentWindowsStillConflictAndChangingAConflictClearsIt() {
        val first = service("a", "10:00", "11:00")
        val second = service("b", "10:00", "11:00").copy(period = FlightWorkPeriod("2026-11-13T10:00", "2026-11-13T11:00",
            "2026-11-13T12:00:00+02:00", "2026-11-13T13:00:00+02:00"))
        assertEquals(setOf("b"), serviceOverlapValidation(listOf(first, second), zone, setOf("b")).keys)
        assertTrue(serviceOverlapValidation(listOf(first, service("b", "11:00", "12:00")), zone, setOf("b")).isEmpty())
    }
}
