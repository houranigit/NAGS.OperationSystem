package com.nags.operations.data.db.entities

import androidx.room.Entity
import androidx.room.ColumnInfo

/** A combined Save is transmitted and acknowledged as one unit. Section rows are UI projections. */
@Entity(tableName = "flight_operations_batch", primaryKeys = ["ownerSubject", "flightId"])
data class FlightOperationsBatchEntity(
    val ownerSubject: String,
    val flightId: String,
    val changeId: String,
    val inputJson: String,
    val rowVersion: String,
    val status: String = "pending",
    val error: String? = null,
    val attemptInputJson: String? = null,
    val attemptRowVersion: String? = null,
    @ColumnInfo(defaultValue = "0") val needsComparison: Boolean = false,
    @ColumnInfo(defaultValue = "0") val attemptRejected: Boolean = false,
)
