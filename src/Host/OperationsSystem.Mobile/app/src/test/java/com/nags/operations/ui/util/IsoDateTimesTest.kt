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
    fun date_time_fields_use_24_hour_time_even_in_a_12_hour_locale() {
        val originalLocale = Locale.getDefault()
        try {
            Locale.setDefault(Locale.US)
            val zone = ZoneId.of("Asia/Riyadh")

            assertEquals(
                "10/19/26\n15:42",
                formatIsoForDateTimeField("2026-10-19T12:42:00Z", zone),
            )
            assertEquals(
                "Oct 19, 2026 · 15:42",
                formatIsoForDateTimeField("2026-10-19T12:42:00Z", zone, compact = false),
            )
            assertEquals(
                "10/19/26\n00:00",
                formatIsoForDateTimeField("2026-10-18T21:00:00Z", zone),
            )
            assertEquals(
                "Oct 19, 2026 · 23:59",
                formatIsoForDateTimeField("2026-10-19T20:59:00Z", zone, compact = false),
            )
        } finally {
            Locale.setDefault(originalLocale)
        }
    }
}
