package com.nags.operations.data.repo

import com.nags.operations.data.MobileMyWorkOrderWireDto
import com.nags.operations.data.db.entities.AdHocFlightEntity
import com.nags.operations.data.db.entities.AogFlightEntity
import com.nags.operations.data.db.entities.FlightEntity
import com.nags.operations.data.db.entities.FlightServiceSummary
import kotlinx.serialization.Serializable

/**
 * Flight row unified from any cached flights table for the create-work-order form header
 * and default service-line seeding.
 */
@Serializable
data class WorkOrderFlightRow(
    val id: String,
    val flightNumber: String,
    val operationTypeCode: String,
    val sta: String,
    val std: String,
    val aircraftModel: String?,
    val customerName: String,
    val customerIataCode: String,
    val stationCode: String,
    val services: List<FlightServiceSummary>,
    val cachedMyWorkOrder: MobileMyWorkOrderWireDto? = null,
)

internal fun FlightEntity.toWorkOrderFlightRow(): WorkOrderFlightRow =
    WorkOrderFlightRow(
        id = id,
        flightNumber = flightNumber,
        operationTypeCode = operationTypeCode,
        sta = sta,
        std = std,
        aircraftModel = aircraftModel,
        customerName = customerName,
        customerIataCode = customerIataCode,
        stationCode = stationCode,
        services = services,
        cachedMyWorkOrder = myWorkOrder,
    )

internal fun AogFlightEntity.toWorkOrderFlightRow(): WorkOrderFlightRow =
    WorkOrderFlightRow(
        id = id,
        flightNumber = flightNumber,
        operationTypeCode = operationTypeCode,
        sta = sta,
        std = std,
        aircraftModel = aircraftModel,
        customerName = customerName,
        customerIataCode = customerIataCode,
        stationCode = stationCode,
        services = emptyList(),
        cachedMyWorkOrder = myWorkOrder,
    )

internal fun AdHocFlightEntity.toWorkOrderFlightRow(): WorkOrderFlightRow =
    WorkOrderFlightRow(
        id = id,
        flightNumber = flightNumber,
        operationTypeCode = operationTypeCode,
        sta = sta,
        std = std,
        aircraftModel = aircraftModel,
        customerName = customerName,
        customerIataCode = customerIataCode,
        stationCode = stationCode,
        services = emptyList(),
        cachedMyWorkOrder = myWorkOrder,
    )
