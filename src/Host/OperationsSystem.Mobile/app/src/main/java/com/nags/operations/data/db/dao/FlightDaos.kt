package com.nags.operations.data.db.dao

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import androidx.room.Transaction
import com.nags.operations.data.db.entities.AdHocFlightEntity
import com.nags.operations.data.db.entities.FlightEntity
import com.nags.operations.data.db.entities.PerLandingFlightEntity
import kotlinx.coroutines.flow.Flow

/**
 * Flights the user is rostered on (non-Per-Landing). Ordered by STA ascending — same order
 * the server returns and the UI expects.
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

/** Per-Landing flights at the user's station (visible station-wide, assigned or not). */
@Dao
interface PerLandingFlightDao {
    @Query("SELECT * FROM flights_per_landing ORDER BY sta")
    fun observeAll(): Flow<List<PerLandingFlightEntity>>

    @Query("SELECT * FROM flights_per_landing WHERE id = :id LIMIT 1")
    suspend fun getById(id: String): PerLandingFlightEntity?

    @Query("SELECT COUNT(*) FROM flights_per_landing")
    suspend fun count(): Int

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(rows: List<PerLandingFlightEntity>)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(row: PerLandingFlightEntity)

    @Query("DELETE FROM flights_per_landing WHERE id = :id")
    suspend fun deleteById(id: String)

    @Query("DELETE FROM flights_per_landing")
    suspend fun deleteAll()

    @Transaction
    suspend fun replaceAll(rows: List<PerLandingFlightEntity>) {
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
