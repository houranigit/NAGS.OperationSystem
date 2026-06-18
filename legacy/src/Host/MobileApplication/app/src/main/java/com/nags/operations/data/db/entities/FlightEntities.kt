package com.nags.operations.data.db.entities

import androidx.room.Entity
import androidx.room.PrimaryKey
import com.nags.operations.data.MobileMyWorkOrderWireDto

/**
 * Schema shared by both flight tables. Kept as a thin row-level shape so the
 * pivot from API DTO -> Room is mechanical: every field maps 1:1, no joins, no
 * normalization. STA / STD / canceledAt are stored as ISO-8601 strings (Room
 * doesn't ship a built-in DateTimeOffset converter and the UI parses them on
 * read into Compose-friendly formats anyway).
 *
 * `status` is the integer wire value of the server's `FlightStatus` enum so
 * downstream code doesn't need a Kotlin enum to round-trip the cache.
 *
 * [myWorkOrder] holds the signed-in user's under-review work order payload when
 * the server embedded it on the list row; `otherWorkOrdersExist` is a coarse
 * boolean used by the card to render "Other employees are also serving this flight".
 */

@Entity(tableName = "flights_my")
data class FlightEntity(
    @PrimaryKey val id: String,
    val flightNumber: String,
    val customerName: String,
    val customerIataCode: String,
    val stationCode: String,
    val operationTypeCode: String,
    val sta: String,
    val std: String,
    val aircraftModel: String?,
    val status: Int,
    val canceledAt: String?,
    val assignedEmployeesCount: Int,
    val myWorkOrder: MobileMyWorkOrderWireDto? = null,
    val otherWorkOrdersExist: Boolean,
    /**
     * Contract services attached to this flight (immutable on the server side,
     * stored as a JSON column on this row by [com.nags.operations.data.db.FlightServiceConverters]).
     */
    val services: List<FlightServiceSummary> = emptyList(),
    /**
     * Employees currently assigned to this flight, cached so the invite screen can render the
     * "already assigned" section offline and exclude them from the invite picker. Stored as a
     * JSON column by [com.nags.operations.data.db.FlightAssignedEmployeeConverters].
     */
    val assignedEmployees: List<FlightAssignedEmployeeSummary> = emptyList(),
)

/**
 * AOG flights at the user's station. Same shape as [FlightEntity] but kept in
 * a separate table because the two lists come from different endpoints with
 * different membership semantics — an AOG flight may appear here without the
 * user being on its assigned-employee roster.
 *
 * Deliberately no per-flight services list: every flight on the AOG tab
 * carries the AOG service by construction, so the chip would be redundant,
 * and the server elides the JOIN to keep the payload lean.
 */
@Entity(tableName = "flights_aog")
data class AogFlightEntity(
    @PrimaryKey val id: String,
    val flightNumber: String,
    val customerName: String,
    val customerIataCode: String,
    val stationCode: String,
    val operationTypeCode: String,
    val sta: String,
    val std: String,
    val aircraftModel: String?,
    val status: Int,
    val canceledAt: String?,
    val assignedEmployeesCount: Int,
    val myWorkOrder: MobileMyWorkOrderWireDto? = null,
    val otherWorkOrdersExist: Boolean,
)

/**
 * Ad Hoc operation-type flights at the user's station. Same row shape as [AogFlightEntity];
 * kept in a separate table because the list comes from `/flights/ad-hoc`.
 */
@Entity(tableName = "flights_ad_hoc")
data class AdHocFlightEntity(
    @PrimaryKey val id: String,
    val flightNumber: String,
    val customerName: String,
    val customerIataCode: String,
    val stationCode: String,
    val operationTypeCode: String,
    val sta: String,
    val std: String,
    val aircraftModel: String?,
    val status: Int,
    val canceledAt: String?,
    val assignedEmployeesCount: Int,
    val myWorkOrder: MobileMyWorkOrderWireDto? = null,
    val otherWorkOrdersExist: Boolean,
)
