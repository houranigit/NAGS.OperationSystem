package com.nags.operations.ui.components

import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.ZoneOffset
import org.junit.Assert.assertEquals
import org.junit.Test

class DateTimeSelectionTest {
    @Test
    fun selectedCalendarDate_isPreservedInPositiveAndNegativeTimeZones() {
        val date = LocalDate.of(2026, 10, 14)

        for (zone in listOf("Pacific/Kiritimati", "America/Adak", "Asia/Riyadh")) {
            val selection = resolveDateTimeSelection(date.materialMillis(), 9, 42, ZoneId.of(zone))
                as DateTimeSelection.Valid
            val selected = OffsetDateTime.parse(selection.iso)

            assertEquals(zone, date, selected.toLocalDate())
            assertEquals(zone, 9, selected.hour)
            assertEquals(zone, 42, selected.minute)
        }
    }

    @Test
    fun offset_comesFromTheSelectedDateIncludingDaylightSavingTime() {
        val zone = ZoneId.of("America/Chicago")

        assertEquals(
            DateTimeSelection.Valid("2026-01-15T09:42-06:00"),
            resolveDateTimeSelection(LocalDate.of(2026, 1, 15).materialMillis(), 9, 42, zone),
        )
        assertEquals(
            DateTimeSelection.Valid("2026-07-15T09:42-05:00"),
            resolveDateTimeSelection(LocalDate.of(2026, 7, 15).materialMillis(), 9, 42, zone),
        )
    }

    @Test
    fun nonexistentLocalTime_isRejectedWithTheExistingClockForwardMessage() {
        assertEquals(
            DateTimeSelection.Invalid(
                "That local time does not exist because the clock moves forward. Choose another time.",
            ),
            resolveDateTimeSelection(
                LocalDate.of(2026, 3, 8).materialMillis(),
                2,
                30,
                ZoneId.of("America/Chicago"),
            ),
        )
    }

    @Test
    fun repeatedLocalTime_isRejectedWithTheExistingClockBackMessage() {
        assertEquals(
            DateTimeSelection.Invalid(
                "That local time occurs twice because the clock moves back. Choose a time outside the repeated hour.",
            ),
            resolveDateTimeSelection(
                LocalDate.of(2026, 11, 1).materialMillis(),
                1,
                30,
                ZoneId.of("America/Chicago"),
            ),
        )
    }

    @Test
    fun timesOutsideDaylightSavingTransitions_keepTheirApplicableOffset() {
        val zone = ZoneId.of("America/Chicago")
        val springDate = LocalDate.of(2026, 3, 8).materialMillis()
        val autumnDate = LocalDate.of(2026, 11, 1).materialMillis()

        assertEquals(
            DateTimeSelection.Valid("2026-03-08T01:59-06:00"),
            resolveDateTimeSelection(springDate, 1, 59, zone),
        )
        assertEquals(
            DateTimeSelection.Valid("2026-03-08T03:00-05:00"),
            resolveDateTimeSelection(springDate, 3, 0, zone),
        )
        assertEquals(
            DateTimeSelection.Valid("2026-11-01T00:59-05:00"),
            resolveDateTimeSelection(autumnDate, 0, 59, zone),
        )
        assertEquals(
            DateTimeSelection.Valid("2026-11-01T02:00-06:00"),
            resolveDateTimeSelection(autumnDate, 2, 0, zone),
        )
    }

    @Test
    fun midnightNoonAndExactMinutes_arePreservedWithZeroSeconds() {
        val date = LocalDate.of(2026, 10, 14).materialMillis()

        for ((hour, minute) in listOf(0 to 0, 12 to 0, 9 to 42, 23 to 59)) {
            val selection = resolveDateTimeSelection(date, hour, minute, ZoneOffset.UTC)
                as DateTimeSelection.Valid
            val selected = OffsetDateTime.parse(selection.iso)

            assertEquals(hour, selected.hour)
            assertEquals(minute, selected.minute)
            assertEquals(0, selected.second)
            assertEquals(0, selected.nano)
        }
    }

    private fun LocalDate.materialMillis(): Long =
        atStartOfDay(ZoneOffset.UTC).toInstant().toEpochMilli()
}
