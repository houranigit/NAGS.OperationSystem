package com.nags.operations.data.db.dao

import androidx.room.Dao
import androidx.room.Query
import androidx.room.Transaction
import androidx.room.Upsert
import com.nags.operations.data.db.entities.FlightServicePendingEntity
import com.nags.operations.data.db.entities.FlightServiceFormDraftEntity
import kotlinx.coroutines.flow.Flow

@Dao
interface FlightServiceDao {
    @Query("SELECT * FROM flight_service_pending WHERE ownerSubject = :owner ORDER BY savedAtUtc, flightId, serviceRecordId")
    fun observe(owner: String): Flow<List<FlightServicePendingEntity>>
    @Query("SELECT * FROM flight_service_pending WHERE ownerSubject = :owner ORDER BY savedAtUtc, flightId, serviceRecordId")
    suspend fun rows(owner: String): List<FlightServicePendingEntity>
    @Query("SELECT * FROM flight_service_pending")
    suspend fun allRows(): List<FlightServicePendingEntity>
    @Query("SELECT * FROM flight_service_form_drafts WHERE ownerSubject = :owner ORDER BY savedAtUtc, flightId")
    fun observeDrafts(owner: String): Flow<List<FlightServiceFormDraftEntity>>
    @Query("SELECT * FROM flight_service_form_drafts WHERE ownerSubject = :owner")
    suspend fun drafts(owner: String): List<FlightServiceFormDraftEntity>
    @Query("SELECT * FROM flight_service_form_drafts")
    suspend fun allDrafts(): List<FlightServiceFormDraftEntity>
    @Upsert suspend fun putDraft(row: FlightServiceFormDraftEntity)
    @Upsert suspend fun put(row: FlightServicePendingEntity)
    @Query("DELETE FROM flight_service_pending WHERE ownerSubject = :owner AND flightId = :flightId AND serviceRecordId = :recordId AND changeId = :changeId")
    suspend fun delete(owner: String, flightId: String, recordId: String, changeId: String)
    @Query("DELETE FROM flight_service_pending WHERE ownerSubject = :owner AND flightId = :flightId")
    suspend fun deleteForFlight(owner: String, flightId: String)
    @Transaction
    suspend fun replaceForFlight(owner: String, flightId: String, rows: List<FlightServicePendingEntity>, draft: FlightServiceFormDraftEntity? = null) {
        deleteForFlight(owner, flightId)
        rows.forEach { put(it) }
        if (draft != null) putDraft(draft)
    }
    @Query("DELETE FROM flight_service_pending") suspend fun clear()
    @Query("DELETE FROM flight_service_form_drafts") suspend fun clearDrafts()
}
