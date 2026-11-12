package com.nags.operations.ui.flights

import com.nags.operations.data.FlightDetail
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class FlightFormSummaryTest {
    private val flight = FlightDetail("flight", "TEST", "customer", customerName = "Airline", stationId = "station",
        stationIata = "JED", stationName = "Jeddah", operationTypeId = "operation", operationTypeName = "Transit",
        aircraftTypeModel = "A320", scheduledArrivalUtc = "2026-11-12T09:00:00Z",
        scheduledDepartureUtc = "2026-11-12T12:00:00Z", isPerLanding = false,
        createdAtUtc = "2026-11-12T09:00:00Z", rowVersion = "version", stationTimeZoneId = "Asia/Riyadh")

    @Test fun summaryLinksFollowStartedEntriesInFormOrderAndExcludePlannedDrafts() {
        val drafts = FlightWorkDrafts(
            services = listOf(FlightServiceDraft(id = "planned"),
                FlightServiceDraft(id = "service", period = FlightWorkPeriod(start = "2026-11-12T12:00"))),
            tasks = listOf(FlightTaskDraft(id = "task", period = FlightWorkPeriod(start = "2026-11-12T12:00", end = "2026-11-12T12:30")),
                FlightTaskDraft(id = "unstarted")),
        )
        val targets = drafts.navigationTargets()
        assertEquals(listOf(FlightFormWorkTarget("service:service", false), FlightFormWorkTarget("task:task", true)), targets)
        val summary = flight.formCardSummary(null, targets)
        assertEquals(1, summary.activeWorkCount)
        assertEquals(1, summary.completedWorkCount)
        // An unsaved work entry does not claim a saved arrival or departure.
        assertEquals(false, summary.hasArrivalSubmission)
        assertEquals(false, summary.hasDepartureSubmission)
    }

    @Test fun recordingCardRetainsPublishedLifecycleAndReturnToRampMetadata() {
        val list = flight.formCardSummary(null, emptyList()).copy(hasArrivalSubmission = true,
            hasDepartureSubmission = true, hasOpenReturnToRamp = true, latestReturnToRampSequence = 2,
            activeWorkCount = 5, completedWorkCount = 6)
        val summary = flight.formCardSummary(list, listOf(FlightFormWorkTarget("task:current", false)))
        assertEquals(FlightCardReportStatus.ReturnedToRamp, summary.cardReportStatus())
        assertEquals(2, summary.latestReturnToRampSequence)
        assertEquals(1, summary.activeWorkCount)
        assertEquals(0, summary.completedWorkCount)
        assertTrue(summary.hasDepartureSubmission == true)
        assertEquals("version", flight.rowVersion)
    }

    @Test fun olderListMetadataKeepsItsUnknownReportFlags() {
        val legacy = flight.formCardSummary(null, emptyList()).copy(hasArrivalSubmission = null,
            hasDepartureSubmission = null, hasCancellationSubmission = null)
        val summary = flight.formCardSummary(legacy, emptyList())
        assertEquals(null, summary.hasArrivalSubmission)
        assertEquals(null, summary.hasDepartureSubmission)
        assertEquals(null, summary.hasCancellationSubmission)
        assertEquals(FlightCardReportStatus.Scheduled, summary.cardReportStatus())
    }
}
