package com.nags.operations.ui.util

import java.time.OffsetDateTime
import java.time.ZoneOffset
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

fun formatIsoForDisplay(iso: String): String =
    try {
        parseOffsetDateTime(iso).format(displayFormatter())
    } catch (_: Exception) {
        iso
    }

/** Prefer the flight schedule row's offset when interpreting plain local datetimes. */
fun offsetSameAsFlight(baseIso: String): ZoneOffset =
    try {
        parseOffsetDateTime(baseIso).offset
    } catch (_: Exception) {
        ZoneOffset.UTC
    }
