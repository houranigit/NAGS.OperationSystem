package com.nags.operations.data

import kotlinx.serialization.Serializable

/**
 * Wire DTOs for the `/api/mobile/v2` endpoints. Field names mirror the
 * server's PascalCase JSON exactly (kotlinx.serialization is case-insensitive
 * by default but matching the wire makes diffs easy to spot).
 *
 * These are deliberately separate from the Room entities — DTOs change with
 * the API contract, entities change with the local schema, and they evolve at
 * different cadences.
 */

@Serializable
data class MobileMeDto(
    val employeeId: String,
    val fullName: String,
    val stationId: String,
    val stationCode: String,
    val stationName: String,
    val manpowerTypeId: String,
    val manpowerTypeName: String,
)

@Serializable
data class ServiceSnapshotDto(
    val serviceId: String,
    val name: String,
    val isAog: Boolean = false,
)

@Serializable
data class ToolSnapshotDto(
    val toolId: String,
    val name: String,
)

@Serializable
data class MaterialSnapshotDto(
    val materialId: String,
    val name: String,
)

@Serializable
data class GeneralSupportSnapshotDto(
    val generalSupportId: String,
    val name: String,
)

@Serializable
data class CustomerSnapshotDto(
    val customerId: String,
    val iataCode: String,
    val name: String,
)

@Serializable
data class AircraftTypeSnapshotDto(
    val aircraftTypeId: String,
    val model: String,
)

@Serializable
data class StationSnapshotDto(
    val stationId: String,
    val iataCode: String,
    val name: String,
)

@Serializable
data class ManpowerTypeSnapshotDto(
    val manpowerTypeId: String,
    val name: String,
)

@Serializable
data class EmployeeSnapshotDto(
    val employeeId: String,
    val fullName: String,
    val stationSnapshot: StationSnapshotDto,
    val manpowerTypeSnapshot: ManpowerTypeSnapshotDto,
)

@Serializable
data class MobileV2CatalogsDto(
    val services: List<ServiceSnapshotDto> = emptyList(),
    val tools: List<ToolSnapshotDto> = emptyList(),
    val materials: List<MaterialSnapshotDto> = emptyList(),
    val generalSupports: List<GeneralSupportSnapshotDto> = emptyList(),
    val customers: List<CustomerSnapshotDto> = emptyList(),
    val aircraftTypes: List<AircraftTypeSnapshotDto> = emptyList(),
    val generatedAt: String,
)

@Serializable
data class MobileFlightSummaryDto(
    val id: String,
    val flightNumber: String,
    val customerName: String,
    val customerIataCode: String,
    val stationCode: String,
    val operationTypeCode: String,
    val sta: String,
    val std: String,
    val aircraftModel: String? = null,
    val status: Int,
    val canceledAt: String? = null,
    val assignedEmployeesCount: Int = 0,
    val myWorkOrder: MobileMyWorkOrderWireDto? = null,
    val otherWorkOrdersExist: Boolean = false,
    val services: List<MobileFlightServiceDto> = emptyList(),
    val assignedEmployees: List<MobileFlightAssignedEmployeeDto> = emptyList(),
)

/**
 * One employee assigned to a flight. Mirrors the server's `MobileFlightAssignedEmployeeDto`.
 * Cached on the flight row (My Flights only) so the invite screen can show already-assigned
 * colleagues offline and exclude them from the invite picker.
 */
@Serializable
data class MobileFlightAssignedEmployeeDto(
    val employeeId: String,
    val fullName: String,
    val manpowerTypeName: String,
)

/**
 * One contract service attached to a flight. Comes from the immutable
 * `FlightService` snapshot on the server side — the mobile UI uses these to
 * badge service chips and to drive the AOG vs scheduled work-order routing.
 */
@Serializable
data class MobileFlightServiceDto(
    val serviceId: String,
    val name: String,
    val isAog: Boolean = false,
)

/**
 * Wire DTO for the `/api/mobile/v2/flights/aog` endpoint. Same shape as
 * [MobileFlightSummaryDto] without the per-flight services list — every flight
 * on the AOG tab carries the AOG service by construction, so the chip list adds
 * no information and the server skips the JOIN entirely.
 */
@Serializable
data class MobileAogFlightSummaryDto(
    val id: String,
    val flightNumber: String,
    val customerName: String,
    val customerIataCode: String,
    val stationCode: String,
    val operationTypeCode: String,
    val sta: String,
    val std: String,
    val aircraftModel: String? = null,
    val status: Int,
    val canceledAt: String? = null,
    val assignedEmployeesCount: Int = 0,
    val myWorkOrder: MobileMyWorkOrderWireDto? = null,
    val otherWorkOrdersExist: Boolean = false,
)
