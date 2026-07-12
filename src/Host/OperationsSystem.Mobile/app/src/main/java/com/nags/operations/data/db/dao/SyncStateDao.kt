package com.nags.operations.data.db.dao

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import com.nags.operations.data.db.entities.SyncStateEntity
import kotlinx.coroutines.flow.Flow

@Dao
interface SyncStateDao {
    /** Reactive snapshot of every table's sync metadata — drives the diagnostics screen. */
    @Query("SELECT * FROM sync_state")
    fun observeAll(): Flow<List<SyncStateEntity>>

    @Query("SELECT * FROM sync_state WHERE tableName = :tableName")
    suspend fun get(tableName: String): SyncStateEntity?

    /**
     * Non-reactive snapshot used by realtime catch-up — we only need the
     * cursors once per (re)connect, so a one-shot read avoids spinning up a
     * Flow collector just to take its first emission.
     */
    @Query("SELECT * FROM sync_state")
    suspend fun snapshot(): List<SyncStateEntity>

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(row: SyncStateEntity)

    /**
     * Update only the cursor — keeps `lastSyncedAt` / `lastDurationMs` /
     * `lastError` untouched, so applying a live event doesn't lie about when
     * the last full per-table sync ran. Returns 0 when the row doesn't exist
     * yet (which is fine — the realtime channel will recreate it on the next
     * batched sync).
     */
    @Query("UPDATE sync_state SET cursor = :cursor WHERE tableName = :tableName")
    suspend fun updateCursor(tableName: String, cursor: String?): Int

    @Query("DELETE FROM sync_state")
    suspend fun deleteAll()
}
