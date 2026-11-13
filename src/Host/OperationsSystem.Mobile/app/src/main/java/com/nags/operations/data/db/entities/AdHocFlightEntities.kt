package com.nags.operations.data.db.entities

import androidx.room.Entity

@Entity(tableName = "flight_adhoc_context", primaryKeys = ["ownerSubject"])
data class AdHocFlightContextEntity(val ownerSubject: String, val payloadJson: String)

/** Creation/details intent owns no media: dependent normal batches retain their existing private files. */
@Entity(tableName = "flight_adhoc_pending", primaryKeys = ["ownerSubject", "flightId"])
data class AdHocFlightPendingEntity(val ownerSubject: String, val flightId: String, val stationId: String,
    val inputJson: String, val localDetailJson: String, val operationsBaseVersion: String,
    val savedAtUtc: String, val status: String = "pending", val error: String? = null,
    val materializedDetailJson: String? = null, val attemptOperation: String? = null,
    val attemptInputJson: String? = null, val attemptRowVersion: String? = null,
    val attemptRejected: Boolean = false, val viewRevoked: Boolean = false,
    val acknowledgedCreationInputJson: String? = null)
