package com.nags.operations.data.db.entities

import androidx.room.Entity
import androidx.room.PrimaryKey

/** One complete, permission-scoped list per API environment and authenticated account. */
@Entity(tableName = "flight_snapshots")
data class FlightSnapshotEntity(
    @PrimaryKey val ownerSubject: String,
    val payloadJson: String = "[]",
    val itemCount: Int = 0,
    val lastAttemptUtc: String? = null,
    val lastSuccessUtc: String? = null,
    val error: String? = null,
    val permissionDenied: Boolean = false,
    val lastDurationMs: Long? = null,
)

/** Basic details are saved only after an authorized detail read. */
@Entity(tableName = "flight_detail_cache", primaryKeys = ["ownerSubject", "flightId"])
data class FlightDetailCacheEntity(
    val ownerSubject: String,
    val flightId: String,
    val payloadJson: String,
)
