package com.nags.operations.data.db.entities

import androidx.room.Entity

@Entity(tableName = "flight_discovery_queries", primaryKeys = ["ownerSubject", "source", "query"])
data class FlightDiscoveryQueryEntity(val ownerSubject: String, val source: String, val query: String,
    val payloadJson: String? = null, val lastAttemptUtc: String? = null, val lastSuccessUtc: String? = null,
    val error: String? = null, val denied: Boolean = false, val accessStatus: Int? = null)

@Entity(tableName = "flight_discovery_details", primaryKeys = ["ownerSubject", "flightId"])
data class FlightDiscoveryDetailEntity(val ownerSubject: String, val flightId: String, val payloadJson: String,
    val revision: Long = 0)

@Entity(tableName = "flight_discovery_station", primaryKeys = ["ownerSubject"])
data class FlightDiscoveryStationEntity(val ownerSubject: String, val stationId: String)

/** An actual durable local Save bridges the interval before its cross-device server History refresh. */
@Entity(tableName = "flight_local_interactions", primaryKeys = ["ownerSubject", "flightId"])
data class FlightLocalInteractionEntity(val ownerSubject: String, val flightId: String, val stationId: String,
    val payloadJson: String, val interactionAtUtc: String, val usedSearch: Boolean,
    val acknowledged: Boolean = false, val viewRevoked: Boolean = false)
