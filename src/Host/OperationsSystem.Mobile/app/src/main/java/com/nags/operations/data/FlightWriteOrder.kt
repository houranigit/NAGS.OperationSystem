package com.nags.operations.data

import java.time.OffsetDateTime

/** Relax an existing actual bound before work; a new bound is applied after work. */
fun parentTimeRelaxesBounds(previous: String?, desired: String, arrival: Boolean): Boolean = runCatching {
    val before = previous?.let { OffsetDateTime.parse(it).toInstant() } ?: return@runCatching false
    val after = OffsetDateTime.parse(desired).toInstant()
    if (arrival) after <= before else after >= before
}.getOrDefault(false)
