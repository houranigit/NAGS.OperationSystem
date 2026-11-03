package com.nags.operations.data.db.entities

import androidx.room.Entity

/** One atomic snapshot per API environment, authenticated account, and catalog. */
@Entity(tableName = "shared_catalog_snapshots", primaryKeys = ["ownerSubject", "catalog"])
data class SharedCatalogSnapshotEntity(
    /** API base URL plus JWT subject, preventing copied account IDs from sharing environments. */
    val ownerSubject: String,
    val catalog: String,
    val payloadJson: String = "[]",
    val itemCount: Int = 0,
    val lastAttemptUtc: String? = null,
    val lastSuccessUtc: String? = null,
    val error: String? = null,
    val permissionDenied: Boolean = false,
    val lastDurationMs: Long? = null,
)
