package com.nags.operations.data.db.entities

import androidx.room.Entity
import androidx.room.PrimaryKey
import com.nags.operations.data.WorkOrderDetailWireDto

/**
 * Schema shared by the three flight cache tables. Kept as a thin row-level shape so the pivot
 * from API DTO -> Room is mechanical: every field maps 1:1, no joins, no normalization.
 * Timestamps are stored as ISO-8601 UTC strings; the UI parses them on read.
 *
 * `status` stores the server's `FlightStatus` enum NAME (`Scheduled`, `InProgress`, `Completed`,
 * `Canceled`) exactly as it travels on the wire.
 *
 * [myWorkOrder] holds the signed-in user's active work order (full detail) when the server
 * embedded it on the row, so the create/update form hydrates from Room alone offline.
 * [rowVersion] is the base64 concurrency token from the last server read.
 */

@Entity(tableName = "flights_my")
data class FlightEntity(
    @PrimaryKey val id: String,
    val flightNumber: String,
    val originalFlightNumber: String,
    val customerId: String,
    val customerName: String,
    val customerIataCode: String?,
    val stationId: String,
    val stationIata: String,
    val operationTypeId: String,
    val operationTypeName: String,
    val aircraftTypeId: String?,
    val aircraftTypeModel: String?,
    val sta: String,
    val std: String,
    val status: String,
    val isPerLanding: Boolean,
    val isAdHoc: Boolean,
    val myWorkOrder: WorkOrderDetailWireDto? = null,
    val otherWorkOrdersExist: Boolean,
    /**
     * Planned services selected at scheduling time (JSON column via
     * [com.nags.operations.data.db.FlightServiceConverters]). The create-work-order form copies
     * every non-Per-Landing planned service into a seeded service line.
     */
    val plannedServices: List<FlightServiceSummary> = emptyList(),
    /**
     * Staff currently assigned to this flight, cached so the invite screen renders offline
     * (JSON column via [com.nags.operations.data.db.FlightAssignedEmployeeConverters]).
     */
    val assignedEmployees: List<FlightAssignedEmployeeSummary> = emptyList(),
    val rowVersion: String = "",
)

/**
 * Per-Landing flights at the user's station. Same row shape as [FlightEntity] but a separate
 * table because membership semantics differ — Per-Landing flights are visible station-wide
 * without the user being on the assigned roster.
 */
@Entity(tableName = "flights_per_landing")
data class PerLandingFlightEntity(
    @PrimaryKey val id: String,
    val flightNumber: String,
    val originalFlightNumber: String,
    val customerId: String,
    val customerName: String,
    val customerIataCode: String?,
    val stationId: String,
    val stationIata: String,
    val operationTypeId: String,
    val operationTypeName: String,
    val aircraftTypeId: String?,
    val aircraftTypeModel: String?,
    val sta: String,
    val std: String,
    val status: String,
    val isPerLanding: Boolean,
    val isAdHoc: Boolean,
    val myWorkOrder: WorkOrderDetailWireDto? = null,
    val otherWorkOrdersExist: Boolean,
    val plannedServices: List<FlightServiceSummary> = emptyList(),
    val assignedEmployees: List<FlightAssignedEmployeeSummary> = emptyList(),
    val rowVersion: String = "",
)

/**
 * Ad Hoc operation-type flights at the user's station. Same row shape; separate table because
 * the list comes from `/flights/ad-hoc` with station-wide membership.
 */
@Entity(tableName = "flights_ad_hoc")
data class AdHocFlightEntity(
    @PrimaryKey val id: String,
    val flightNumber: String,
    val originalFlightNumber: String,
    val customerId: String,
    val customerName: String,
    val customerIataCode: String?,
    val stationId: String,
    val stationIata: String,
    val operationTypeId: String,
    val operationTypeName: String,
    val aircraftTypeId: String?,
    val aircraftTypeModel: String?,
    val sta: String,
    val std: String,
    val status: String,
    val isPerLanding: Boolean,
    val isAdHoc: Boolean,
    val myWorkOrder: WorkOrderDetailWireDto? = null,
    val otherWorkOrdersExist: Boolean,
    val plannedServices: List<FlightServiceSummary> = emptyList(),
    val assignedEmployees: List<FlightAssignedEmployeeSummary> = emptyList(),
    val rowVersion: String = "",
)
