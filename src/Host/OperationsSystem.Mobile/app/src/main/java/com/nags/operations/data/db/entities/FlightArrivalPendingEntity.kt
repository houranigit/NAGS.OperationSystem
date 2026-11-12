package com.nags.operations.data.db.entities

import androidx.room.Entity
import androidx.room.ColumnInfo

/** Durable arrival intent stays separate from replaceable flight snapshots. */
@Entity(tableName = "flight_arrival_pending", primaryKeys = ["ownerSubject", "flightId"])
data class FlightArrivalPendingEntity(
    val ownerSubject: String,
    val flightId: String,
    val flightNumber: String,
    val changeId: String,
    val inputJson: String,
    val rowVersion: String,
    val savedAtUtc: String,
    val status: String = "pending",
    val error: String? = null,
    // Retain any transmitted request until acknowledged, independently of later local edits.
    val attemptInputJson: String? = null,
    // The reviewed target, or own arrival for untargeted saves. JSON "null" means no own arrival existed;
    // a null column predates reviewed snapshots. The request JSON retains an explicit target ID.
    val reviewedArrivalJson: String? = null,
    @ColumnInfo(defaultValue = "0") val needsArrivalComparison: Boolean = false,
)
