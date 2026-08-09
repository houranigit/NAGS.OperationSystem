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

/**
 * The work-order UI always edits wall-clock values in the user's device zone. The argument is
 * retained for source compatibility with older callers that derived a fixed offset from a flight.
 * A ZoneId, unlike a ZoneOffset, preserves daylight-saving rules for the picked date.
 */
fun userTimeZone(): ZoneId = ZoneId.systemDefault()

@Deprecated("Use userTimeZone(); a flight's serialized offset is not the user's time zone")
@Suppress("UNUSED_PARAMETER")
fun offsetSameAsFlight(baseIso: String): ZoneId = userTimeZone()
