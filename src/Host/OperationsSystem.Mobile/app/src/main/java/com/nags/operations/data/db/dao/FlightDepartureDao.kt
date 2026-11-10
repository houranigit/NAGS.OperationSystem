package com.nags.operations.data.db.dao

import androidx.room.Dao
import androidx.room.Query
import androidx.room.Upsert
import com.nags.operations.data.db.entities.FlightDeparturePendingEntity
import kotlinx.coroutines.flow.Flow

@Dao
interface FlightDepartureDao {
    @Query("SELECT * FROM flight_departure_pending WHERE ownerSubject = :owner ORDER BY savedAtUtc, flightId")
    fun observe(owner: String): Flow<List<FlightDeparturePendingEntity>>

    @Query("SELECT * FROM flight_departure_pending WHERE ownerSubject = :owner ORDER BY savedAtUtc, flightId")
    suspend fun rows(owner: String): List<FlightDeparturePendingEntity>

    @Upsert
    suspend fun put(row: FlightDeparturePendingEntity)

    @Query("DELETE FROM flight_departure_pending WHERE ownerSubject = :owner AND flightId = :flightId AND changeId = :changeId")
    suspend fun delete(owner: String, flightId: String, changeId: String)

    @Query("SELECT signatureFile FROM flight_departure_pending WHERE signatureFile IS NOT NULL UNION SELECT attemptSignatureFile FROM flight_departure_pending WHERE attemptSignatureFile IS NOT NULL")
    suspend fun signatureFiles(): List<String>

    @Query("DELETE FROM flight_departure_pending")
    suspend fun clear()
}
