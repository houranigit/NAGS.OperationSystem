package com.nags.operations.ui.flights

import com.nags.operations.data.FlightListItem
import com.nags.operations.data.db.entities.FlightCancellationPendingEntity
import com.nags.operations.data.db.entities.FlightArrivalPendingEntity
import com.nags.operations.data.db.entities.FlightDeparturePendingEntity
import java.time.Instant
import org.junit.Assert.*
import org.junit.Test

class FlightCardReportStatusTest {
    private val now = Instant.parse("2026-11-10T08:00:00Z")
    private val flight = FlightListItem("flight", "SV100", customerName = "Airline", stationIataCode = "JED",
        operationTypeName = "Transit", scheduledArrivalUtc = "2026-11-10T10:00:00Z",
        scheduledDepartureUtc = "2026-11-10T12:00:00Z", isPerLanding = false, stationTimeZoneId = "Asia/Riyadh")

    private val reported = flight.copy(hasArrivalSubmission = false, hasDepartureSubmission = false, hasCancellationSubmission = false)

    @Test fun everyReportCombinationHasOneExplicitPrecedenceAcrossAllThreeCategories() {
        val expected = listOf(FlightCardReportStatus.Scheduled, FlightCardReportStatus.OnGround,
            FlightCardReportStatus.Departed, FlightCardReportStatus.Departed, FlightCardReportStatus.Canceled,
            FlightCardReportStatus.Conflict, FlightCardReportStatus.Conflict, FlightCardReportStatus.Conflict)
        for (mask in 0..7) {
            val row = flight.copy(hasArrivalSubmission = (mask and 1) != 0, hasDepartureSubmission = (mask and 2) != 0,
                hasCancellationSubmission = (mask and 4) != 0)
            for (category in listOf(row.copy(isMyFlight = true), row.copy(isPerLanding = true), row.copy(isMyAdHocFlight = true)))
                assertEquals("report mask $mask", expected[mask], category.cardReportStatus(now = now))
        }
    }

    @Test fun durableLocalInputUpdatesTheCardWithoutChangingItsServerReportFlags() {
        assertEquals(FlightCardReportStatus.OnGround, flight.cardReportStatus(localArrival = true, now = now))
        assertEquals(FlightCardReportStatus.Departed, flight.cardReportStatus(localArrival = true, localDeparture = true, now = now))
        assertEquals(FlightCardReportStatus.Canceled, flight.cardReportStatus(localCancellation = true, now = now))
        assertEquals(FlightCardReportStatus.Conflict,
            flight.copy(hasCancellationSubmission = true).cardReportStatus(localDeparture = true, now = now))
        assertEquals(FlightCardReportStatus.Conflict,
            flight.copy(hasArrivalSubmission = true).cardReportStatus(localCancellation = true, now = now))
        assertNull(flight.hasArrivalSubmission)
        assertNull(flight.hasDepartureSubmission)
        assertNull(flight.hasCancellationSubmission)
        assertEquals(FlightCardReportStatus.Scheduled, flight.cardReportStatus(now = now))
    }

    @Test fun possiblySubmittedCancellationRemainsVisibleUntilTheExactRevertIsAcknowledged() {
        val local = FlightCancellationPendingEntity("owner", flight.id, flight.flightNumber, "staff", "change", "save", "{}", null,
            "version", "2026-11-10T10:00:00Z")
        assertTrue(local.keepsCancellationOnCard())
        assertTrue(local.copy(status = "conflict").keepsCancellationOnCard())
        assertTrue(local.copy(operation = "revert", attemptOperation = "save").keepsCancellationOnCard())
        assertTrue(local.copy(operation = "revert", submissionId = "saved", attemptOperation = "revert").keepsCancellationOnCard())
        assertFalse(local.copy(operation = "revert").keepsCancellationOnCard())
        assertEquals(FlightCardReportStatus.Canceled, flight.copy(hasCancellationSubmission = true).cardReportStatus(now = now))
    }

    @Test fun timingCrossesTheThirtyMinuteAndActualArrivalBoundaries() {
        val sta = Instant.parse(reported.scheduledArrivalUtc)
        assertEquals(FlightCardReportStatus.Scheduled, reported.cardReportStatus(now = sta.minusSeconds(1801)))
        assertEquals(FlightCardReportStatus.DueSoon, reported.cardReportStatus(now = sta.minusSeconds(1800)))
        assertEquals(FlightCardReportStatus.DueSoon, reported.cardReportStatus(now = sta.minusSeconds(1)))
        assertEquals(FlightCardReportStatus.ArrivalNotRecorded, reported.cardReportStatus(now = sta))
        assertEquals(FlightCardReportStatus.ArrivalNotRecorded, reported.cardReportStatus(now = sta.plusSeconds(1200)))
        assertEquals(FlightCardReportStatus.OnGround, reported.cardReportStatus(localArrival = true, now = sta))
        assertEquals(FlightCardReportStatus.Canceled, reported.cardReportStatus(localCancellation = true, now = sta))
    }

    @Test fun timingUsesInstantsRatherThanTheDeviceOrStationWallClock() {
        val row = reported.copy(scheduledArrivalUtc = "2026-11-10T13:00:00+03:00")
        assertEquals(FlightCardReportStatus.DueSoon, row.cardReportStatus(now = Instant.parse("2026-11-10T09:35:00Z")))
        assertEquals(25L, row.cardStaMinutes(Instant.parse("2026-11-10T09:35:00Z")))
    }

    @Test fun unknownLegacyFlagsAndInvalidSchedulesNeverClaimAMissingArrival() {
        val past = Instant.parse("2026-11-10T11:00:00Z")
        assertEquals(FlightCardReportStatus.Scheduled, flight.cardReportStatus(now = past))
        assertEquals(FlightCardReportStatus.ArrivalNotRecorded, reported.cardReportStatus(now = past))
        assertEquals(FlightCardReportStatus.Scheduled, reported.copy(scheduledArrivalUtc = "invalid").cardReportStatus(now = past))
    }

    @Test fun anOpenReturnToRampOverridesTheOriginalDepartureButNotAConflict() {
        val row = reported.copy(hasArrivalSubmission = true, hasDepartureSubmission = true,
            hasOpenReturnToRamp = true, latestReturnToRampSequence = 2)
        assertEquals(FlightCardReportStatus.ReturnedToRamp, row.cardReportStatus(now = now))
        assertEquals(FlightCardReportStatus.ReturnedToRamp, row.cardReportStatus(localDeparture = true, now = now))
        assertEquals(FlightCardReportStatus.Departed, row.copy(hasOpenReturnToRamp = false).cardReportStatus(now = now))
        assertEquals(FlightCardReportStatus.Conflict, row.copy(hasCancellationSubmission = true).cardReportStatus(now = now))
    }

    @Test fun completedWorkDoesNotImplyDepartureOrAnActualArrival() {
        val row = reported.copy(activeWorkCount = 0, completedWorkCount = 3)
        assertEquals(FlightCardReportStatus.Scheduled, row.cardReportStatus(now = now))
        assertEquals(FlightCardReportStatus.OnGround, row.copy(hasArrivalSubmission = true).cardReportStatus(now = now))
    }

    @Test fun minuteLabelsRoundRemainingTimeUpAndElapsedTimeDown() {
        val sta = Instant.parse(reported.scheduledArrivalUtc)
        assertEquals(2L, reported.cardStaMinutes(sta.minusSeconds(61)))
        assertEquals(2L, reported.cardStaMinutes(sta.minusMillis(60_500)))
        assertEquals(1L, reported.cardStaMinutes(sta.minusSeconds(1)))
        assertEquals(0L, reported.cardStaMinutes(sta.plusSeconds(59)))
        assertEquals(0L, reported.cardStaMinutes(sta.plusMillis(59_500)))
        assertEquals(-1L, reported.cardStaMinutes(sta.plusSeconds(60)))
        assertNull(reported.copy(scheduledArrivalUtc = "invalid").cardStaMinutes(now))
    }

    @Test fun durablePendingAndReviewNoticesKeepTheirExistingPrecedence() {
        val arrival = FlightArrivalPendingEntity("owner", flight.id, flight.flightNumber, "change", "{}", "version", now.toString())
        val departure = FlightDeparturePendingEntity("owner", flight.id, flight.flightNumber, "staff", "change", "{}", "version", "{}", now.toString())
        val cancellation = FlightCancellationPendingEntity("owner", flight.id, flight.flightNumber, "staff", "change", "save", "{}", null, "version", now.toString())
        assertEquals(FlightProgressStatus.ArrivalPendingSync, flight.cardPendingProgress(arrival))
        assertEquals(FlightProgressStatus.ArrivalNeedsReview, flight.cardPendingProgress(arrival.copy(status = "conflict")))
        assertEquals(FlightProgressStatus.DeparturePendingSync, flight.cardPendingProgress(arrival, departure))
        assertEquals(FlightProgressStatus.DepartureNeedsReview, flight.cardPendingProgress(arrival, departure.copy(status = "conflict")))
        assertEquals(FlightProgressStatus.CancellationRevertPending,
            flight.cardPendingProgress(arrival, departure, cancellation.copy(operation = "revert")))
        assertEquals(FlightProgressStatus.CancellationNeedsReview,
            flight.cardPendingProgress(arrival, departure, cancellation.copy(status = "conflict")))
        assertNull(flight.cardPendingProgress(arrival.copy(flightId = "other")))
    }
}
