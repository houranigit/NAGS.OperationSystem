package com.nags.operations.data.db.entities

import androidx.room.Entity
import kotlinx.serialization.Serializable

/** Durable desired departure and an immutable transmitted attempt, separate from flight caches. */
@Entity(tableName = "flight_departure_pending", primaryKeys = ["ownerSubject", "flightId"])
@Serializable
data class FlightDeparturePendingEntity(
    val ownerSubject: String,
    val flightId: String,
    val flightNumber: String,
    val staffMemberId: String,
    val changeId: String,
    val inputJson: String,
    val rowVersion: String,
    val reviewedDepartureJson: String,
    val savedAtUtc: String,
    val signatureFile: String? = null,
    val needsDepartureComparison: Boolean = false,
    val status: String = "pending",
    val error: String? = null,
    val attemptInputJson: String? = null,
    val attemptRowVersion: String? = null,
    val attemptSignatureFile: String? = null,
    val reviewSourceInputJson: String? = null,
)
