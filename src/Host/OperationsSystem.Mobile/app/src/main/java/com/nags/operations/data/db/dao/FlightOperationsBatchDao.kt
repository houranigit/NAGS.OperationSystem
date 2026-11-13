package com.nags.operations.data.db.dao

import androidx.room.Dao
import androidx.room.Query
import androidx.room.Upsert
import com.nags.operations.data.db.entities.FlightOperationsBatchEntity

@Dao
interface FlightOperationsBatchDao {
    @Query("SELECT * FROM flight_operations_batch WHERE ownerSubject = :owner")
    suspend fun rows(owner: String): List<FlightOperationsBatchEntity>
    @Query("SELECT * FROM flight_operations_batch")
    suspend fun allRows(): List<FlightOperationsBatchEntity>
    @Upsert suspend fun put(row: FlightOperationsBatchEntity)
    @Query("DELETE FROM flight_operations_batch WHERE ownerSubject = :owner AND flightId = :flightId")
    suspend fun delete(owner: String, flightId: String)
    @Query("DELETE FROM flight_operations_batch") suspend fun clear()
}
