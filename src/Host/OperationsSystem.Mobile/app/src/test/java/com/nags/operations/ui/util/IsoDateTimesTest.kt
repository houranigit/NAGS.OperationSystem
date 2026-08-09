package com.nags.operations.ui.util

import java.time.ZoneId
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
}
