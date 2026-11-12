package com.nags.operations.data.db.dao

import androidx.room.Dao
import androidx.room.Query
import androidx.room.Transaction
import androidx.room.Upsert
import com.nags.operations.data.db.entities.FlightTaskPendingEntity
import kotlinx.coroutines.flow.Flow

@Dao
interface FlightTaskDao {
    @Query("SELECT * FROM flight_task_pending WHERE ownerSubject = :owner ORDER BY savedAtUtc, flightId, taskRecordId")
    fun observe(owner: String): Flow<List<FlightTaskPendingEntity>>
    @Query("SELECT * FROM flight_task_pending WHERE ownerSubject = :owner ORDER BY savedAtUtc, flightId, taskRecordId")
    suspend fun rows(owner: String): List<FlightTaskPendingEntity>
    @Query("SELECT * FROM flight_task_pending")
    suspend fun allRows(): List<FlightTaskPendingEntity>
    @Upsert suspend fun put(row: FlightTaskPendingEntity)
    @Query("DELETE FROM flight_task_pending WHERE ownerSubject = :owner AND flightId = :flightId AND taskRecordId = :recordId AND changeId = :changeId")
    suspend fun delete(owner: String, flightId: String, recordId: String, changeId: String)
    @Query("DELETE FROM flight_task_pending WHERE ownerSubject = :owner AND flightId = :flightId")
    suspend fun deleteForFlight(owner: String, flightId: String)
    @Transaction
    suspend fun replaceForFlight(owner: String, flightId: String, rows: List<FlightTaskPendingEntity>) {
        deleteForFlight(owner, flightId)
        rows.forEach { put(it) }
    }
    @Query("DELETE FROM flight_task_pending") suspend fun clear()
}
