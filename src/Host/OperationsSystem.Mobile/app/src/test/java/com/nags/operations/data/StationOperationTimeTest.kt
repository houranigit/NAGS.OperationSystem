package com.nags.operations.data

import com.nags.operations.ui.util.formatFlightDateRangeForDisplay
import com.nags.operations.ui.util.formatIsoTimeForDisplay
import com.nags.operations.ui.util.operationLocalTime
import com.nags.operations.ui.util.stationTimeZone
import java.time.ZoneId
import java.util.Locale
import java.util.TimeZone
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class StationOperationTimeTest {
    @Test
    fun stationClockAndInputIgnoreTheDeviceZone() {
        val previous = TimeZone.getDefault()
        try {
            TimeZone.setDefault(TimeZone.getTimeZone("America/Chicago"))
            val station = stationTimeZone("Asia/Riyadh")
            assertEquals("2026-11-10T01:30", operationLocalTime("2026-11-09T22:30:00Z", station))
            assertEquals("2026-11-09T22:30:00Z", arrivalInputTime("2026-11-10T01:30", null, station))
            assertEquals("01:30", formatIsoTimeForDisplay("2026-11-09T22:30:00Z", station))
        } finally {
            TimeZone.setDefault(previous)
        }
    }

    @Test
    fun invalidStationZonesAreRejectedInsteadOfUsingTheDevice() {
        assertTrue(runCatching { stationTimeZone("") }.isFailure)
        assertTrue(runCatching { stationTimeZone("not/a-zone") }.isFailure)
    }

    @Test
    fun eachInputUsesItsDatesDaylightSavingRules() {
        val station = ZoneId.of("America/Chicago")
        assertEquals("2026-01-15T16:00:00Z", arrivalInputTime("2026-01-15T10:00", null, station))
        assertEquals("2026-07-15T15:00:00Z", arrivalInputTime("2026-07-15T10:00", null, station))
    }

    @Test
    fun daylightSavingGapsAndNewAmbiguousTimesRequireAValidTime() {
        val station = ZoneId.of("America/Chicago")
        assertEquals("", arrivalInputTime("2026-03-08T02:30", null, station))
        assertEquals("", arrivalInputTime("2026-11-01T01:30", null, station))
        assertEquals("2026-11-01T07:30:00Z", arrivalInputTime("2026-11-01T01:30", "2026-11-01T07:30:00Z", station))
    }

    @Test
    fun stationDateRangeUsesTheStationCalendarAcrossMidnight() {
        val previous = Locale.getDefault()
        try {
            Locale.setDefault(Locale.US)
            assertEquals("Nov 9-10 2026", formatFlightDateRangeForDisplay(
                "2026-11-09T20:30:00Z", "2026-11-09T22:30:00Z", ZoneId.of("Asia/Riyadh")))
        } finally {
            Locale.setDefault(previous)
        }
    }

    @Test
    fun stationZoneSurvivesRegisterAndDetailCacheSerialization() {
        val json = Json { ignoreUnknownKeys = true }
        val item = FlightListItem("flight", "104", customerName = "Customer", stationIataCode = "RUH",
            operationTypeName = "Transit", scheduledArrivalUtc = "2026-11-09T22:30:00Z",
            scheduledDepartureUtc = "2026-11-09T23:30:00Z", isPerLanding = false, stationTimeZoneId = "Asia/Riyadh")
        val detail = FlightDetail("flight", "104", "customer", customerName = "Customer", stationId = "station",
            stationIata = "RUH", stationName = "Riyadh", operationTypeId = "operation", operationTypeName = "Transit",
            scheduledArrivalUtc = item.scheduledArrivalUtc, scheduledDepartureUtc = item.scheduledDepartureUtc,
            isPerLanding = false, createdAtUtc = "2026-11-09T20:00:00Z", rowVersion = "AAAAAAAAAAE=",
            stationTimeZoneId = item.stationTimeZoneId)
        assertEquals("Asia/Riyadh", json.decodeFromString<FlightListItem>(json.encodeToString(item)).stationTimeZoneId)
        assertEquals("Asia/Riyadh", json.decodeFromString<FlightDetail>(json.encodeToString(detail)).stationTimeZoneId)
        val oldItem = kotlinx.serialization.json.JsonObject(
            json.parseToJsonElement(json.encodeToString(item)).jsonObject - "stationTimeZoneId")
        val oldDetail = kotlinx.serialization.json.JsonObject(
            json.parseToJsonElement(json.encodeToString(detail)).jsonObject - "stationTimeZoneId")
        assertTrue(runCatching { json.decodeFromString<FlightListItem>(oldItem.toString()) }.isFailure)
        assertTrue(runCatching { json.decodeFromString<FlightDetail>(oldDetail.toString()) }.isFailure)
        assertTrue(runCatching { item.copy(stationTimeZoneId = "invalid/zone") }.isFailure)
    }
}
