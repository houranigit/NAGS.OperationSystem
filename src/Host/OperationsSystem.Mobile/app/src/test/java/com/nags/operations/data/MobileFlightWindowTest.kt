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

        assertTrue(inside.shouldEnterMobileFlightCache(MobileFlightCache.MyFlights, sta))
        assertFalse(serverRejected.shouldEnterMobileFlightCache(MobileFlightCache.MyFlights, sta))
        assertFalse(
            inside.shouldEnterMobileFlightCache(
                MobileFlightCache.MyFlights,
                sta.minusSeconds(13 * 60 * 60),
            ),
        )
        assertTrue(inside.areMobileActionsAvailable(sta))
        assertFalse(serverRejected.areMobileActionsAvailable(sta))
        assertFalse(inside.areMobileActionsAvailable(sta.plusSeconds(13 * 60 * 60)))
    }

    @Test
    fun cacheMembershipKeepsAdHocFlightsOutOfEveryOtherList() {
        val adHoc = flight(sta.toString(), isAdHoc = true)

        assertFalse(adHoc.shouldEnterMobileFlightCache(MobileFlightCache.MyFlights, sta))
        assertFalse(adHoc.shouldEnterMobileFlightCache(MobileFlightCache.PerLandingFlights, sta))
        assertTrue(adHoc.shouldEnterMobileFlightCache(MobileFlightCache.AdHocFlights, sta))
    }

    @Test
    fun cacheMembershipAcceptsOnlyItsOwnListShape() {
        val assigned = flight(sta.toString())
        val perLanding = flight(sta.toString(), isPerLanding = true)

        assertTrue(assigned.shouldEnterMobileFlightCache(MobileFlightCache.MyFlights, sta))
        assertFalse(assigned.shouldEnterMobileFlightCache(MobileFlightCache.PerLandingFlights, sta))
        assertFalse(assigned.shouldEnterMobileFlightCache(MobileFlightCache.AdHocFlights, sta))

        assertFalse(perLanding.shouldEnterMobileFlightCache(MobileFlightCache.MyFlights, sta))
        assertTrue(perLanding.shouldEnterMobileFlightCache(MobileFlightCache.PerLandingFlights, sta))
        assertFalse(perLanding.shouldEnterMobileFlightCache(MobileFlightCache.AdHocFlights, sta))
    }

    @Test
    fun cacheMembershipRetainsScheduledInProgressAndCompletedOnly() {
        listOf("Scheduled", "InProgress", "Completed").forEach { status ->
            assertTrue(
                flight(sta.toString(), status = status)
                    .shouldEnterMobileFlightCache(MobileFlightCache.MyFlights, sta),
            )
        }

        listOf("Canceled", "Merged", "Unknown").forEach { status ->
            assertFalse(
                flight(sta.toString(), status = status)
                    .shouldEnterMobileFlightCache(MobileFlightCache.MyFlights, sta),
            )
        }
    }

    @Test
    fun cachedNotificationFallbackIsAlwaysInformationOnly() {
        val cached = flight(sta.toString(), serverWithinWindow = true)
        val fallback = cached.asInformationOnlyMobileDetail()

        assertFalse(fallback.isWithinMobileWindow)
        assertFalse(fallback.areMobileActionsAvailable(sta))
    }

    private fun flight(
        staIso: String,
        serverWithinWindow: Boolean = true,
        status: String = "Scheduled",
        isPerLanding: Boolean = false,
        isAdHoc: Boolean = false,
    ) = MobileFlightDto(
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
        status = status,
        isPerLanding = isPerLanding,
        isAdHoc = isAdHoc,
        rowVersion = "revision",
        isWithinMobileWindow = serverWithinWindow,
    )
}
