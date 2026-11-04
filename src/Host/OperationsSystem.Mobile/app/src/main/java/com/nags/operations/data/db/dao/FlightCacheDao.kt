package com.nags.operations.data.db.dao

import androidx.room.Dao
import androidx.room.Query
import androidx.room.Transaction
import androidx.room.Upsert
import com.nags.operations.data.db.entities.FlightDetailCacheEntity
import com.nags.operations.data.db.entities.FlightSnapshotEntity
import kotlinx.coroutines.flow.Flow

@Dao
interface FlightCacheDao {
    @Query("SELECT * FROM flight_snapshots WHERE ownerSubject = :ownerSubject")
    fun observe(ownerSubject: String): Flow<FlightSnapshotEntity?>

    @Query("SELECT * FROM flight_snapshots WHERE ownerSubject = :ownerSubject")
    suspend fun snapshot(ownerSubject: String): FlightSnapshotEntity?

    @Query("SELECT * FROM flight_detail_cache WHERE ownerSubject = :ownerSubject")
    suspend fun details(ownerSubject: String): List<FlightDetailCacheEntity>

    @Query("SELECT * FROM flight_detail_cache WHERE ownerSubject = :ownerSubject AND flightId = :flightId")
    suspend fun detail(ownerSubject: String, flightId: String): FlightDetailCacheEntity?

    @Upsert
    suspend fun putSnapshot(snapshot: FlightSnapshotEntity)

    @Upsert
    suspend fun putDetails(details: List<FlightDetailCacheEntity>)

    @Query("DELETE FROM flight_detail_cache WHERE ownerSubject = :ownerSubject")
    suspend fun deleteDetails(ownerSubject: String)

    @Query("SELECT EXISTS(SELECT 1 FROM flight_snapshots) OR EXISTS(SELECT 1 FROM flight_detail_cache)")
    suspend fun hasAnyCache(): Boolean

    @Query("DELETE FROM flight_snapshots")
    suspend fun deleteSnapshots()

    @Query("DELETE FROM flight_detail_cache")
    suspend fun deleteAllDetails()

    @Transaction
    suspend fun replace(snapshot: FlightSnapshotEntity, details: List<FlightDetailCacheEntity>) {
        putSnapshot(snapshot)
        deleteDetails(snapshot.ownerSubject)
        if (details.isNotEmpty()) putDetails(details)
    }

    @Transaction
    suspend fun clearAll() {
        deleteSnapshots()
        deleteAllDetails()
    }
}
