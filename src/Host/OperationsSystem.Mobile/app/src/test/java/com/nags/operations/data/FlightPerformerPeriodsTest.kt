package com.nags.operations.data

import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import org.junit.Assert.*
import org.junit.Test

class FlightPerformerPeriodsTest {
    private val firstId = "11111111-1111-1111-1111-111111111111"
    private val secondId = "22222222-2222-2222-2222-222222222222"
    private val thirdId = "33333333-3333-3333-3333-333333333333"
    private val staffId = "44444444-4444-4444-4444-444444444444"
    private val otherStaff = "55555555-5555-5555-5555-555555555555"
    private val version = "AAAAAAAAAAE="
    private val parentStart = "2026-11-11T00:00:00Z"
    private fun period(id: String, start: String, end: String? = null, staff: String = staffId) =
        OperationalWorkPeriod(id, staff, "2026-11-11T$start:00Z", end?.let { "2026-11-11T$it:00Z" })

    private fun service(periods: List<OperationalWorkPeriod>) = SaveFlightServiceRequest(
        "66666666-6666-6666-6666-666666666666", parentStart,
        performers = periods.map { SaveFlightServicePerformerRequest(it.id, it.ownerId, it.startUtc, it.endUtc) })
    private fun task(periods: List<OperationalWorkPeriod>) = SaveFlightTaskRequest(0, parentStart,
        performers = periods.map { SaveFlightTaskPerformerRequest(it.id, it.ownerId, it.startUtc, it.endUtc) })
    private fun errors(periods: List<OperationalWorkPeriod>) = listOf(service(periods).validationErrors(version), task(periods).validationErrors(version))
    private fun conflicts(vararg ids: String) = ids.map { "performers.$it.overlappingPeriods" }.toSet()

    @Test fun repeatedEmployeeWorksAdjacentShiftsAndDifferentDaysOnTheSameLongRunningParent() {
        val rows = listOf(period(firstId, "08:00", "12:00"), period(secondId, "12:00", "16:00"),
            period(thirdId, "08:00", "12:00").copy(startUtc = "2026-11-12T08:00:00Z", endUtc = "2026-11-12T12:00:00Z"))
        errors(rows).forEach { assertTrue(it.toString(), it.isEmpty()) }
    }

    @Test fun partialContainedIdenticalAndOpenWindowsConflictForOnlyTheRepeatedEmployee() {
        val first = period(firstId, "08:00", "12:00")
        for (second in listOf(period(secondId, "11:00", "13:00"), period(secondId, "09:00", "10:00"),
            period(secondId, "08:00", "12:00"), period(secondId, "07:00"))) {
            errors(listOf(first, second, period(thirdId, "08:00", "12:00", otherStaff)))
                .forEach { assertEquals(conflicts(firstId, secondId), it) }
        }
        errors(listOf(period(firstId, "08:00"), period(secondId, "17:00", "18:00")))
            .forEach { assertEquals(conflicts(firstId, secondId), it) }
    }

    @Test fun movingOrReassigningTheConflictingOccurrenceClearsItsErrors() {
        val first = period(firstId, "08:00", "12:00")
        val second = period(secondId, "11:00", "13:00")
        for (fixed in listOf(period(secondId, "12:00", "13:00"), second.copy(ownerId = otherStaff)))
            errors(listOf(first, fixed)).forEach { assertTrue(it.isEmpty()) }
    }

    @Test fun identicalZeroWindowsConflictWhileOtherZeroPeriodsReserveNoTime() {
        errors(listOf(period(firstId, "10:00", "10:00"), period(secondId, "10:00", "10:00")))
            .forEach { assertEquals(conflicts(firstId, secondId), it) }
        errors(listOf(period(firstId, "08:00", "12:00"), period(secondId, "10:00", "10:00"),
            period(thirdId, "10:01", "10:01"))).forEach { assertTrue(it.isEmpty()) }
    }

    @Test fun equalInstantsWithDifferentOffsetsAndStaffIdCasingStillConflict() {
        val first = period(firstId, "08:00", "12:00", "AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA")
        val second = period(secondId, "08:00", "12:00", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
            .copy(startUtc = "2026-11-11T10:00:00+02:00", endUtc = "2026-11-11T14:00:00+02:00")
        errors(listOf(first, second)).forEach { assertEquals(conflicts(firstId, secondId), it) }
    }

    @Test fun occurrenceIdsStayUniqueAndMalformedPeriodsDoNotManufactureOverlapErrors() {
        val first = period(firstId, "08:00", "12:00")
        errors(listOf(first, first)).forEach { assertEquals(setOf("performers"), it) }
        val invalidPeriods = listOf(
            period(secondId, "08:00", "12:00").copy(startUtc = "invalid") to "startUtc",
            period(secondId, "08:00", "12:00").copy(endUtc = "invalid") to "endUtc",
            period(secondId, "11:00", "10:00") to "endUtc",
        )
        invalidPeriods.forEach { (invalid, field) ->
            errors(listOf(first, invalid)).forEach { assertEquals(setOf("performers.$secondId.$field"), it) }
        }
    }

    @Test fun repeatedEmployeeOccurrencesSurviveNormalizationAndQueuedWireRoundTrips() {
        val periods = listOf(period(secondId, "12:00", "16:00"), period(firstId, "08:00", "12:00"))
        val savedService = service(periods).normalized()
        val savedTask = task(periods).normalized()
        assertEquals(listOf(firstId, secondId), savedService.performers.map { it.id })
        assertEquals(listOf(staffId, staffId), savedService.performers.map { it.staffMemberId })
        assertEquals(savedService, Json.decodeFromString<SaveFlightServiceRequest>(Json.encodeToString(savedService)))
        assertEquals(savedTask, Json.decodeFromString<SaveFlightTaskRequest>(Json.encodeToString(savedTask)))
        assertTrue(savedService.validationErrors(version).isEmpty())
        assertTrue(savedTask.validationErrors(version).isEmpty())
    }
}
