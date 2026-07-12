package com.nags.operations.data.db.entities

import androidx.room.Entity
import androidx.room.PrimaryKey

/**
 * Active staff members at the signed-in user's home station. The sync replaces this entire
 * table on every refresh so a roster change on the server (e.g. a transfer) flows down
 * without per-row reconciliation.
 *
 * [staffMemberId] is the MasterData StaffMember id (used for API calls); [employeeNumber]
 * is the human-readable employee number shown in pickers.
 */
@Entity(tableName = "employees")
data class EmployeeEntity(
    @PrimaryKey val staffMemberId: String,
    val fullName: String,
    val employeeNumber: String,
)
