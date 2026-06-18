package com.nags.operations.data

import com.nags.operations.data.db.entities.AdHocFlightEntity
import com.nags.operations.data.db.entities.AogFlightEntity
import com.nags.operations.data.db.entities.FlightEntity

fun FlightEntity.toSummary(): MobileFlightSummaryDto =
    MobileFlightSummaryDto(
        id = id,
        flightNumber = flightNumber,
        customerName = customerName,
        customerIataCode = customerIataCode,
        stationCode = stationCode,
        operationTypeCode = operationTypeCode,
        sta = sta,
        std = std,
        aircraftModel = aircraftModel,
        status = status,
        canceledAt = canceledAt,
        assignedEmployeesCount = assignedEmployeesCount,
        myWorkOrder = myWorkOrder,
        otherWorkOrdersExist = otherWorkOrdersExist,
        services = services.map {
            MobileFlightServiceDto(
                serviceId = it.serviceId,
                name = it.name,
                isAog = it.isAog,
            )
        },
        assignedEmployees = assignedEmployees.map {
            MobileFlightAssignedEmployeeDto(
                employeeId = it.employeeId,
                fullName = it.fullName,
                manpowerTypeName = it.manpowerTypeName,
            )
        },
    )

fun AogFlightEntity.toSummary(): MobileFlightSummaryDto =
    MobileFlightSummaryDto(
        id = id,
        flightNumber = flightNumber,
        customerName = customerName,
        customerIataCode = customerIataCode,
        stationCode = stationCode,
        operationTypeCode = operationTypeCode,
        sta = sta,
        std = std,
        aircraftModel = aircraftModel,
        status = status,
        canceledAt = canceledAt,
        assignedEmployeesCount = assignedEmployeesCount,
        myWorkOrder = myWorkOrder,
        otherWorkOrdersExist = otherWorkOrdersExist,
        services = emptyList(),
    )

fun AdHocFlightEntity.toSummary(): MobileFlightSummaryDto =
    MobileFlightSummaryDto(
        id = id,
        flightNumber = flightNumber,
        customerName = customerName,
        customerIataCode = customerIataCode,
        stationCode = stationCode,
        operationTypeCode = operationTypeCode,
        sta = sta,
        std = std,
        aircraftModel = aircraftModel,
        status = status,
        canceledAt = canceledAt,
        assignedEmployeesCount = assignedEmployeesCount,
        myWorkOrder = myWorkOrder,
        otherWorkOrdersExist = otherWorkOrdersExist,
        services = emptyList(),
    )
