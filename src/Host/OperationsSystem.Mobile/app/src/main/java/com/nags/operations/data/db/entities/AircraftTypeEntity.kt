package com.nags.operations.data.db.entities

import androidx.room.Entity
import androidx.room.PrimaryKey

@Entity(tableName = "aircraft_types")
data class AircraftTypeEntity(
    @PrimaryKey val aircraftTypeId: String,
    val manufacturer: String,
    val model: String,
)
