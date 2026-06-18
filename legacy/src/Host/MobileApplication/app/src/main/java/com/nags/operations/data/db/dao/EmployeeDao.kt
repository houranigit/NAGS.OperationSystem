package com.nags.operations.data.db.dao

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import androidx.room.Transaction
import com.nags.operations.data.db.entities.EmployeeEntity
import kotlinx.coroutines.flow.Flow

@Dao
interface EmployeeDao {
    @Query("SELECT * FROM employees ORDER BY fullName COLLATE NOCASE")
    fun observeAll(): Flow<List<EmployeeEntity>>

    @Query("SELECT * FROM employees ORDER BY fullName COLLATE NOCASE")
    suspend fun snapshot(): List<EmployeeEntity>

    @Query("SELECT COUNT(*) FROM employees")
    suspend fun count(): Int

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(rows: List<EmployeeEntity>)

    @Query("DELETE FROM employees")
    suspend fun deleteAll()

    @Transaction
    suspend fun replaceAll(rows: List<EmployeeEntity>) {
        deleteAll()
        insertAll(rows)
    }
}
