package com.nags.operations.data.db.dao

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import androidx.room.Transaction
import com.nags.operations.data.db.entities.AircraftTypeEntity
import com.nags.operations.data.db.entities.CustomerEntity
import com.nags.operations.data.db.entities.GeneralSupportEntity
import com.nags.operations.data.db.entities.MaterialEntity
import com.nags.operations.data.db.entities.ServiceEntity
import com.nags.operations.data.db.entities.ToolEntity
import kotlinx.coroutines.flow.Flow

/**
 * One DAO per catalog table. Each one supports:
 *   • [observeAll] — reactive list for the UI (Room re-emits on any change)
 *   • [count] — one-shot count for the diagnostics screen
 *   • [replaceAll] — atomic delete+insert used by the sync coordinator
 *
 * `replaceAll` is annotated `@Transaction` so the table is never seen empty by
 * a concurrent reader between the delete and the insert.
 */

@Dao
interface ServiceDao {
    @Query("SELECT * FROM services ORDER BY name COLLATE NOCASE")
    fun observeAll(): Flow<List<ServiceEntity>>

    @Query("SELECT * FROM services ORDER BY name COLLATE NOCASE")
    suspend fun snapshot(): List<ServiceEntity>

    @Query("SELECT COUNT(*) FROM services")
    suspend fun count(): Int

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(rows: List<ServiceEntity>)

    @Query("DELETE FROM services")
    suspend fun deleteAll()

    @Transaction
    suspend fun replaceAll(rows: List<ServiceEntity>) {
        deleteAll()
        insertAll(rows)
    }
}

@Dao
interface ToolDao {
    @Query("SELECT * FROM tools ORDER BY name COLLATE NOCASE")
    fun observeAll(): Flow<List<ToolEntity>>

    @Query("SELECT COUNT(*) FROM tools")
    suspend fun count(): Int

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(rows: List<ToolEntity>)

    @Query("DELETE FROM tools")
    suspend fun deleteAll()

    @Transaction
    suspend fun replaceAll(rows: List<ToolEntity>) {
        deleteAll()
        insertAll(rows)
    }
}

@Dao
interface MaterialDao {
    @Query("SELECT * FROM materials ORDER BY name COLLATE NOCASE")
    fun observeAll(): Flow<List<MaterialEntity>>

    @Query("SELECT COUNT(*) FROM materials")
    suspend fun count(): Int

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(rows: List<MaterialEntity>)

    @Query("DELETE FROM materials")
    suspend fun deleteAll()

    @Transaction
    suspend fun replaceAll(rows: List<MaterialEntity>) {
        deleteAll()
        insertAll(rows)
    }
}

@Dao
interface GeneralSupportDao {
    @Query("SELECT * FROM general_supports ORDER BY name COLLATE NOCASE")
    fun observeAll(): Flow<List<GeneralSupportEntity>>

    @Query("SELECT COUNT(*) FROM general_supports")
    suspend fun count(): Int

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(rows: List<GeneralSupportEntity>)

    @Query("DELETE FROM general_supports")
    suspend fun deleteAll()

    @Transaction
    suspend fun replaceAll(rows: List<GeneralSupportEntity>) {
        deleteAll()
        insertAll(rows)
    }
}

@Dao
interface CustomerDao {
    @Query("SELECT * FROM customers ORDER BY name COLLATE NOCASE")
    fun observeAll(): Flow<List<CustomerEntity>>

    @Query("SELECT COUNT(*) FROM customers")
    suspend fun count(): Int

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(rows: List<CustomerEntity>)

    @Query("DELETE FROM customers")
    suspend fun deleteAll()

    @Transaction
    suspend fun replaceAll(rows: List<CustomerEntity>) {
        deleteAll()
        insertAll(rows)
    }
}

@Dao
interface AircraftTypeDao {
    @Query("SELECT * FROM aircraft_types ORDER BY model COLLATE NOCASE")
    fun observeAll(): Flow<List<AircraftTypeEntity>>

    @Query("SELECT COUNT(*) FROM aircraft_types")
    suspend fun count(): Int

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(rows: List<AircraftTypeEntity>)

    @Query("DELETE FROM aircraft_types")
    suspend fun deleteAll()

    @Transaction
    suspend fun replaceAll(rows: List<AircraftTypeEntity>) {
        deleteAll()
        insertAll(rows)
    }
}
