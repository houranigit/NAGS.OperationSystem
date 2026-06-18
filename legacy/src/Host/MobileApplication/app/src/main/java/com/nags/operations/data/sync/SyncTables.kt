package com.nags.operations.data.sync

/**
 * Stable identifiers for every logical table the mobile sync owns. Used as
 * primary keys in the `sync_state` Room table and as section labels on the
 * Sync Center diagnostics screen.
 *
 * Adding a new synced table is a one-line addition here plus a slice in
 * [SyncCoordinator.refreshAll]; nothing else in the app needs to know.
 */
enum class SyncTable(val displayName: String, val storageKey: String) {
    Services("Services", "services"),
    Tools("Tools", "tools"),
    Materials("Materials", "materials"),
    GeneralSupports("General supports", "general_supports"),
    Customers("Customers", "customers"),
    AircraftTypes("Aircraft types", "aircraft-types"),
    Employees("Employees (my station)", "employees"),
    Flights("Flights (assigned, non-AOG)", "flights_my"),
    AogFlights("AOG flights (my station)", "flights_aog"),
    AdHocFlights("Ad Hoc flights (my station)", "flights_ad_hoc"),
}
