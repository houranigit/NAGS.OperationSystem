package com.nags.operations.data.db.dao

import androidx.room.Dao
import androidx.room.Query
import androidx.room.Upsert
import com.nags.operations.data.db.entities.FlightReturnToRampPendingEntity
import kotlinx.coroutines.flow.Flow

@Dao
interface FlightReturnToRampDao {
    @Query("SELECT * FROM flight_rtr_pending WHERE ownerSubject = :owner ORDER BY savedAtUtc, flightId, occurrenceId")
    fun observe(owner: String): Flow<List<FlightReturnToRampPendingEntity>>
    @Query("SELECT * FROM flight_rtr_pending WHERE ownerSubject = :owner ORDER BY savedAtUtc, flightId, occurrenceId")
    suspend fun rows(owner: String): List<FlightReturnToRampPendingEntity>
    @Query("SELECT * FROM flight_rtr_pending") suspend fun allRows(): List<FlightReturnToRampPendingEntity>
    @Upsert suspend fun put(row: FlightReturnToRampPendingEntity)
    @Query("DELETE FROM flight_rtr_pending WHERE ownerSubject = :owner AND flightId = :flightId AND occurrenceId = :id AND changeId = :changeId")
    suspend fun delete(owner: String, flightId: String, id: String, changeId: String)
    @Query("DELETE FROM flight_rtr_pending") suspend fun clear()
}
