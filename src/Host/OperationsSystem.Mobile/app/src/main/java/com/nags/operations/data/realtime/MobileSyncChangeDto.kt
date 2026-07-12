package com.nags.operations.data.realtime

import kotlinx.serialization.Serializable

/**
 * Wire envelope mirroring the server's `MobileSyncChange` record. Every push the
 * mobile receives — whether from SignalR or from the REST catch-up endpoint — has
 * this shape, so the apply path in [com.nags.operations.data.sync.SyncCoordinator]
 * only has to learn one schema.
 *
 * `originMutationId` carries the `clientMutationId` of the outbox mutation that caused the
 * change, letting the originating device drop its outbox row the moment the echo arrives.
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
)

/**
 * Logical-table identifiers used in [MobileSyncChangeDto.table]. Match the
 * server-side [MobileSyncTables] one-for-one — the mobile dispatcher routes
 * envelopes to DAOs by string match against these constants.
 */
object MobileSyncTables {
    const val Flights = "flights"
    const val FlightsPerLanding = "flights-per-landing"
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
