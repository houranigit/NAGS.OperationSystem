package com.nags.operations.data.realtime

import kotlinx.serialization.Serializable

/**
 * Wire envelope mirroring the server's `MobileSyncChange` record. Every push the
 * mobile receives — whether from SignalR or from the REST catch-up endpoint — has
 * this shape, so the apply path in [com.nags.operations.data.sync.SyncCoordinator]
 * only has to learn one schema.
 *
 * The `originMutationId` / `originClientId` fields are reserved for the future
 * write/outbox flow; the server always emits them as null today, but the wire
 * shape is stable so adding them later doesn't require a contract bump.
 */
@Serializable
data class MobileSyncChangeDto(
    val table: String,
    val op: String,
    val entityId: String? = null,
    val audience: String = MobileSyncAudience.AllStations,
    val version: String,
    val payload: String? = null,
    val originMutationId: String? = null,
    val originClientId: String? = null,
)

/**
 * Logical-table identifiers used in [MobileSyncChangeDto.table]. Match the
 * server-side [MobileSyncTables] one-for-one — the mobile dispatcher routes
 * envelopes to DAOs by string match against these constants.
 */
object MobileSyncTables {
    const val Flights = "flights"
    const val FlightsAog = "flights-aog"
    /** Ad Hoc station list — server wire id `flights-ad-hoc`. */
    const val FlightsAdHoc = "flights-ad-hoc"
    const val Employees = "employees"
    const val Services = "services"
    const val Tools = "tools"
    const val Materials = "materials"
    const val GeneralSupports = "general-supports"
    const val Customers = "customers"
    const val AircraftTypes = "aircraft-types"
}

/** Operations used in [MobileSyncChangeDto.op]. */
object MobileSyncOps {
    const val Upsert = "upsert"
    const val Delete = "delete"
    const val Refresh = "refresh"
}

/** Audience prefixes — informational only on the mobile side (the server picks the group). */
object MobileSyncAudience {
    const val EmployeePrefix = "employee:"
    const val StationPrefix = "station:"
    const val AllStations = "all-stations"
}
