package com.nags.operations.data.db.entities

import kotlinx.serialization.Serializable

/**
 * One staff member assigned to a cached flight. Lives inside [FlightEntity.assignedEmployees]
 * as a JSON-serialised list (see `FlightAssignedEmployeeConverters`) so the cache stays a
 * single row per flight — no join table.
 *
 * [staffMemberId] is the MasterData StaffMember id used for selection/exclusion; [employeeId]
 * is the human-readable employee number shown next to the name. The list is read-only on the
 * device; the server is the source of truth and every flight sync replaces the whole row.
 */
@Serializable
data class FlightAssignedEmployeeSummary(
    val staffMemberId: String,
    val fullName: String,
    val employeeId: String,
)
