package com.nags.operations.ui.flights

import com.nags.operations.data.FlightStatusKind
import com.nags.operations.data.MobileFlightDto
import com.nags.operations.ui.adhoc.AdHocFlightsViewModel
import com.nags.operations.ui.common.label
import com.nags.operations.ui.perlanding.PerLandingFlightsViewModel
import java.time.Instant
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class FlightListPresentationTest {
    private val now = Instant.parse("2026-07-18T18:00:00Z")

    @Test
    fun filterKindsAreExactlyAllScheduledInProgressAndCompleted() {
        assertEquals(
            listOf(
                FlightStatusKind.Scheduled,
                FlightStatusKind.InProgress,
                FlightStatusKind.Completed,
            ),
            MobileFlightListStatusKinds,
        )
        assertEquals(
            listOf("All", "Scheduled", "In Progress", "Completed"),
            listOf("All") + MobileFlightListStatusKinds.map { it.label() },
        )
    }

    @Test
    fun myAndPerLandingDefaultToScheduledWhileAdHocDefaultsToAll() {
        assertEquals(FlightStatusKind.Scheduled, MyFlightsViewModel.UiState().statusFilter)
        assertEquals(FlightStatusKind.Scheduled, PerLandingFlightsViewModel.UiState().statusFilter)
        assertEquals(null, AdHocFlightsViewModel.UiState().statusFilter)
    }

    @Test
    fun allIncludesOnlyTheThreePresentedStatusesInsideTheMobileWindow() {
        val rows = listOf(
            flight("scheduled", FlightStatusKind.Scheduled),
            flight("in-progress", FlightStatusKind.InProgress),
            flight("completed", FlightStatusKind.Completed),
            flight("canceled", FlightStatusKind.Canceled),
            flight("merged", FlightStatusKind.Merged),
            flight(
                id = "outside-window",
                status = FlightStatusKind.Completed,
                sta = now.plusSeconds(13 * 60 * 60),
            ),
        )

        assertEquals(
            listOf("scheduled", "in-progress", "completed"),
            filterMobileFlightList(rows, statusFilter = null, search = "", now = now)
                .map { it.id },
        )
    }

    @Test
    fun eachStatusFilterAndSearchAreAppliedTogether() {
        val rows = MobileFlightListStatusKinds.flatMap { status ->
            listOf(
                flight("${status.wire}-100", status, flightNumber = "SV100"),
                flight("${status.wire}-200", status, flightNumber = "SV200"),
            )
        }

        MobileFlightListStatusKinds.forEach { status ->
            assertEquals(
                listOf("${status.wire}-200"),
                filterMobileFlightList(rows, status, search = "200", now = now).map { it.id },
            )
        }
    }

    @Test
    fun listMembershipKeepsAdHocRowsExclusiveToTheAdHocTab() {
        val regular = flight("regular", FlightStatusKind.Scheduled)
        val perLanding = flight(
            "per-landing",
            FlightStatusKind.Scheduled,
            isPerLanding = true,
        )
        val adHoc = flight("ad-hoc", FlightStatusKind.Scheduled, isAdHoc = true)

        assertTrue(regular.belongsToMyFlightsList())
        assertFalse(regular.belongsToPerLandingFlightsList())
        assertFalse(regular.belongsToAdHocFlightsList())

        assertFalse(perLanding.belongsToMyFlightsList())
        assertTrue(perLanding.belongsToPerLandingFlightsList())
        assertFalse(perLanding.belongsToAdHocFlightsList())

        assertFalse(adHoc.belongsToMyFlightsList())
        assertFalse(adHoc.belongsToPerLandingFlightsList())
        assertTrue(adHoc.belongsToAdHocFlightsList())
    }

    private fun flight(
        id: String,
        status: FlightStatusKind,
        sta: Instant = now,
        flightNumber: String = id,
        isPerLanding: Boolean = false,
        isAdHoc: Boolean = false,
    ) = MobileFlightDto(
        id = id,
        flightNumber = flightNumber,
        originalFlightNumber = flightNumber,
        customerId = "customer-1",
        customerName = "Customer",
        stationId = "station-1",
        stationIata = "ORD",
        operationTypeId = "operation-1",
        operationTypeName = if (isAdHoc) "Ad Hoc" else "Scheduled",
        scheduledArrivalUtc = sta.toString(),
        scheduledDepartureUtc = sta.plusSeconds(60 * 60).toString(),
        status = status.wire,
        isPerLanding = isPerLanding,
        isAdHoc = isAdHoc,
        rowVersion = "revision",
    )
}
