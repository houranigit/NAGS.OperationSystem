package com.nags.operations.data

import java.time.Instant
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class FlightVisibilityTest {
    private val now = Instant.parse("2026-11-16T12:00:00Z")
    private val employee = AssignedEmployee("staff", "Employee", "E1")
    private val old = FlightDetail("flight", "SV100", "customer", customerName = "Airline", stationId = "station",
        stationIata = "JED", stationName = "Jeddah", operationTypeId = "operation", operationTypeName = "Transit",
        scheduledArrivalUtc = "2026-11-01T09:00:00Z", scheduledDepartureUtc = "2026-11-01T11:00:00Z",
        isPerLanding = false, createdAtUtc = "2026-11-01T00:00:00Z", rowVersion = "version", stationTimeZoneId = "Asia/Riyadh")
    private val arrival = FlightArrivalSubmission("arrival", employee, old.scheduledArrivalUtc, "aircraft", "A320", "TAIL",
        receivedByEmployee = employee, isAccepted = true, createdAtUtc = old.createdAtUtc)

    @Test fun overdueAndOnGroundFlightsRemainVisibleRegardlessOfOldSchedule() {
        assertVisible(old, true)
        assertVisible(old.copy(arrivalSubmissions = listOf(arrival)), true)
        // A conflicting cancellation cannot silently hide an aircraft still on the ground.
        val canceled = FlightCancellationSubmission("cancel", employee, old.scheduledArrivalUtc, isAccepted = false, createdAtUtc = old.createdAtUtc)
        assertVisible(old.copy(arrivalSubmissions = listOf(arrival), cancellationSubmissions = listOf(canceled), hasCancellationSubmission = true), true)
    }

    @Test fun upcomingPendingFlightsUseOnlyStaLookaheadAndIncludeItsExactBoundary() {
        assertVisible(old.copy(scheduledArrivalUtc = now.plusSeconds(43200).toString(), scheduledDepartureUtc = now.plusSeconds(86400).toString()), true)
        assertVisible(old.copy(scheduledArrivalUtc = now.plusSeconds(43200).plusNanos(1).toString(), scheduledDepartureUtc = now.plusSeconds(86400).toString()), false)
        val actualArrival = old.copy(scheduledArrivalUtc = now.plusSeconds(90000).toString(),
            scheduledDepartureUtc = now.plusSeconds(93600).toString(), arrivalSubmissions = listOf(arrival))
        assertVisible(actualArrival, true)
        assertTrue(isWithinFlightPlanningWindow(old.scheduledArrivalUtc, old.scheduledDepartureUtc, now))
        assertFalse(isWithinFlightPlanningWindow(actualArrival.scheduledArrivalUtc, actualArrival.scheduledDepartureUtc, now))
    }

    @Test fun departedCanceledAndClosedRtrFlightsExpireTwelveHoursAfterLatestActual() {
        val boundary = now.minusSeconds(43200).toString()
        val departed = old.copy(departureSubmissions = listOf(departure(boundary)))
        assertVisible(departed, true)
        assertVisible(departed, false, now.plusNanos(1))
        val cancellation = FlightCancellationSubmission("cancel", employee, boundary, isAccepted = true, createdAtUtc = old.createdAtUtc)
        assertVisible(old.copy(hasCancellationSubmission = true, cancellationSubmissions = listOf(cancellation)), true)
        assertVisible(old.copy(hasCancellationSubmission = true, cancellationSubmissions = listOf(cancellation)), false, now.plusNanos(1))
        val closed = departed.copy(returnToRamps = listOf(rtr(now.minusSeconds(1800).toString())))
        assertVisible(closed, true, now.plusSeconds(40000))
        assertVisible(closed, false, now.plusSeconds(43200))
        // Separate departures use the latest actual, independently of collection order.
        assertVisible(departed.copy(departureSubmissions = listOf(departure(boundary), departure(now.minusSeconds(10).toString()))), true, now.plusSeconds(60))
    }

    @Test fun anOpenRtrRetainsAFlightAfterItsOriginalDepartureAndScheduleExpired() {
        val flight = old.copy(departureSubmissions = listOf(departure(old.scheduledDepartureUtc)), returnToRamps = listOf(rtr(null)))
        assertVisible(flight, true)
        assertVisible(flight, true, now.plusSeconds(30 * 86400))
    }

    @Test fun legacyUnknownAndIncompleteEvidenceCannotGrantUnlimitedVisibility() {
        val current = card(old)
        assertFalse(current.copy(visibilityPolicyVersion = null).isInFlightWindow(now))
        assertFalse(current.copy(visibilityPolicyVersion = 99).isInFlightWindow(now))
        assertFalse(current.copy(hasArrivalSubmission = null).isInFlightWindow(now))
        assertFalse(current.copy(hasDepartureSubmission = true, latestDepartureAtUtc = null).isInFlightWindow(now))
        assertFalse(current.copy(hasDepartureSubmission = true, latestDepartureAtUtc = "invalid").isInFlightWindow(now))
        assertFalse(current.copy(scheduledArrivalUtc = "invalid").isInFlightWindow(now))
        assertFalse(current.copy(scheduledDepartureUtc = "2026-10-01T00:00:00Z").isInFlightWindow(now))
    }

    @Test fun policyEvidenceRoundTripsWithoutLosingTerminalOrRtrTimes() {
        val detail = old.copy(departureSubmissions = listOf(departure(old.scheduledDepartureUtc)),
            returnToRamps = listOf(rtr(now.minusSeconds(10).toString())))
        val item = card(detail)
        val cached = Json.decodeFromString<FlightListItem>(Json.encodeToString(item))
        assertEquals(1, cached.visibilityPolicyVersion)
        assertEquals(old.scheduledDepartureUtc, cached.latestDepartureAtUtc)
        assertEquals(now.minusSeconds(10).toString(), cached.latestReturnToRampDepartureAtUtc)
        assertTrue(cached.isInFlightWindow(now))
    }

    private fun assertVisible(detail: FlightDetail, expected: Boolean, at: Instant = now) {
        assertEquals(expected, detail.isInFlightWindow(at))
        assertEquals(expected, card(detail).isInFlightWindow(at))
    }
    private fun card(detail: FlightDetail) = FlightListItem(detail.id, detail.flightNumber, customerName = detail.customerName,
        stationIataCode = detail.stationIata, operationTypeName = detail.operationTypeName,
        scheduledArrivalUtc = detail.scheduledArrivalUtc, scheduledDepartureUtc = detail.scheduledDepartureUtc,
        isPerLanding = detail.isPerLanding, stationTimeZoneId = detail.stationTimeZoneId).withReturnToRampReports(detail)
    private fun departure(at: String) = FlightDepartureSubmission(at, employee, at, employee, isAccepted = true, createdAtUtc = old.createdAtUtc)
    private fun rtr(departed: String?) = FlightReturnToRampRecord("rtr", 1, old.scheduledDepartureUtc, "staff", "Employee",
        departedAtUtc = departed, createdByUserId = "user", createdAtUtc = old.createdAtUtc)
}
