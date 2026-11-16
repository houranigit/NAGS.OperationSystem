package com.nags.operations.data.db.entities

import androidx.room.Entity
import com.nags.operations.data.FlightReturnToRampRecord
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

/** Desired occurrence and frozen HTTP attempt reference independently retained private media. */
@Entity(tableName = "flight_rtr_pending", primaryKeys = ["ownerSubject", "flightId", "occurrenceId"])
@Serializable
data class FlightReturnToRampPendingEntity(
    val ownerSubject: String, val flightId: String, val occurrenceId: String, val flightNumber: String,
    val changeId: String, val inputJson: String, val previewJson: String, val rowVersion: String,
    val reviewedOccurrenceJson: String?, val savedAtUtc: String, val status: String = "pending", val error: String? = null,
    val filesJson: String = "{}", val needsComparison: Boolean = false,
    val attemptInputJson: String? = null, val attemptRowVersion: String? = null, val attemptFilesJson: String? = null,
) {
    fun preview(json: Json): FlightReturnToRampRecord = json.decodeFromString<FlightReturnToRampRecord>(previewJson)
        .copy(localStatus = status, localSavedAtUtc = savedAtUtc)
}
