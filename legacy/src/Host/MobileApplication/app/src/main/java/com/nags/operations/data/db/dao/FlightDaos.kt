package com.nags.operations.data.db.dao

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import androidx.room.Transaction
import com.nags.operations.data.db.entities.AdHocFlightEntity
import com.nags.operations.data.db.entities.AogFlightEntity
import com.nags.operations.data.db.entities.FlightEntity
import kotlinx.coroutines.flow.Flow

/**
 * Flights you're assigned to (non-AOG). Ordered by STA ascending — same order the
 * server returns and the operations UI expects.
 */
@Dao
interface FlightDao {
    @Query("SELECT * FROM flights_my ORDER BY sta")
    fun observeAll(): Flow<List<FlightEntity>>

    @Query("SELECT * FROM flights_my WHERE id = :id LIMIT 1")
    suspend fun getById(id: String): FlightEntity?

    /** Reactive single-row read — drives the invite screen so its lists react to sync. */
    @Query("SELECT * FROM flights_my WHERE id = :id LIMIT 1")
    fun observeById(id: String): Flow<FlightEntity?>

    @Query("SELECT COUNT(*) FROM flights_my")
    suspend fun count(): Int

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(rows: List<FlightEntity>)

    /** Single-row upsert path used by the real-time sync (one envelope = one row). */
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(row: FlightEntity)

    /** Drops one row by primary key — used when an envelope's op is `delete`. */
    @Query("DELETE FROM flights_my WHERE id = :id")
    suspend fun deleteById(id: String)

    @Query("DELETE FROM flights_my")
    suspend fun deleteAll()

    @Transaction
    suspend fun replaceAll(rows: List<FlightEntity>) {
        deleteAll()
        insertAll(rows)
    }
}

/** AOG flights at the user's station (whether they're assigned or not). */
@Dao
interface AogFlightDao {
    @Query("SELECT * FROM flights_aog ORDER BY sta")
    fun observeAll(): Flow<List<AogFlightEntity>>

    @Query("SELECT * FROM flights_aog WHERE id = :id LIMIT 1")
    suspend fun getById(id: String): AogFlightEntity?

    @Query("SELECT COUNT(*) FROM flights_aog")
    suspend fun count(): Int

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(rows: List<AogFlightEntity>)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(row: AogFlightEntity)

    @Query("DELETE FROM flights_aog WHERE id = :id")
    suspend fun deleteById(id: String)

    @Query("DELETE FROM flights_aog")
    suspend fun deleteAll()

    @Transaction
    suspend fun replaceAll(rows: List<AogFlightEntity>) {
        deleteAll()
        insertAll(rows)
    }
}

/** Ad Hoc flights at the user's station. */
@Dao
interface AdHocFlightDao {
    @Query("SELECT * FROM flights_ad_hoc ORDER BY sta")
    fun observeAll(): Flow<List<AdHocFlightEntity>>

    @Query("SELECT * FROM flights_ad_hoc WHERE id = :id LIMIT 1")
    suspend fun getById(id: String): AdHocFlightEntity?

    @Query("SELECT COUNT(*) FROM flights_ad_hoc")
    suspend fun count(): Int

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(rows: List<AdHocFlightEntity>)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(row: AdHocFlightEntity)

    @Query("DELETE FROM flights_ad_hoc WHERE id = :id")
    suspend fun deleteById(id: String)

    @Query("DELETE FROM flights_ad_hoc")
    suspend fun deleteAll()

    @Transaction
    suspend fun replaceAll(rows: List<AdHocFlightEntity>) {
        deleteAll()
        insertAll(rows)
    }
}
