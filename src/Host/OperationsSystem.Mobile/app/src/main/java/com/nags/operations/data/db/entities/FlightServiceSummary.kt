package com.nags.operations.data.db.entities

import kotlinx.serialization.Serializable

/**
 * One planned service stored alongside a cached flight row. Lives inside
 * [FlightEntity.plannedServices] as a JSON-serialised list (see `FlightServiceConverters`) so
 * the cache stays a single row per flight — no join table, no foreign keys.
 *
 * That tradeoff is deliberate: the list is tiny (1–5 entries), read-only on mobile (planned
 * services are edited on the portal), and the UI never filters flights by service in SQL — it
 * scans the in-memory list for chip rendering and the create-work-order seeding rule.
 */
@Serializable
data class FlightServiceSummary(
    val serviceId: String,
    val name: String,
    val isAircraftPerLanding: Boolean = false,
)
