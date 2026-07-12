package com.nags.operations.data

import com.nags.operations.data.db.entities.AdHocFlightEntity
import com.nags.operations.data.db.entities.FlightAssignedEmployeeSummary
import com.nags.operations.data.db.entities.FlightEntity
import com.nags.operations.data.db.entities.FlightServiceSummary
import com.nags.operations.data.db.entities.PerLandingFlightEntity

/**
 * Mechanical pivots between the wire [MobileFlightDto] and the three Room flight tables.
 * All three tables share the same row shape; only the table (list membership) differs.
 */

private fun MobileFlightDto.plannedServiceSummaries(): List<FlightServiceSummary> =
    plannedServices.map {
        FlightServiceSummary(
            serviceId = it.serviceId,
            name = it.name,
            isAircraftPerLanding = it.isAircraftPerLanding,
        )
    }

private fun MobileFlightDto.assignedEmployeeSummaries(): List<FlightAssignedEmployeeSummary> =
    assignedEmployees.map {
        FlightAssignedEmployeeSummary(
            staffMemberId = it.staffMemberId,
            fullName = it.fullName,
            employeeId = it.employeeId,
        )
    }

fun MobileFlightDto.toFlightEntity(): FlightEntity =
    FlightEntity(
        id = id,
        flightNumber = flightNumber,
        originalFlightNumber = originalFlightNumber,
        customerId = customerId,
        customerName = customerName,
        customerIataCode = customerIataCode,
        stationId = stationId,
        stationIata = stationIata,
        operationTypeId = operationTypeId,
        operationTypeName = operationTypeName,
        aircraftTypeId = aircraftTypeId,
        aircraftTypeModel = aircraftTypeModel,
        sta = scheduledArrivalUtc,
        std = scheduledDepartureUtc,
        status = status,
        isPerLanding = isPerLanding,
        isAdHoc = isAdHoc,
        myWorkOrder = myWorkOrder,
        otherWorkOrdersExist = otherWorkOrdersExist,
        plannedServices = plannedServiceSummaries(),
        assignedEmployees = assignedEmployeeSummaries(),
        rowVersion = rowVersion,
    )

fun MobileFlightDto.toPerLandingEntity(): PerLandingFlightEntity =
    PerLandingFlightEntity(
        id = id,
        flightNumber = flightNumber,
        originalFlightNumber = originalFlightNumber,
        customerId = customerId,
        customerName = customerName,
        customerIataCode = customerIataCode,
        stationId = stationId,
        stationIata = stationIata,
        operationTypeId = operationTypeId,
        operationTypeName = operationTypeName,
        aircraftTypeId = aircraftTypeId,
        aircraftTypeModel = aircraftTypeModel,
        sta = scheduledArrivalUtc,
        std = scheduledDepartureUtc,
        status = status,
        isPerLanding = isPerLanding,
        isAdHoc = isAdHoc,
        myWorkOrder = myWorkOrder,
        otherWorkOrdersExist = otherWorkOrdersExist,
        plannedServices = plannedServiceSummaries(),
        assignedEmployees = assignedEmployeeSummaries(),
        rowVersion = rowVersion,
    )

fun MobileFlightDto.toAdHocEntity(): AdHocFlightEntity =
    AdHocFlightEntity(
        id = id,
        flightNumber = flightNumber,
        originalFlightNumber = originalFlightNumber,
        customerId = customerId,
        customerName = customerName,
        customerIataCode = customerIataCode,
        stationId = stationId,
        stationIata = stationIata,
        operationTypeId = operationTypeId,
        operationTypeName = operationTypeName,
        aircraftTypeId = aircraftTypeId,
        aircraftTypeModel = aircraftTypeModel,
        sta = scheduledArrivalUtc,
        std = scheduledDepartureUtc,
        status = status,
        isPerLanding = isPerLanding,
        isAdHoc = isAdHoc,
        myWorkOrder = myWorkOrder,
        otherWorkOrdersExist = otherWorkOrdersExist,
        plannedServices = plannedServiceSummaries(),
        assignedEmployees = assignedEmployeeSummaries(),
        rowVersion = rowVersion,
    )

private fun summary(
    id: String,
    flightNumber: String,
    originalFlightNumber: String,
    customerId: String,
    customerName: String,
    customerIataCode: String?,
    stationId: String,
    stationIata: String,
    operationTypeId: String,
    operationTypeName: String,
    aircraftTypeId: String?,
    aircraftTypeModel: String?,
    sta: String,
    std: String,
    status: String,
    isPerLanding: Boolean,
    isAdHoc: Boolean,
    myWorkOrder: WorkOrderDetailWireDto?,
    otherWorkOrdersExist: Boolean,
    plannedServices: List<FlightServiceSummary>,
    assignedEmployees: List<FlightAssignedEmployeeSummary>,
    rowVersion: String,
): MobileFlightDto =
    MobileFlightDto(
        id = id,
        flightNumber = flightNumber,
        originalFlightNumber = originalFlightNumber,
        customerId = customerId,
        customerIataCode = customerIataCode,
        customerName = customerName,
        stationId = stationId,
        stationIata = stationIata,
        operationTypeId = operationTypeId,
        operationTypeName = operationTypeName,
        aircraftTypeId = aircraftTypeId,
        aircraftTypeModel = aircraftTypeModel,
        scheduledArrivalUtc = sta,
        scheduledDepartureUtc = std,
        status = status,
        isPerLanding = isPerLanding,
        isAdHoc = isAdHoc,
        plannedServices = plannedServices.map {
            MobilePlannedServiceDto(it.serviceId, it.name, it.isAircraftPerLanding)
        },
        assignedEmployees = assignedEmployees.map {
            MobileStaffMemberDto(it.staffMemberId, it.fullName, it.employeeId)
        },
        myWorkOrder = myWorkOrder,
        otherWorkOrdersExist = otherWorkOrdersExist,
        updatedAtUtc = null,
        rowVersion = rowVersion,
    )

fun FlightEntity.toSummary(): MobileFlightDto = summary(
    id, flightNumber, originalFlightNumber, customerId, customerName, customerIataCode,
    stationId, stationIata, operationTypeId, operationTypeName, aircraftTypeId, aircraftTypeModel,
    sta, std, status, isPerLanding, isAdHoc, myWorkOrder, otherWorkOrdersExist,
    plannedServices, assignedEmployees, rowVersion,
)

fun PerLandingFlightEntity.toSummary(): MobileFlightDto = summary(
    id, flightNumber, originalFlightNumber, customerId, customerName, customerIataCode,
    stationId, stationIata, operationTypeId, operationTypeName, aircraftTypeId, aircraftTypeModel,
    sta, std, status, isPerLanding, isAdHoc, myWorkOrder, otherWorkOrdersExist,
    plannedServices, assignedEmployees, rowVersion,
)

fun AdHocFlightEntity.toSummary(): MobileFlightDto = summary(
    id, flightNumber, originalFlightNumber, customerId, customerName, customerIataCode,
    stationId, stationIata, operationTypeId, operationTypeName, aircraftTypeId, aircraftTypeModel,
    sta, std, status, isPerLanding, isAdHoc, myWorkOrder, otherWorkOrdersExist,
    plannedServices, assignedEmployees, rowVersion,
)
