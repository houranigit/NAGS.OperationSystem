package com.nags.operations.data.db.dao

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import androidx.room.Transaction
import com.nags.operations.data.db.entities.NotificationEntity
import kotlinx.coroutines.flow.Flow

@Dao
interface NotificationDao {
    @Query("SELECT * FROM notifications WHERE recipientUserId = :recipientUserId AND isArchived = 0 ORDER BY createdAtUtc DESC")
    fun observeAll(recipientUserId: String): Flow<List<NotificationEntity>>

    @Query("SELECT COUNT(*) FROM notifications WHERE recipientUserId = :recipientUserId AND isRead = 0 AND isArchived = 0")
    fun observeUnreadCount(recipientUserId: String): Flow<Int>

    @Query("SELECT * FROM notifications WHERE id = :id AND recipientUserId = :recipientUserId LIMIT 1")
    suspend fun getById(id: String, recipientUserId: String): NotificationEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(row: NotificationEntity)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertAll(rows: List<NotificationEntity>)

    @Query("UPDATE notifications SET isRead = 1, readAtUtc = :readAtUtc WHERE id = :id AND recipientUserId = :recipientUserId")
    suspend fun markRead(id: String, recipientUserId: String, readAtUtc: String)

    @Query("UPDATE notifications SET isRead = 1, readAtUtc = COALESCE(readAtUtc, :readAtUtc) WHERE recipientUserId = :recipientUserId AND isArchived = 0")
    suspend fun markAllRead(recipientUserId: String, readAtUtc: String)

    @Query("""
        UPDATE notifications SET
            isArchived = 1,
            isRead = 1,
            kind = '',
            titleEn = '', bodyEn = '',
            titleAr = '', bodyAr = '',
            payloadJson = '{}'
        WHERE id = :id AND recipientUserId = :recipientUserId
    """)
    suspend fun archive(id: String, recipientUserId: String)

    @Query("""
        UPDATE notifications SET
            isArchived = 1,
            isRead = 1,
            kind = '',
            titleEn = '', bodyEn = '',
            titleAr = '', bodyAr = '',
            payloadJson = '{}'
        WHERE recipientUserId = :recipientUserId
    """)
    suspend fun archiveAll(recipientUserId: String)

    @Query("SELECT id FROM notifications WHERE recipientUserId = :recipientUserId AND isArchived = 1")
    suspend fun archivedIds(recipientUserId: String): List<String>

    @Query("DELETE FROM notifications WHERE recipientUserId = :recipientUserId AND isArchived = 0")
    suspend fun deleteVisible(recipientUserId: String)

    @Query("""
        UPDATE notifications SET
            isRead = 1,
            kind = '',
            titleEn = '', bodyEn = '',
            titleAr = '', bodyAr = '',
            payloadJson = '{}'
        WHERE isArchived = 1
    """)
    suspend fun scrubArchivedTombstones()

    @Query("DELETE FROM notifications WHERE isArchived = 0")
    suspend fun deleteAllVisible()

    @Transaction
    suspend fun replaceAll(recipientUserId: String, rows: List<NotificationEntity>) {
        val archived = archivedIds(recipientUserId).toHashSet()
        deleteVisible(recipientUserId)
        upsertAll(rows.filterNot { it.id in archived })
    }
}
