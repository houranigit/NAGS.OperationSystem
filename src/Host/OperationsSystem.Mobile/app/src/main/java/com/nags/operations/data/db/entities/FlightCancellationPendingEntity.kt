package com.nags.operations.data.db.entities

import androidx.room.Entity

/** One durable desired cancellation action and its exact, possibly unacknowledged HTTP attempt. */
@Entity(tableName = "flight_cancellation_pending", primaryKeys = ["ownerSubject", "flightId"])
data class FlightCancellationPendingEntity(
    val ownerSubject: String,
    val flightId: String,
    val flightNumber: String,
    val staffMemberId: String,
    val changeId: String,
    val operation: String,
    val inputJson: String?,
    val submissionId: String?,
    val rowVersion: String,
    val savedAtUtc: String,
    val status: String = "pending",
    val error: String? = null,
    val attemptOperation: String? = null,
    val attemptInputJson: String? = null,
    val attemptSubmissionId: String? = null,
    val attemptRowVersion: String? = null,
)
