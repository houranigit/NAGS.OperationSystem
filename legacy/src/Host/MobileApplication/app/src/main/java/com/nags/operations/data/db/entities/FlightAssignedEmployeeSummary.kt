package com.nags.operations.data.db.entities

import kotlinx.serialization.Serializable

/**
 * One employee assigned to a cached flight. Lives inside [FlightEntity.assignedEmployees]
 * as a JSON-serialised list (see `FlightAssignedEmployeeConverters`) so the cache stays a
 * single row per flight — no join table.
 *
 * Only the fields the invite screen renders are kept: [employeeId] (selection / exclusion),
 * [fullName] and [manpowerTypeName] (display). The list is read-only on the device; the
 * server is the source of truth and every flight sync replaces the whole row.
 */
@Serializable
data class FlightAssignedEmployeeSummary(
    val employeeId: String,
    val fullName: String,
    val manpowerTypeName: String,
)
