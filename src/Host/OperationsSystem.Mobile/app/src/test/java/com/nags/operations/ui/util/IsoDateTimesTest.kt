package com.nags.operations.ui.util

import java.time.ZoneId
import java.util.Locale
import org.junit.Assert.assertEquals
import org.junit.Test

class IsoDateTimesTest {
    @Test
    fun utc_instant_is_converted_to_the_users_riyadh_wall_clock() {
        val local = parseInUserZone(
            "2026-07-18T15:42:00Z",
            ZoneId.of("Asia/Riyadh"),
        )

        assertEquals(2026, local.year)
        assertEquals(7, local.monthValue)
        assertEquals(18, local.dayOfMonth)
        assertEquals(18, local.hour)
        assertEquals("+03:00", local.offset.id)
    }

    @Test
    fun conversion_uses_the_selected_dates_daylight_saving_offset() {
        val zone = ZoneId.of("America/Chicago")

        assertEquals("-06:00", parseInUserZone("2026-01-15T12:00:00Z", zone).offset.id)
        assertEquals("-05:00", parseInUserZone("2026-07-15T12:00:00Z", zone).offset.id)
    }

    @Test
    fun card_uses_local_date_ranges_and_24_hour_times() {
        val previousLocale = Locale.getDefault()
        try {
            Locale.setDefault(Locale.US)
            val zone = ZoneId.of("America/Chicago")
            val arrival = "2026-11-05T05:30:00Z"
            val departure = "2026-11-05T08:00:00Z"

            assertEquals("Nov 4, 2026", formatIsoDateForDisplay(arrival, zone))
            assertEquals("23:30", formatIsoTimeForDisplay(arrival, zone))
            assertEquals("02:00", formatIsoTimeForDisplay(departure, zone))
            assertEquals("Nov 4-5 2026", formatFlightDateRangeForDisplay(arrival, departure, zone))
            assertEquals("Nov 4, 2026", formatFlightDateRangeForDisplay(arrival, "2026-11-05T05:45:00Z", zone))
        } finally {
            Locale.setDefault(previousLocale)
        }
    }

    @Test
    fun card_preserves_unparseable_values() {
        assertEquals("invalid", formatIsoDateForDisplay("invalid"))
        assertEquals("invalid", formatIsoTimeForDisplay("invalid"))
        assertEquals("invalid", formatFlightDateRangeForDisplay("invalid", "invalid"))
    }
    @Test
    fun card_date_ranges_keep_month_and_year_boundaries_unambiguous() {
        val previousLocale = Locale.getDefault()
        try {
            Locale.setDefault(Locale.US)
            val zone = ZoneId.of("UTC")
            assertEquals("Nov 27-30 2026", formatFlightDateRangeForDisplay("2026-11-27T10:00:00Z", "2026-11-30T12:00:00Z", zone))
            assertEquals("Nov 30-Dec 1 2026", formatFlightDateRangeForDisplay("2026-11-30T23:00:00Z", "2026-12-01T01:00:00Z", zone))
            assertEquals("Dec 31 2026-Jan 1 2027", formatFlightDateRangeForDisplay("2026-12-31T23:00:00Z", "2027-01-01T01:00:00Z", zone))
        } finally {
            Locale.setDefault(previousLocale)
        }
    }
}
