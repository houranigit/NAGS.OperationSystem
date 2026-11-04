package com.nags.operations.data

/** Read-only flight cache diagnostics, independent of shared MasterData catalogs. */
data class FlightSyncStatus(
    val itemCount: Int = 0,
    val lastAttemptUtc: String? = null,
    val lastSuccessUtc: String? = null,
    val error: String? = null,
    val permissionDenied: Boolean = false,
    val lastDurationMs: Long? = null,
)
