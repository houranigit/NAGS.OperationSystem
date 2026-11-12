package com.nags.operations.ui.flights

import com.nags.operations.data.arrivalInputTime
import java.time.Instant
import java.time.ZoneId
import org.junit.Assert.*
import org.junit.Test

class FlightWorkTimeSeedsTest {
    private val zone = ZoneId.of("Asia/Riyadh")
    private val now = Instant.parse("2026-11-11T23:20:42Z")

    @Test fun newStartRoundsFutureActualArrivalUpToTheFirstSelectableStationMinute() {
        val bounds = flightWorkTimeBounds("2026-11-12T06:00:31.187126Z", null)
        val service = seedBlankWorkStart(FlightWorkPeriod(), zone, bounds, now)
        assertEquals("2026-11-12T09:01", service.start)
        assertEquals("2026-11-12T06:01:00Z", service.originalStartUtc)
        // Default staff inherit this same initial service period, including its UTC proof.
        val employee = FlightEmployeeDraft(period = service)
        assertEquals(service, employee.period)
        assertEquals(service.originalStartUtc, arrivalInputTime(service.start, service.originalStartUtc, zone))
    }

    @Test fun newStartAfterDepartureUsesTheLastSelectableMinuteWithoutRoundingDepartureUp() {
        val bounds = flightWorkTimeBounds("2026-11-11T20:00:31Z", "2026-11-11T22:10:59Z")
        val service = seedBlankWorkStart(FlightWorkPeriod(), zone, bounds, now)
        assertEquals("2026-11-12T01:10", service.start)
        assertEquals("2026-11-11T22:10:00Z", service.originalStartUtc)
    }

    @Test fun newlyAddedStaffUseTheIntersectionOfParentAndActualTimes() {
        val actuals = flightWorkTimeBounds("2026-11-12T06:00:31Z", "2026-11-12T08:00:59Z")
        val parent = FlightWorkPeriod("2026-11-12T09:15:20", "2026-11-12T09:30:52")
        val employeeBounds = actuals.within(parent, zone)
        val early = seedBlankWorkStart(FlightWorkPeriod(), zone, employeeBounds, now)
        assertEquals("2026-11-12T09:16", early.start)
        val late = seedBlankWorkStart(FlightWorkPeriod(), zone, employeeBounds, Instant.parse("2026-11-12T10:00:00Z"))
        assertEquals("2026-11-12T09:30", late.start)
    }

    @Test fun blankEndIncludesItsOwnStartAndRemainsInsideTheParentWindow() {
        val bounds = flightWorkTimeBounds("2026-11-12T06:00:31Z", "2026-11-12T06:30:52Z")
        val period = FlightWorkPeriod("2026-11-12T09:15:20", originalStartUtc = "2026-11-12T06:15:20Z")
        assertEquals("2026-11-12T09:16", seedBlankWorkEnd(period, zone, bounds, preferNow = true, now = now).end)
        assertEquals("2026-11-12T09:16", seedBlankWorkEnd(period, zone, bounds, preferNow = false, now = now).end)
        val late = seedBlankWorkEnd(period, zone, bounds, preferNow = true, now = Instant.parse("2026-11-12T10:00:00Z"))
        assertEquals("2026-11-12T09:30", late.end)
        assertEquals(period.start, late.start)
        assertEquals(period.originalStartUtc, late.originalStartUtc)
    }

    @Test fun explicitOrPendingTimesAreNeverReplacedEvenWhenOutsideTheCurrentWindow() {
        val bounds = flightWorkTimeBounds("2026-11-12T06:00:31Z", "2026-11-12T08:00:00Z")
        val explicit = FlightWorkPeriod("2026-11-12T02:20", "invalid entered end", "2026-11-11T23:20:00Z", "old end")
        assertSame(explicit, seedBlankWorkStart(explicit, zone, bounds, now))
        assertSame(explicit, seedBlankWorkEnd(explicit, zone, bounds, preferNow = true, now = now))
    }

    @Test fun aWindowWithoutAnySelectableMinuteKeepsTheOrdinaryDefaultForStrictValidation() {
        val bounds = flightWorkTimeBounds("2026-11-12T06:00:31Z", "2026-11-12T06:00:59Z")
        val value = seedBlankWorkStart(FlightWorkPeriod(), zone, bounds, now)
        assertEquals("2026-11-12T02:20", value.start)
        assertTrue(Instant.parse(checkNotNull(value.originalStartUtc)) < checkNotNull(bounds.lowerUtc))
        assertEquals(Instant.parse("2026-11-12T06:00:31Z"), bounds.lowerUtc)
        assertEquals(Instant.parse("2026-11-12T06:00:59Z"), bounds.upperUtc)
    }

    @Test fun aKnownNewSeedRetainsItsOffsetDuringTheRepeatedDaylightSavingHour() {
        val chicago = ZoneId.of("America/Chicago")
        val value = seedBlankWorkStart(FlightWorkPeriod(), chicago, FlightWorkTimeBounds(), Instant.parse("2026-11-01T07:30:42Z"))
        assertEquals("2026-11-01T01:30", value.start)
        assertEquals("2026-11-01T07:30:00Z", arrivalInputTime(value.start, value.originalStartUtc, chicago))
    }

    @Test fun originallyBlankStaffFollowTheInitialSeedAndFurtherWheelChangesInThatPicker() {
        val original = FlightServiceDraft(employees = listOf(FlightEmployeeDraft()))
        val following = original.employees.filter { it.period.start.isBlank() }.map { it.id }.toSet()
        val initial = seedBlankWorkStart(original.period, zone,
            flightWorkTimeBounds("2026-11-12T06:00:31Z", null), now)
        val seeded = servicePeriodWithInitialStaff(original, initial, following)
        assertEquals(initial.start, seeded.employees.single().period.start)
        assertEquals(initial.originalStartUtc, seeded.employees.single().period.originalStartUtc)
        val selected = servicePeriodWithInitialStaff(seeded, initial.copy(start = "2026-11-12T09:30"), following)
        assertEquals(selected.period.start, selected.employees.single().period.start)
        assertEquals("2026-11-12T06:30:00Z", arrivalInputTime(selected.employees.single().period.start,
            selected.employees.single().period.originalStartUtc, zone))
    }

    @Test fun initialPickerTrackingLeavesPreviouslyEnteredStaffPeriodsUnchanged() {
        val explicit = FlightEmployeeDraft(period = FlightWorkPeriod("2026-11-12T10:00", "2026-11-12T10:30",
            "2026-11-12T07:00:00Z", "2026-11-12T07:30:00Z"))
        val blank = FlightEmployeeDraft()
        val original = FlightServiceDraft(employees = listOf(explicit, blank))
        val following = original.employees.filter { it.period.start.isBlank() }.map { it.id }.toSet()
        val updated = servicePeriodWithInitialStaff(original,
            FlightWorkPeriod("2026-11-12T09:30", originalStartUtc = "2026-11-12T06:30:00Z"), following)
        assertEquals(explicit, updated.employees.first())
        assertEquals("2026-11-12T09:30", updated.employees.last().period.start)
    }

    @Test fun laterParentEditsDoNotMoveStaffAfterTheFirstPickerSessionCloses() {
        val period = FlightWorkPeriod("2026-11-12T09:30", originalStartUtc = "2026-11-12T06:30:00Z")
        val existing = FlightServiceDraft(period = period, employees = listOf(FlightEmployeeDraft(period = period)))
        val edited = servicePeriodWithInitialStaff(existing, period.copy(start = "2026-11-12T10:00"), emptySet())
        assertEquals("2026-11-12T10:00", edited.period.start)
        assertEquals(existing.employees, edited.employees)
    }
}
