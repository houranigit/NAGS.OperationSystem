package com.nags.operations.data

import java.time.ZoneId
import kotlinx.serialization.Serializable

/** The mobile feed is restricted by station, membership and the rolling 24-hour window. */
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
    val stationId: String? = null,
    val asOfUtc: String? = null,
    val windowStartUtc: String? = null,
    val windowEndUtc: String? = null,
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
    val stationId: String? = null,
    val isMyFlight: Boolean = false,
    val isMyAdHocFlight: Boolean = false,
    val stationTimeZoneId: String,
) {
    init { ZoneId.of(stationTimeZoneId) }
}

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
    val arrivalSubmissions: List<FlightArrivalSubmission> = emptyList(),
    val departureSubmissions: List<FlightDepartureSubmission> = emptyList(),
    val currentStaffMemberId: String? = null,
    val canSaveArrival: Boolean = false,
    val canSaveDeparture: Boolean = false,
    val hasCancellationSubmission: Boolean = false,
    val stationTimeZoneId: String,
    @kotlinx.serialization.Transient val fromCache: Boolean = false,
    val cancellationSubmissions: List<FlightCancellationSubmission> = emptyList(),
    val canSaveCancellation: Boolean = false,
) {
    init { ZoneId.of(stationTimeZoneId) }
}

@Serializable
data class InviteEmployeesRequest(val staffMemberIds: List<String>)
