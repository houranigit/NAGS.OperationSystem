package com.nags.operations.data.db.entities

import androidx.room.Entity

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
)
