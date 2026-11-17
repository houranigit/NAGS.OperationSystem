package com.nags.operations.ui.flights

import java.time.Instant
import org.junit.Assert.assertEquals
import org.junit.Test

class FlightSharePresentationTest {
    private val now = Instant.parse("2026-11-16T12:00:00Z")

    @Test fun relativeRecordingTimeChangesAtMinuteHourDayAndWeekBoundaries() {
        val cases = listOf(
            -60L to SharePostAge(ShareAgeKind.JustNow),
            59L to SharePostAge(ShareAgeKind.JustNow),
            60L to SharePostAge(ShareAgeKind.Minutes, 1),
            3599L to SharePostAge(ShareAgeKind.Minutes, 59),
            3600L to SharePostAge(ShareAgeKind.Hours, 1),
            86399L to SharePostAge(ShareAgeKind.Hours, 23),
            86400L to SharePostAge(ShareAgeKind.Days, 1),
            604799L to SharePostAge(ShareAgeKind.Days, 6),
            604800L to SharePostAge(ShareAgeKind.Date),
        )
        cases.forEach { (age, expected) -> assertEquals(expected, sharePostAge(now.minusSeconds(age).toString(), now)) }
    }

    @Test fun timestampOffsetsRepresentTheSameAgeAndMalformedTimesUseDateFallback() {
        assertEquals(SharePostAge(ShareAgeKind.Hours, 1), sharePostAge("2026-11-16T14:00:00+03:00", now))
        assertEquals(SharePostAge(ShareAgeKind.Date), sharePostAge("invalid", now))
    }
}
