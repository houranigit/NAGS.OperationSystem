package com.nags.operations.data

import kotlinx.serialization.Serializable

enum class FlightShareDirection(val key: String) { All("all"), Received("received"), Sent("sent") }

@Serializable
data class FlightSharePost(
    val id: String,
    val flightId: String,
    val authorStaffMemberId: String,
    val authorFullName: String,
    val authorEmployeeId: String,
    val message: String? = null,
    val createdAtUtc: String,
    val isStationWide: Boolean,
    val recipients: List<AssignedEmployee> = emptyList(),
    val flight: FlightListItem,
    val stationName: String? = null,
)

@Serializable
data class FlightSharePage(
    val items: List<FlightSharePost> = emptyList(),
    val page: Int = 1,
    val pageSize: Int = 20,
    val totalCount: Long = 0,
    val totalPages: Int = 0,
    val hasPreviousPage: Boolean = false,
    val hasNextPage: Boolean = false,
    val stationId: String,
    val asOfUtc: String,
    val windowStartUtc: String,
    val windowEndUtc: String,
    @kotlinx.serialization.Transient val fromCache: Boolean = false,
    @kotlinx.serialization.Transient val ownerSessionKey: String? = null,
) {
    internal fun flightPage() = FlightPage(items.map { it.flight }, page, pageSize, totalCount,
        totalPages, hasPreviousPage, hasNextPage, stationId = stationId, asOfUtc = asOfUtc,
        windowStartUtc = windowStartUtc, windowEndUtc = windowEndUtc)
}

@Serializable
data class PublishFlightShareRequest(val message: String? = null, val staffMemberIds: List<String> = emptyList())
