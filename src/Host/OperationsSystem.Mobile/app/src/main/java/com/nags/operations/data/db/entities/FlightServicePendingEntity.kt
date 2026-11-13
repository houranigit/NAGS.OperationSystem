package com.nags.operations.data.db.entities

import androidx.room.Entity
import kotlinx.serialization.Serializable

/** Editable desired work and its immutable unknown HTTP attempt have independent lifetimes. */
@Entity(tableName = "flight_service_pending", primaryKeys = ["ownerSubject", "flightId", "serviceRecordId"])
@Serializable
data class FlightServicePendingEntity(
    val ownerSubject: String, val flightId: String, val serviceRecordId: String, val flightNumber: String,
    val changeId: String, val operation: String, val inputJson: String?, val rowVersion: String,
    val reviewedServiceJson: String?, val savedAtUtc: String, val needsServiceComparison: Boolean = false,
    val status: String = "pending", val error: String? = null,
    val attemptOperation: String? = null, val attemptInputJson: String? = null, val attemptRowVersion: String? = null,
    val attachmentFilesJson: String = "{}", val attemptAttachmentFilesJson: String? = null,
    val reviewSourceInputJson: String? = null,
)
