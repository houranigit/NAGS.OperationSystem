package com.nags.operations.ui.flights

import java.time.LocalDateTime
import org.junit.Assert.assertEquals
import org.junit.Test

class ArrivalWheelPickerTest {
    @Test
    fun selecting_february_from_january_31_uses_the_last_valid_day() {
        assertEquals(LocalDateTime.of(2026, 2, 28, 23, 47), arrivalDateInMonth(LocalDateTime.of(2026, 1, 31, 23, 47), 2026, 2))
    }

    @Test
    fun february_selection_keeps_leap_day_when_the_target_year_supports_it() {
        assertEquals(LocalDateTime.of(2024, 2, 29, 23, 47), arrivalDateInMonth(LocalDateTime.of(2024, 1, 31, 23, 47), 2024, 2))
    }

    @Test
    fun changing_a_leap_day_to_a_non_leap_year_clamps_to_february_28() {
        assertEquals(LocalDateTime.of(2025, 2, 28, 23, 47), arrivalDateInMonth(LocalDateTime.of(2024, 2, 29, 23, 47), 2025, 2))
    }

    @Test
    fun valid_days_and_time_are_preserved_across_month_and_year_changes() {
        assertEquals(LocalDateTime.of(2027, 12, 17, 23, 47), arrivalDateInMonth(LocalDateTime.of(2026, 4, 17, 23, 47), 2027, 12))
    }

    @Test
    fun thirty_day_months_do_not_roll_an_arrival_into_the_following_month() {
        assertEquals(LocalDateTime.of(2026, 4, 30, 23, 47), arrivalDateInMonth(LocalDateTime.of(2026, 3, 31, 23, 47), 2026, 4))
    }

    @Test
    fun changing_the_year_preserves_seconds_and_fractional_time() {
        val selected = LocalDateTime.of(2024, 2, 29, 0, 15, 31, 123_000_000)

        assertEquals(LocalDateTime.of(2025, 2, 28, 0, 15, 31, 123_000_000),
            arrivalDateInMonth(selected, 2025, 2))
    }
}
