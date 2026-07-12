package com.nags.operations.ui.util

import java.util.Locale

private fun uppercaseStripWhitespace(raw: String): String =
    raw.filterNot { it.isWhitespace() }.uppercase(Locale.ROOT)

/**
 * Work-order flight number as typed in mobile forms: uppercase (locale-neutral)
 * and all whitespace removed — same convention as the operations mobile reference app.
 */
fun normalizeWorkOrderFlightNumberInput(raw: String): String =
    uppercaseStripWhitespace(raw)

/** Aircraft tail / registration — same uppercase and whitespace rules as [normalizeWorkOrderFlightNumberInput]. */
fun normalizeWorkOrderAircraftTailInput(raw: String): String =
    uppercaseStripWhitespace(raw)
