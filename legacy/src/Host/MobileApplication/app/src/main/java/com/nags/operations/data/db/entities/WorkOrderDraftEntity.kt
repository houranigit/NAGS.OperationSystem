package com.nags.operations.data.db.entities

import androidx.room.Entity
import androidx.room.PrimaryKey

/**
 * Locally saved work-order form tied to a flight snapshot. User-authored;
 * not synced until submit is implemented server-side.
 */
@Entity(tableName = "work_order_drafts")
data class WorkOrderDraftEntity(
    @PrimaryKey val draftId: String,
    val flightId: String,
    /** Denormalized for list + search without parsing JSON. */
    val flightNumber: String,
    val customerName: String,
    val staIso: String,
    val stationCode: String,
    /** Full [com.nags.operations.data.repo.WorkOrderFlightRow] JSON for resume. */
    val flightJson: String,
    /** Full [com.nags.operations.ui.workorder.CreateWorkOrderFormState] JSON. */
    val formJson: String,
    val updatedAtEpochMs: Long,
)
