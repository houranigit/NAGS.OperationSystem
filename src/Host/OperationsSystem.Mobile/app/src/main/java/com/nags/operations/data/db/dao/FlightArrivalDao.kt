package com.nags.operations.data.db.dao

import androidx.room.Dao
import androidx.room.Query
import androidx.room.Upsert
import com.nags.operations.data.db.entities.FlightArrivalPendingEntity
import kotlinx.coroutines.flow.Flow

@Dao
interface FlightArrivalDao {
    @Query("SELECT * FROM flight_arrival_pending WHERE ownerSubject = :owner ORDER BY savedAtUtc, flightId")
    fun observe(owner: String): Flow<List<FlightArrivalPendingEntity>>

    @Query("SELECT * FROM flight_arrival_pending WHERE ownerSubject = :owner ORDER BY savedAtUtc, flightId")
    suspend fun rows(owner: String): List<FlightArrivalPendingEntity>

    @Upsert
    suspend fun put(row: FlightArrivalPendingEntity)

    @Query("DELETE FROM flight_arrival_pending WHERE ownerSubject = :owner AND flightId = :flightId AND changeId = :changeId")
    suspend fun delete(owner: String, flightId: String, changeId: String)

    @Query("DELETE FROM flight_arrival_pending")
    suspend fun clear()
}
