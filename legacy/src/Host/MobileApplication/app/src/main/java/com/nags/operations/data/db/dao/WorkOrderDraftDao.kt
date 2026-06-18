package com.nags.operations.data.db.dao

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import com.nags.operations.data.db.entities.WorkOrderDraftEntity
import kotlinx.coroutines.flow.Flow

@Dao
interface WorkOrderDraftDao {
    @Query("SELECT * FROM work_order_drafts ORDER BY updatedAtEpochMs DESC")
    fun observeAll(): Flow<List<WorkOrderDraftEntity>>

    @Query("SELECT * FROM work_order_drafts WHERE draftId = :id LIMIT 1")
    suspend fun getById(id: String): WorkOrderDraftEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(entity: WorkOrderDraftEntity)

    @Query("DELETE FROM work_order_drafts WHERE draftId = :id")
    suspend fun deleteById(id: String)

    @Query("DELETE FROM work_order_drafts")
    suspend fun deleteAll()
}
