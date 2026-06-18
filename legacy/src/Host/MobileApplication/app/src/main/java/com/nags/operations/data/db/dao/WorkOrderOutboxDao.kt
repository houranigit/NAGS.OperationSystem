package com.nags.operations.data.db.dao

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import com.nags.operations.data.db.entities.WorkOrderOutboxEntity
import kotlinx.coroutines.flow.Flow

/**
 * Reads and writes for the mobile work-order outbox. The DAO is intentionally
 * small — the repository ([com.nags.operations.data.repo.WorkOrderOutboxRepository])
 * owns all attachment-file side effects so this layer can stay a pure SQL surface.
 */
@Dao
interface WorkOrderOutboxDao {
    /** All rows newest-first; used by the Sync Center and as the basis for overlay flows. */
    @Query("SELECT * FROM work_order_outbox ORDER BY createdAtEpochMs DESC")
    fun observeAll(): Flow<List<WorkOrderOutboxEntity>>

    /**
     * Rows in [STATUS_PENDING][com.nags.operations.data.db.entities.WorkOrderOutboxEntity.Companion.STATUS_PENDING]
     * the worker should drain. Older rows first so retries respect the user's intent order.
     */
    @Query(
        """
        SELECT * FROM work_order_outbox
        WHERE status = 0
        ORDER BY createdAtEpochMs ASC
        """,
    )
    suspend fun listPendingFifo(): List<WorkOrderOutboxEntity>

    /**
     * Cheap "is there anything to drain?" gate the worker can `combine` with
     * connectivity + sign-in. Emits `true` while at least one
     * [STATUS_PENDING][com.nags.operations.data.db.entities.WorkOrderOutboxEntity.Companion.STATUS_PENDING]
     * row exists.
     */
    @Query("SELECT EXISTS(SELECT 1 FROM work_order_outbox WHERE status = 0)")
    fun observeHasPending(): Flow<Boolean>

    @Query("SELECT * FROM work_order_outbox WHERE clientMutationId = :id LIMIT 1")
    suspend fun getById(id: String): WorkOrderOutboxEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(entity: WorkOrderOutboxEntity)

    @Query("DELETE FROM work_order_outbox WHERE clientMutationId = :id")
    suspend fun deleteById(id: String)

    @Query("DELETE FROM work_order_outbox")
    suspend fun deleteAll()

    /**
     * Flips any row that was left in
     * [STATUS_SENDING][com.nags.operations.data.db.entities.WorkOrderOutboxEntity.Companion.STATUS_SENDING]
     * back to
     * [STATUS_PENDING][com.nags.operations.data.db.entities.WorkOrderOutboxEntity.Companion.STATUS_PENDING].
     * Used by the worker on (re)start: a previous run can leave a row marked
     * Sending if the process was killed mid-POST, or if the connectivity gate
     * flipped while a submission was in flight. Without this, the orphaned row
     * sits visible as "Syncing…" indefinitely.
     *
     * Increments `attempts` so the backoff schedule still treats the row as a
     * retry rather than fresh work — keeps a wedged endpoint from being
     * hammered every time the user reopens the app.
     */
    @Query(
        """
        UPDATE work_order_outbox
           SET status = 0,
               attempts = attempts + 1,
               updatedAtEpochMs = :now
         WHERE status = 1
        """,
    )
    suspend fun recoverInterruptedSends(now: Long): Int

    /**
     * Inline status / error / attempt update — avoids reading the row back and
     * keeps the work-order outbox single-statement.
     */
    @Query(
        """
        UPDATE work_order_outbox
           SET status = :status,
               attempts = :attempts,
               lastError = :lastError,
               serverWorkOrderId = :serverWorkOrderId,
               updatedAtEpochMs = :updatedAtEpochMs
         WHERE clientMutationId = :id
        """,
    )
    suspend fun updateStatus(
        id: String,
        status: Int,
        attempts: Int,
        lastError: String?,
        serverWorkOrderId: String?,
        updatedAtEpochMs: Long,
    )
}
