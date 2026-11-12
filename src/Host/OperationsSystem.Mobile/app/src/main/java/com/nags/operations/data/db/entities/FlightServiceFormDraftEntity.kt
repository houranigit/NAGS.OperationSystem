package com.nags.operations.data.db.entities

import androidx.room.Entity

/** Local editor state is never pending HTTP work and carries no synchronization status. */
@Entity(tableName = "flight_service_form_drafts", primaryKeys = ["ownerSubject", "flightId"])
data class FlightServiceFormDraftEntity(val ownerSubject: String, val flightId: String, val formJson: String,
    val attachmentFilesJson: String, val savedAtUtc: String)
