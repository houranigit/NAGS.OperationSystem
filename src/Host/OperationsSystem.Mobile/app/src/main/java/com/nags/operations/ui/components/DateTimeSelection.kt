package com.nags.operations.ui.components

import java.time.Instant
import java.time.LocalDateTime
import java.time.LocalTime
import java.time.ZoneId
import java.time.ZoneOffset

internal sealed interface DateTimeSelection {
    data class Valid(val iso: String) : DateTimeSelection
    data class Invalid(val message: String) : DateTimeSelection
}

/** Material date-picker values encode the selected calendar date at UTC midnight. */
internal fun resolveDateTimeSelection(
    selectedDateMillis: Long,
    hour: Int,
    minute: Int,
    zoneId: ZoneId,
): DateTimeSelection {
    val pickedDate = Instant.ofEpochMilli(selectedDateMillis)
        .atZone(ZoneOffset.UTC)
        .toLocalDate()
    val combined = LocalDateTime.of(pickedDate, LocalTime.of(hour, minute))
    val validOffsets = zoneId.rules.getValidOffsets(combined)
    return when (validOffsets.size) {
        0 -> DateTimeSelection.Invalid(
            "That local time does not exist because the clock moves forward. Choose another time.",
        )
        1 -> DateTimeSelection.Valid(combined.atOffset(validOffsets.single()).toString())
        else -> DateTimeSelection.Invalid(
            "That local time occurs twice because the clock moves back. Choose a time outside the repeated hour.",
        )
    }
}
