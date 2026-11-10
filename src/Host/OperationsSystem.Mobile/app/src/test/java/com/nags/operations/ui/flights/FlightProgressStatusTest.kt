package com.nags.operations.ui.flights

import com.nags.operations.data.AssignedEmployee
import com.nags.operations.data.FlightArrivalSubmission
import com.nags.operations.data.FlightDepartureSubmission
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.db.entities.FlightArrivalPendingEntity
import com.nags.operations.data.db.entities.FlightDeparturePendingEntity
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Test

class FlightProgressStatusTest {
    private val staff = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
    private val employee = AssignedEmployee(staff, "Employee", "E1")
    private val arrival = FlightArrivalSubmission("arrival", employee, "2026-11-10T10:00:00Z", "aircraft", "A320", "HZ-TEST",
        receivedByEmployee = employee, isAccepted = false, createdAtUtc = "2026-11-10T10:00:00Z")
    private val departure = FlightDepartureSubmission("departure", employee, "2026-11-10T11:00:00Z", employee,
        isAccepted = false, createdAtUtc = "2026-11-10T11:00:00Z")
    private val flight = FlightDetail("flight", "TEST", "customer", customerName = "Airline", stationId = "station",
        stationIata = "JED", stationName = "Jeddah", operationTypeId = "operation", operationTypeName = "Transit",
        scheduledArrivalUtc = "2026-11-10T09:00:00Z", scheduledDepartureUtc = "2026-11-10T12:00:00Z",
        isPerLanding = false, createdAtUtc = "2026-11-10T09:00:00Z", rowVersion = "reviewed-version",
        currentStaffMemberId = staff, stationTimeZoneId = "Asia/Riyadh")
    private val pendingArrival = FlightArrivalPendingEntity("owner", flight.id, flight.flightNumber, "arrival-change", "{}",
        flight.rowVersion, "2026-11-10T11:00:00Z")
    private val pendingDeparture = FlightDeparturePendingEntity("owner", flight.id, flight.flightNumber, staff,
        "departure-change", "{}", flight.rowVersion, "[]", "2026-11-10T11:00:00Z")

    @Test fun planningDesignationDoesNotReplaceTheDraftProgressOfAnUnsubmittedFlight() {
        assertEquals(FlightProgressStatus.Draft, flight.progressStatus())
        assertEquals(FlightProgressStatus.Draft, flight.copy(isPerLanding = true).progressStatus())
        assertEquals(FlightProgressStatus.Draft, flight.copy(operationTypeName = "AdHoc").progressStatus())
    }

    @Test fun serverSubmissionsShowTheirStageWithoutClaimingApproval() {
        val arrived = flight.copy(arrivalSubmissions = listOf(arrival))
        assertEquals(FlightProgressStatus.ArrivalSubmitted, arrived.progressStatus())
        assertEquals(FlightProgressStatus.DepartureSubmitted,
            arrived.copy(departureSubmissions = listOf(departure)).progressStatus())
        assertFalse(arrival.isAccepted)
        assertFalse(departure.isAccepted)
        assertEquals("reviewed-version", arrived.rowVersion)
    }

    @Test fun queuedArrivalCorrectionsOutrankThePriorArrivalAndExposeReviewFailures() {
        val arrived = flight.copy(arrivalSubmissions = listOf(arrival))
        assertEquals(FlightProgressStatus.ArrivalPendingSync, arrived.progressStatus(pendingArrival = pendingArrival))
        for (state in listOf("conflict", "failed", "unknown")) {
            assertEquals(FlightProgressStatus.ArrivalNeedsReview,
                arrived.progressStatus(pendingArrival = pendingArrival.copy(status = state)))
        }
    }

    @Test fun queuedDepartureCorrectionsOutrankThePriorDepartureAndAnyArrivalIntent() {
        val departed = flight.copy(arrivalSubmissions = listOf(arrival), departureSubmissions = listOf(departure))
        assertEquals(FlightProgressStatus.DeparturePendingSync, departed.progressStatus(pendingArrival, pendingDeparture))
        for (state in listOf("conflict", "failed", "unknown")) {
            assertEquals(FlightProgressStatus.DepartureNeedsReview,
                departed.progressStatus(pendingArrival, pendingDeparture.copy(status = state)))
        }
    }

    @Test fun aSubmittedDepartureIsNotDowngradedByAnArrivalCorrection() {
        val departed = flight.copy(departureSubmissions = listOf(departure))
        for (state in listOf("pending", "conflict", "failed")) {
            assertEquals(FlightProgressStatus.DepartureSubmitted,
                departed.progressStatus(pendingArrival = pendingArrival.copy(status = state)))
        }
    }

    @Test fun otherFlightsAndMismatchedStaffCannotSetTheSelectedFlightsLocalProgress() {
        assertEquals(FlightProgressStatus.Draft,
            flight.progressStatus(pendingArrival.copy(flightId = "different"), pendingDeparture.copy(flightId = "different")))
        assertEquals(FlightProgressStatus.Draft,
            flight.progressStatus(pendingDeparture = pendingDeparture.copy(staffMemberId = "different")))
        assertEquals(FlightProgressStatus.ArrivalPendingSync,
            flight.progressStatus(pendingArrival, pendingDeparture.copy(staffMemberId = "different")))
        assertEquals(FlightProgressStatus.Draft,
            flight.copy(currentStaffMemberId = null).progressStatus(pendingArrival, pendingDeparture))
        assertEquals(FlightProgressStatus.DeparturePendingSync,
            flight.progressStatus(pendingDeparture = pendingDeparture.copy(staffMemberId = staff.uppercase())))
    }

    @Test fun discardReturnsToTheServerStageAndAcknowledgmentChangesPendingToSubmitted() {
        val arrived = flight.copy(arrivalSubmissions = listOf(arrival))
        assertEquals(FlightProgressStatus.DeparturePendingSync, arrived.progressStatus(pendingDeparture = pendingDeparture))
        assertEquals(FlightProgressStatus.ArrivalSubmitted, arrived.progressStatus())
        assertEquals(FlightProgressStatus.DepartureSubmitted,
            arrived.copy(departureSubmissions = listOf(departure)).progressStatus())
    }
}
