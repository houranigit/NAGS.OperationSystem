package com.nags.operations.data.repo

import com.nags.operations.data.WorkOrderDetailWireDto
import com.nags.operations.data.db.entities.AdHocFlightEntity
import com.nags.operations.data.db.entities.FlightEntity
import com.nags.operations.data.db.entities.FlightServiceSummary
import com.nags.operations.data.db.entities.PerLandingFlightEntity
import kotlinx.serialization.Serializable

/**
 * Flight row unified from any cached flights table for the create-work-order form header and
 * the planned-service seeding rule: every non-Per-Landing planned service becomes a seeded
 * service line the user completes (picks one or more performers) or removes; Per-Landing flights start
 * with zero seeded lines.
 */
@Serializable
data class WorkOrderFlightRow(
    val id: String,
    val flightNumber: String,
    val operationTypeName: String,
    val sta: String,
    val std: String,
    val aircraftTypeId: String?,
    val aircraftTypeModel: String?,
    val customerName: String,
    val customerIataCode: String?,
    val stationIata: String,
    val isPerLanding: Boolean,
    val isAdHoc: Boolean,
    val plannedServices: List<FlightServiceSummary>,
    val cachedMyWorkOrder: WorkOrderDetailWireDto? = null,
)

internal fun FlightEntity.toWorkOrderFlightRow(): WorkOrderFlightRow =
    WorkOrderFlightRow(
        id = id,
        flightNumber = flightNumber,
        operationTypeName = operationTypeName,
        sta = sta,
        std = std,
        aircraftTypeId = aircraftTypeId,
        aircraftTypeModel = aircraftTypeModel,
        customerName = customerName,
        customerIataCode = customerIataCode,
        stationIata = stationIata,
        isPerLanding = isPerLanding,
        isAdHoc = isAdHoc,
        plannedServices = plannedServices,
        cachedMyWorkOrder = myWorkOrder,
    )

internal fun PerLandingFlightEntity.toWorkOrderFlightRow(): WorkOrderFlightRow =
    WorkOrderFlightRow(
        id = id,
        flightNumber = flightNumber,
        operationTypeName = operationTypeName,
        sta = sta,
        std = std,
        aircraftTypeId = aircraftTypeId,
        aircraftTypeModel = aircraftTypeModel,
        customerName = customerName,
        customerIataCode = customerIataCode,
        stationIata = stationIata,
        isPerLanding = isPerLanding,
        isAdHoc = isAdHoc,
        plannedServices = plannedServices,
        cachedMyWorkOrder = myWorkOrder,
    )

internal fun AdHocFlightEntity.toWorkOrderFlightRow(): WorkOrderFlightRow =
    WorkOrderFlightRow(
        id = id,
        flightNumber = flightNumber,
        operationTypeName = operationTypeName,
        sta = sta,
        std = std,
        aircraftTypeId = aircraftTypeId,
        aircraftTypeModel = aircraftTypeModel,
        customerName = customerName,
        customerIataCode = customerIataCode,
        stationIata = stationIata,
        isPerLanding = isPerLanding,
        isAdHoc = isAdHoc,
        plannedServices = plannedServices,
        cachedMyWorkOrder = myWorkOrder,
    )
