package com.nags.operations.data

import java.time.Instant
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class MobileFlightWindowTest {
    private val sta = Instant.parse("2026-07-18T18:00:00Z")

    @Test
    fun windowIsInclusiveAtBothTwelveHourBoundaries() {
        assertEquals(
            MobileFlightWindowPhase.Within,
            evaluateMobileFlightWindow(sta.toString(), sta.minusSeconds(12 * 60 * 60)).phase,
        )
        assertEquals(
            MobileFlightWindowPhase.Within,
            evaluateMobileFlightWindow(sta.toString(), sta.plusSeconds(12 * 60 * 60)).phase,
        )
    }

    @Test
    fun windowRejectsTimesImmediatelyOutsideBothBoundaries() {
        assertEquals(
            MobileFlightWindowPhase.Before,
            evaluateMobileFlightWindow(sta.toString(), sta.minusSeconds(12 * 60 * 60 + 1)).phase,
        )
        assertEquals(
            MobileFlightWindowPhase.After,
            evaluateMobileFlightWindow(sta.toString(), sta.plusSeconds(12 * 60 * 60 + 1)).phase,
        )
    }

    @Test
    fun invalidScheduleFailsClosed() {
        val result = evaluateMobileFlightWindow("not-a-timestamp", sta)

        assertEquals(MobileFlightWindowPhase.Unknown, result.phase)
        assertFalse(result.isWithinWindow)
    }

    @Test
    fun realtimeCacheRequiresServerAndLocalWindowAgreement() {
        val inside = flight(sta.toString(), serverWithinWindow = true)
        val serverRejected = flight(sta.toString(), serverWithinWindow = false)

        assertTrue(inside.shouldEnterMobileFlightCache(sta))
        assertFalse(serverRejected.shouldEnterMobileFlightCache(sta))
        assertFalse(inside.shouldEnterMobileFlightCache(sta.minusSeconds(13 * 60 * 60)))
        assertTrue(inside.areMobileActionsAvailable(sta))
        assertFalse(serverRejected.areMobileActionsAvailable(sta))
        assertFalse(inside.areMobileActionsAvailable(sta.plusSeconds(13 * 60 * 60)))
    }

    @Test
    fun cachedNotificationFallbackIsAlwaysInformationOnly() {
        val cached = flight(sta.toString(), serverWithinWindow = true)
        val fallback = cached.asInformationOnlyMobileDetail()

        assertFalse(fallback.isWithinMobileWindow)
        assertFalse(fallback.areMobileActionsAvailable(sta))
    }

    private fun flight(staIso: String, serverWithinWindow: Boolean) = MobileFlightDto(
        id = "flight-1",
        flightNumber = "SV100",
        originalFlightNumber = "SV100",
        customerId = "customer-1",
        customerName = "Customer",
        stationId = "station-1",
        stationIata = "DMM",
        operationTypeId = "operation-1",
        operationTypeName = "Scheduled",
        scheduledArrivalUtc = staIso,
        scheduledDepartureUtc = sta.plusSeconds(2 * 60 * 60).toString(),
        status = "Scheduled",
        rowVersion = "revision",
        isWithinMobileWindow = serverWithinWindow,
    )
}
