package com.nags.operations.data.db.entities

import kotlinx.serialization.Serializable

/**
 * One contract service stored alongside a cached flight row. Lives inside
 * [FlightEntity.services] / [AogFlightEntity.services] as a JSON-serialised
 * list (see `FlightServiceConverters`) so the cache stays a single row per
 * flight — no join table, no foreign keys, no per-row replacement logic.
 *
 * That tradeoff is deliberate: the list is tiny (typically 1–5 entries per
 * flight), it's read-only on mobile (services are immutable after flight
 * creation server-side), and the UI never filters flights by service in SQL —
 * it scans the in-memory list to decide chip rendering and AOG routing.
 */
@Serializable
data class FlightServiceSummary(
    val serviceId: String,
    val name: String,
    val isAog: Boolean,
)
