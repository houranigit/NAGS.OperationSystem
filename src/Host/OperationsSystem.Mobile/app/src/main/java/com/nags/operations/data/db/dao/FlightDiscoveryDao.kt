package com.nags.operations.data.db.dao

import androidx.room.Dao
import androidx.room.Query
import androidx.room.Upsert
import com.nags.operations.data.db.entities.*
import kotlinx.coroutines.flow.Flow

@Dao
interface FlightDiscoveryDao {
    @Query("SELECT * FROM flight_discovery_queries WHERE ownerSubject = :owner") suspend fun queries(owner: String): List<FlightDiscoveryQueryEntity>
    @Query("SELECT * FROM flight_discovery_queries WHERE ownerSubject = :owner") fun observeQueries(owner: String): Flow<List<FlightDiscoveryQueryEntity>>
    @Upsert suspend fun putQuery(row: FlightDiscoveryQueryEntity)
    @Query("DELETE FROM flight_discovery_queries WHERE ownerSubject = :owner") suspend fun deleteQueries(owner: String)
    @Query("SELECT * FROM flight_discovery_details WHERE ownerSubject = :owner AND flightId = :id") suspend fun detail(owner: String, id: String): FlightDiscoveryDetailEntity?
    @Query("SELECT * FROM flight_discovery_details WHERE ownerSubject = :owner") suspend fun details(owner: String): List<FlightDiscoveryDetailEntity>
    @Upsert suspend fun putDetail(row: FlightDiscoveryDetailEntity)
    @Query("DELETE FROM flight_discovery_details WHERE ownerSubject = :owner AND flightId = :id") suspend fun deleteDetail(owner: String, id: String)
    @Query("DELETE FROM flight_discovery_details WHERE ownerSubject = :owner") suspend fun deleteDetails(owner: String)
    @Query("SELECT * FROM flight_discovery_station WHERE ownerSubject = :owner") suspend fun station(owner: String): FlightDiscoveryStationEntity?
    @Upsert suspend fun putStation(row: FlightDiscoveryStationEntity)
    @Query("DELETE FROM flight_discovery_station WHERE ownerSubject = :owner") suspend fun deleteStation(owner: String)
    @Query("SELECT * FROM flight_local_interactions WHERE ownerSubject = :owner") suspend fun interactions(owner: String): List<FlightLocalInteractionEntity>
    @Query("SELECT * FROM flight_local_interactions WHERE ownerSubject = :owner") fun observeInteractions(owner: String): Flow<List<FlightLocalInteractionEntity>>
    @Upsert suspend fun putInteraction(row: FlightLocalInteractionEntity)
    @Query("DELETE FROM flight_local_interactions WHERE ownerSubject = :owner AND flightId = :id") suspend fun deleteInteraction(owner: String, id: String)
    @Query("DELETE FROM flight_local_interactions WHERE ownerSubject = :owner") suspend fun deleteInteractions(owner: String)
    @Query("DELETE FROM flight_discovery_queries") suspend fun clearQueries()
    @Query("DELETE FROM flight_discovery_details") suspend fun clearDetails()
    @Query("DELETE FROM flight_discovery_station") suspend fun clearStations()
    @Query("DELETE FROM flight_local_interactions") suspend fun clearInteractions()
}
