package com.nags.operations.ui.util

import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.ZoneOffset
import java.time.ZonedDateTime
import java.time.format.DateTimeFormatter
import java.time.format.FormatStyle
import java.util.Locale

private fun displayFormatter(): DateTimeFormatter =
    DateTimeFormatter.ofLocalizedDateTime(FormatStyle.MEDIUM, FormatStyle.SHORT)
        .withLocale(Locale.getDefault())

fun parseOffsetDateTime(iso: String): OffsetDateTime =
    try {
        OffsetDateTime.parse(iso)
    } catch (_: Exception) {
        OffsetDateTime.ofInstant(java.time.Instant.parse(iso), ZoneOffset.UTC)
    }

fun parseInUserZone(iso: String, zoneId: ZoneId = ZoneId.systemDefault()): ZonedDateTime =
    parseOffsetDateTime(iso).atZoneSameInstant(zoneId)

fun formatIsoForDisplay(iso: String, zoneId: ZoneId = ZoneId.systemDefault()): String =
    try {
        parseInUserZone(iso, zoneId).format(displayFormatter())
    } catch (_: Exception) {
        iso
    }

fun formatIsoDateForDisplay(iso: String, zoneId: ZoneId = ZoneId.systemDefault()): String =
    try {
        parseInUserZone(iso, zoneId).format(DateTimeFormatter.ofLocalizedDate(FormatStyle.MEDIUM)
            .withLocale(Locale.getDefault()))
    } catch (_: Exception) {
        iso
    }

fun formatIsoTimeForDisplay(iso: String, zoneId: ZoneId = ZoneId.systemDefault()): String =
    try {
        parseInUserZone(iso, zoneId).format(DateTimeFormatter.ofPattern("HH:mm", Locale.getDefault()))
    } catch (_: Exception) {
        iso
    }

/** The date range uses local calendar days; the card's STA and STD fields contain times only. */
fun formatFlightDateRangeForDisplay(arrivalIso: String, departureIso: String, zoneId: ZoneId = ZoneId.systemDefault()): String =
    try {
        val arrival = parseInUserZone(arrivalIso, zoneId).toLocalDate()
        val departure = parseInUserZone(departureIso, zoneId).toLocalDate()
        val locale = Locale.getDefault()
        val monthDay = DateTimeFormatter.ofPattern("MMM d", locale)
        val fullDate = DateTimeFormatter.ofPattern("MMM d yyyy", locale)
        when {
            arrival == departure -> formatIsoDateForDisplay(arrivalIso, zoneId)
            arrival.year != departure.year -> "${arrival.format(fullDate)} - ${departure.format(fullDate)}"
            arrival.month != departure.month -> "${arrival.format(monthDay)} - ${departure.format(fullDate)}"
            else -> "${arrival.format(monthDay)} - ${departure.format(DateTimeFormatter.ofPattern("d yyyy", locale))}"
        }
    } catch (_: Exception) {
        formatIsoDateForDisplay(arrivalIso, zoneId)
    }

/** Flight metadata carries the validated station zone; device settings never select it. */
fun stationTimeZone(id: String): ZoneId = ZoneId.of(id)

fun operationLocalTime(iso: String, zone: ZoneId): String =
    runCatching { parseInUserZone(iso, zone).toLocalDateTime().toString() }.getOrDefault("")
