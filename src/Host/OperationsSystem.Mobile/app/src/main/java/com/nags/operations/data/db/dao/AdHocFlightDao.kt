package com.nags.operations.data.db.dao

import androidx.room.Dao
import androidx.room.Query
import androidx.room.Upsert
import com.nags.operations.data.db.entities.AdHocFlightContextEntity
import com.nags.operations.data.db.entities.AdHocFlightPendingEntity
import kotlinx.coroutines.flow.Flow

@Dao
interface AdHocFlightDao {
    @Query("SELECT * FROM flight_adhoc_context WHERE ownerSubject = :owner") suspend fun context(owner: String): AdHocFlightContextEntity?
    @Query("SELECT * FROM flight_adhoc_context WHERE ownerSubject = :owner") fun observeContext(owner: String): Flow<AdHocFlightContextEntity?>
    @Upsert suspend fun putContext(row: AdHocFlightContextEntity)
    @Query("DELETE FROM flight_adhoc_context WHERE ownerSubject = :owner") suspend fun deleteContext(owner: String)
    @Query("SELECT * FROM flight_adhoc_pending WHERE ownerSubject = :owner ORDER BY savedAtUtc DESC") suspend fun rows(owner: String): List<AdHocFlightPendingEntity>
    @Query("SELECT * FROM flight_adhoc_pending WHERE ownerSubject = :owner ORDER BY savedAtUtc DESC") fun observe(owner: String): Flow<List<AdHocFlightPendingEntity>>
    @Upsert suspend fun put(row: AdHocFlightPendingEntity)
    @Query("DELETE FROM flight_adhoc_pending WHERE ownerSubject = :owner AND flightId = :id") suspend fun delete(owner: String, id: String)
    @Query("DELETE FROM flight_adhoc_pending") suspend fun clearRows()
    @Query("DELETE FROM flight_adhoc_context") suspend fun clearContexts()
}
