package com.nags.operations.ui.flights

import com.nags.operations.data.FlightStatusKind
import com.nags.operations.data.MobileFlightDto
import org.junit.Assert.assertEquals
import org.junit.Test

class FlightSummaryActionsDecisionTest {
    @Test
    fun completedFlightUsesReturnToRampOnlyDecision() {
        assertEquals(
            FlightSummaryActionsDecision.CompletedReturnToRamp,
            deriveFlightSummaryActions(flight(FlightStatusKind.Completed)),
        )
    }

    @Test
    fun canceledAndMergedFlightsRemainReadOnly() {
        listOf(FlightStatusKind.Canceled, FlightStatusKind.Merged).forEach { status ->
            assertEquals(
                FlightSummaryActionsDecision.ReadOnly,
                deriveFlightSummaryActions(flight(status)),
            )
        }
    }

    private fun flight(status: FlightStatusKind) = MobileFlightDto(
        id = "flight-1",
        flightNumber = "SV100",
        originalFlightNumber = "SV100",
        customerId = "customer-1",
        customerName = "Customer",
        stationId = "station-1",
        stationIata = "ORD",
        operationTypeId = "operation-1",
        operationTypeName = "Scheduled",
        scheduledArrivalUtc = "2026-07-18T18:00:00Z",
        scheduledDepartureUtc = "2026-07-18T19:00:00Z",
        status = status.wire,
        rowVersion = "revision",
    )
}
