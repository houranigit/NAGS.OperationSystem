package com.nags.operations.ui.flights

import com.nags.operations.data.MobileFlightDto
import org.junit.Assert.assertEquals
import org.junit.Test

class FlightNotificationRoutingTest {
    @Test
    fun adHocFlightNotificationRoutesOnlyToAdHocTab() {
        assertEquals(
            FlightNotificationTab.AdHoc,
            notificationFlightTab(flight(isAdHoc = true)),
        )
    }

    @Test
    fun ordinaryAndPerLandingFlightNotificationsKeepMyFlightsRoute() {
        assertEquals(
            FlightNotificationTab.MyFlights,
            notificationFlightTab(flight()),
        )
        assertEquals(
            FlightNotificationTab.MyFlights,
            notificationFlightTab(flight(isPerLanding = true)),
        )
    }

    @Test
    fun scheduleLevelNotificationKeepsMyFlightsRoute() {
        assertEquals(FlightNotificationTab.MyFlights, notificationFlightTab(null))
    }

    private fun flight(
        isPerLanding: Boolean = false,
        isAdHoc: Boolean = false,
    ) = MobileFlightDto(
        id = "flight-1",
        flightNumber = "SV100",
        originalFlightNumber = "SV100",
        customerId = "customer-1",
        customerName = "Customer",
        stationId = "station-1",
        stationIata = "ORD",
        operationTypeId = "operation-1",
        operationTypeName = if (isAdHoc) "Ad Hoc" else "Scheduled",
        scheduledArrivalUtc = "2026-07-19T18:00:00Z",
        scheduledDepartureUtc = "2026-07-19T19:00:00Z",
        status = "Scheduled",
        isPerLanding = isPerLanding,
        isAdHoc = isAdHoc,
        rowVersion = "revision",
    )
}
