package com.nags.operations.data.db.entities

import androidx.room.Entity
import kotlinx.serialization.Serializable

/** Editable desired work and its immutable unknown HTTP attempt have independent lifetimes. */
@Entity(tableName = "flight_task_pending", primaryKeys = ["ownerSubject", "flightId", "taskRecordId"])
@Serializable
data class FlightTaskPendingEntity(
    val ownerSubject: String, val flightId: String, val taskRecordId: String, val flightNumber: String,
    val changeId: String, val operation: String, val inputJson: String?, val rowVersion: String,
    val reviewedTaskJson: String?, val savedAtUtc: String, val needsTaskComparison: Boolean = false,
    val status: String = "pending", val error: String? = null,
    val attemptOperation: String? = null, val attemptInputJson: String? = null, val attemptRowVersion: String? = null,
    val attachmentFilesJson: String = "{}", val attemptAttachmentFilesJson: String? = null,
    val reviewSourceInputJson: String? = null,
)
