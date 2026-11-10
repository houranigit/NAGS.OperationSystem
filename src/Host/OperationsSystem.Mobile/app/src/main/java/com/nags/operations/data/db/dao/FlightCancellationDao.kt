package com.nags.operations.data.db.dao

import androidx.room.Dao
import androidx.room.Query
import androidx.room.Upsert
import com.nags.operations.data.db.entities.FlightCancellationPendingEntity
import kotlinx.coroutines.flow.Flow

@Dao
interface FlightCancellationDao {
    @Query("SELECT * FROM flight_cancellation_pending WHERE ownerSubject = :owner ORDER BY savedAtUtc, flightId")
    fun observe(owner: String): Flow<List<FlightCancellationPendingEntity>>

    @Query("SELECT * FROM flight_cancellation_pending WHERE ownerSubject = :owner ORDER BY savedAtUtc, flightId")
    suspend fun rows(owner: String): List<FlightCancellationPendingEntity>

    @Upsert
    suspend fun put(row: FlightCancellationPendingEntity)

    @Query("DELETE FROM flight_cancellation_pending WHERE ownerSubject = :owner AND flightId = :flightId AND changeId = :changeId")
    suspend fun delete(owner: String, flightId: String, changeId: String)

    @Query("DELETE FROM flight_cancellation_pending")
    suspend fun clear()
}
