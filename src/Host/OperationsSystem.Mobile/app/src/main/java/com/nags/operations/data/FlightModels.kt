package com.nags.operations.data

import kotlinx.serialization.Serializable

/** The Operations flight register uses the same paged response on mobile and the portal. */
@Serializable
data class FlightPage(
    val items: List<FlightListItem> = emptyList(),
    val page: Int = 1,
    val pageSize: Int = 20,
    val totalCount: Long = 0,
    val totalPages: Int = 0,
    val hasPreviousPage: Boolean = false,
    val hasNextPage: Boolean = false,
    @kotlinx.serialization.Transient val fromCache: Boolean = false,
)

@Serializable
data class FlightListItem(
    val id: String,
    val flightNumber: String,
    val customerIataCode: String? = null,
    val customerName: String,
    val stationIataCode: String,
    val operationTypeName: String,
    val scheduledArrivalUtc: String,
    val scheduledDepartureUtc: String,
    val isPerLanding: Boolean,
    val aircraftTypeModel: String? = null,
    val customerId: String? = null,
    val customerLogoVersion: String? = null,
)

@Serializable
data class PlannedService(
    val serviceId: String,
    val name: String,
    val isAircraftPerLanding: Boolean,
)

@Serializable
data class AssignedEmployee(
    val staffMemberId: String,
    val fullName: String,
    val employeeId: String,
)

@Serializable
data class FlightDetail(
    val id: String,
    val flightNumber: String,
    val customerId: String,
    val customerIataCode: String? = null,
    val customerName: String,
    val stationId: String,
    val stationIata: String,
    val stationName: String,
    val operationTypeId: String,
    val operationTypeName: String,
    val aircraftTypeId: String? = null,
    val aircraftTypeModel: String? = null,
    val scheduledArrivalUtc: String,
    val scheduledDepartureUtc: String,
    val isPerLanding: Boolean,
    val plannedServices: List<PlannedService> = emptyList(),
    val assignedEmployees: List<AssignedEmployee> = emptyList(),
    val createdAtUtc: String,
    val updatedAtUtc: String? = null,
    val rowVersion: String,
    @kotlinx.serialization.Transient val fromCache: Boolean = false,
)

@Serializable
data class InviteEmployeesRequest(val staffMemberIds: List<String>)
