package com.nags.operations.data.db.entities

import androidx.room.Entity
import androidx.room.PrimaryKey

/**
 * Per-table sync metadata: one row per logical table the mobile sync owns.
 * Powers the Sync Center diagnostics screen ("Services — 137 rows, synced
 * 12 s ago, last duration 240 ms").
 *
 * `lastSyncedAt` is the epoch-millis of the most recent successful sync; null
 * means the table has never been synced. `lastError` is set whenever a sync
 * fails — the table data itself is left untouched so the user keeps the
 * previous snapshot to use offline.
 *
 * `cursor` is the ISO-8601 UTC timestamp of the freshest change the mobile
 * has applied for this table. Updated by the real-time channel on every
 * successful envelope apply, and passed back to the server on reconnect via
 * `GET /api/mobile/v2/sync/changes?since=...` so the catch-up endpoint can
 * replay everything we missed while offline.
 */
@Entity(tableName = "sync_state")
data class SyncStateEntity(
    @PrimaryKey val tableName: String,
    val lastSyncedAt: Long?,
    val lastDurationMs: Long?,
    val lastError: String?,
    val cursor: String? = null,
)
