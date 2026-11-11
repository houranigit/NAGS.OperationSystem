package com.nags.operations.ui.flights

import com.nags.operations.data.FlightListItem
import com.nags.operations.data.db.entities.FlightCancellationPendingEntity
import org.junit.Assert.*
import org.junit.Test

class FlightCardReportStatusTest {
    private val flight = FlightListItem("flight", "SV100", customerName = "Airline", stationIataCode = "JED",
        operationTypeName = "Transit", scheduledArrivalUtc = "2026-11-10T10:00:00Z",
        scheduledDepartureUtc = "2026-11-10T12:00:00Z", isPerLanding = false, stationTimeZoneId = "Asia/Riyadh")

    @Test fun everyReportCombinationHasOneExplicitPrecedenceIndependentOfDesignation() {
        val expected = listOf(FlightCardReportStatus.Scheduled, FlightCardReportStatus.Arrived,
            FlightCardReportStatus.Departed, FlightCardReportStatus.Departed, FlightCardReportStatus.Canceled,
            FlightCardReportStatus.Conflict, FlightCardReportStatus.Conflict, FlightCardReportStatus.Conflict)
        for (mask in 0..7) {
            val row = flight.copy(hasArrivalSubmission = (mask and 1) != 0, hasDepartureSubmission = (mask and 2) != 0,
                hasCancellationSubmission = (mask and 4) != 0)
            assertEquals("report mask $mask", expected[mask], row.cardReportStatus())
            assertEquals(expected[mask], row.copy(isPerLanding = true).cardReportStatus())
        }
    }

    @Test fun durableLocalInputUpdatesTheCardWithoutChangingItsServerReportFlags() {
        assertEquals(FlightCardReportStatus.Arrived, flight.cardReportStatus(localArrival = true))
        assertEquals(FlightCardReportStatus.Departed, flight.cardReportStatus(localArrival = true, localDeparture = true))
        assertEquals(FlightCardReportStatus.Canceled, flight.cardReportStatus(localCancellation = true))
        assertEquals(FlightCardReportStatus.Conflict,
            flight.copy(hasCancellationSubmission = true).cardReportStatus(localDeparture = true))
        assertEquals(FlightCardReportStatus.Conflict,
            flight.copy(hasArrivalSubmission = true).cardReportStatus(localCancellation = true))
        assertNull(flight.hasArrivalSubmission)
        assertNull(flight.hasDepartureSubmission)
        assertNull(flight.hasCancellationSubmission)
        assertEquals(FlightCardReportStatus.Scheduled, flight.cardReportStatus())
    }

    @Test fun possiblySubmittedCancellationRemainsVisibleUntilTheExactRevertIsAcknowledged() {
        val local = FlightCancellationPendingEntity("owner", flight.id, flight.flightNumber, "staff", "change", "save", "{}", null,
            "version", "2026-11-10T10:00:00Z")
        assertTrue(local.keepsCancellationOnCard())
        assertTrue(local.copy(status = "conflict").keepsCancellationOnCard())
        assertTrue(local.copy(operation = "revert", attemptOperation = "save").keepsCancellationOnCard())
        assertTrue(local.copy(operation = "revert", submissionId = "saved", attemptOperation = "revert").keepsCancellationOnCard())
        assertFalse(local.copy(operation = "revert").keepsCancellationOnCard())
        assertEquals(FlightCardReportStatus.Canceled, flight.copy(hasCancellationSubmission = true).cardReportStatus())
    }
}
