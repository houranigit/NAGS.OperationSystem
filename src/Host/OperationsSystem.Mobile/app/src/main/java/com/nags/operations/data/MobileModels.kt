package com.nags.operations.data

import kotlinx.serialization.Serializable

/**
 * Wire DTOs for the `/api/v1/mobile` endpoints. Field names mirror the server's camelCase
 * JSON exactly. These are deliberately separate from the Room entities — DTOs change with
 * the API contract, entities change with the local schema.
 */

/** Mirrors server `MobileMeDto` (`GET /api/v1/mobile/me`). */
@Serializable
data class MobileMeDto(
    val staffMemberId: String,
    val fullName: String,
    val employeeId: String,
    val stationId: String,
    val stationIata: String,
    val stationName: String,
    val manpowerTypeId: String,
    val manpowerTypeName: String? = null,
)

/** One id+name catalog row (tools, materials, general supports). */
@Serializable
data class MobileCatalogItemDto(
    val id: String,
    val name: String,
)

/**
 * A service catalog row. `isAircraftPerLanding` marks the well-known Per Landing designation,
 * which must never appear in work-order service-line pickers.
 */
@Serializable
data class MobileServiceCatalogItemDto(
    val id: String,
    val name: String,
    val isAircraftPerLanding: Boolean = false,
)

@Serializable
data class MobileCustomerDto(
    val id: String,
    val iataCode: String? = null,
    val name: String,
)

@Serializable
data class MobileAircraftTypeDto(
    val id: String,
    val manufacturer: String,
    val model: String,
)

/** Mirrors server `MobileCatalogsDto` (`GET /api/v1/mobile/catalogs`). */
@Serializable
data class MobileCatalogsDto(
    val services: List<MobileServiceCatalogItemDto> = emptyList(),
    /** Services the signed-in staff member may select as performed services. */
    val allowedPerformedServiceIds: List<String> = emptyList(),
    val tools: List<MobileCatalogItemDto> = emptyList(),
    val materials: List<MobileCatalogItemDto> = emptyList(),
    val generalSupports: List<MobileCatalogItemDto> = emptyList(),
    val customers: List<MobileCustomerDto> = emptyList(),
    val aircraftTypes: List<MobileAircraftTypeDto> = emptyList(),
    val generatedAtUtc: String,
)

/** Mirrors server `AssignedEmployeeDto` — one staff member (roster row / pickers). */
@Serializable
data class MobileStaffMemberDto(
    val staffMemberId: String,
    val fullName: String,
    val employeeId: String,
)

/**
 * A planned service selected when the flight was scheduled. The create-work-order form copies
 * every non-Per-Landing planned service into a seeded service line the user completes or removes.
 */
@Serializable
data class MobilePlannedServiceDto(
    val serviceId: String,
    val name: String,
    val isAircraftPerLanding: Boolean = false,
)

/**
 * Mirrors server `MobileFlightDto`. One shape serves the My / Per-Landing / Ad-Hoc lists and
 * the single-flight fetch used by the realtime upsert path. `myWorkOrder` embeds the caller's
 * active work order (full detail) so the form hydrates offline from Room alone.
 */
@Serializable
data class MobileFlightDto(
    val id: String,
    val flightNumber: String,
    val originalFlightNumber: String,
    val customerId: String,
    val customerIataCode: String? = null,
    val customerName: String,
    val stationId: String,
    val stationIata: String,
    val operationTypeId: String,
    val operationTypeName: String,
    val aircraftTypeId: String? = null,
    val aircraftTypeModel: String? = null,
    val scheduledArrivalUtc: String,
    val scheduledDepartureUtc: String,
    val status: String,
    val isPerLanding: Boolean = false,
    val isAdHoc: Boolean = false,
    val plannedServices: List<MobilePlannedServiceDto> = emptyList(),
    val assignedEmployees: List<MobileStaffMemberDto> = emptyList(),
    val myWorkOrder: WorkOrderDetailWireDto? = null,
    val otherWorkOrdersExist: Boolean = false,
    val updatedAtUtc: String? = null,
    val rowVersion: String,
    /** Server-authoritative result of the inclusive STA ±12-hour mobile window check. */
    val isWithinMobileWindow: Boolean = true,
    /** Exact server-derived boundaries used for information-only deep links. */
    val mobileWindowStartsAtUtc: String? = null,
    val mobileWindowEndsAtUtc: String? = null,
)
