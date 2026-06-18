package com.nags.operations.data.db.entities

import androidx.room.Entity
import androidx.room.PrimaryKey

/**
 * Active employees at the signed-in user's home station. The sync replaces
 * this entire table on every refresh so a roster change on the server
 * (e.g. a transfer) flows down without any per-row reconciliation.
 *
 * Station fields are denormalised on every row so the picker can render a
 * label like "Ahmed Alotaibi · RUH" without a join.
 */
@Entity(tableName = "employees")
data class EmployeeEntity(
    @PrimaryKey val employeeId: String,
    val fullName: String,
    val stationId: String,
    val stationCode: String,
    val stationName: String,
    val manpowerTypeId: String,
    val manpowerTypeName: String,
)
