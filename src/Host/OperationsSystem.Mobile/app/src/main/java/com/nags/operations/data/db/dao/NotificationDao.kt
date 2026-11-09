package com.nags.operations.data.db.dao

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import androidx.room.Transaction
import com.nags.operations.data.db.entities.NotificationEntity
import com.nags.operations.data.db.entities.NotificationCategoryReadEntity
import com.nags.operations.data.db.entities.NotificationUnreadStateEntity
import kotlinx.coroutines.flow.Flow

@Dao
interface NotificationDao {
    @Query("SELECT * FROM notifications WHERE recipientUserId = :recipientUserId AND isArchived = 0 ORDER BY createdAtUtc DESC")
    fun observeAll(recipientUserId: String): Flow<List<NotificationEntity>>

    @Query("SELECT COUNT(*) FROM notifications WHERE recipientUserId = :recipientUserId AND isRead = 0 AND isArchived = 0")
    fun observeUnreadCount(recipientUserId: String): Flow<Int>

    @Query("SELECT * FROM notification_unread_state WHERE recipientUserId = :recipientUserId")
    fun observeUnreadTotal(recipientUserId: String): Flow<NotificationUnreadStateEntity?>

    @Query("SELECT * FROM notification_unread_state WHERE recipientUserId = :recipientUserId")
    suspend fun unreadTotal(recipientUserId: String): NotificationUnreadStateEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertUnreadTotal(row: NotificationUnreadStateEntity)

    @Query("DELETE FROM notification_unread_state")
    suspend fun deleteUnreadTotals()

    @Query("SELECT * FROM notifications WHERE id = :id AND recipientUserId = :recipientUserId LIMIT 1")
    suspend fun getById(id: String, recipientUserId: String): NotificationEntity?

    @Query("SELECT * FROM notification_category_reads WHERE recipientUserId = :recipientUserId AND category = :category")
    suspend fun categoryRead(recipientUserId: String, category: String): NotificationCategoryReadEntity?

    @Query("SELECT * FROM notification_category_reads WHERE recipientUserId = :recipientUserId AND pending = 1")
    suspend fun pendingCategoryReads(recipientUserId: String): List<NotificationCategoryReadEntity>

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertCategoryRead(row: NotificationCategoryReadEntity)

    @Query("DELETE FROM notification_category_reads")
    suspend fun deleteCategoryReads()

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(row: NotificationEntity)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertAll(rows: List<NotificationEntity>)

    @Query("UPDATE notifications SET isRead = 1, readAtUtc = :readAtUtc WHERE id = :id AND recipientUserId = :recipientUserId")
    suspend fun markRead(id: String, recipientUserId: String, readAtUtc: String)

    @Query("UPDATE notifications SET isRead = 1, readAtUtc = COALESCE(readAtUtc, :readAtUtc) WHERE recipientUserId = :recipientUserId AND isArchived = 0 AND id IN (:ids)")
    suspend fun markReadSnapshot(recipientUserId: String, ids: List<String>, readAtUtc: String)

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
        WHERE recipientUserId = :recipientUserId AND id IN (:ids)
    """)
    suspend fun archiveSnapshot(recipientUserId: String, ids: List<String>)

    @Query("SELECT id FROM notifications WHERE recipientUserId = :recipientUserId AND isArchived = 1")
    suspend fun archivedIds(recipientUserId: String): List<String>

    @Query("DELETE FROM notifications WHERE recipientUserId = :recipientUserId AND isArchived = 0")
    suspend fun deleteVisible(recipientUserId: String)

    @Query("""
        DELETE FROM notifications WHERE recipientUserId = :recipientUserId AND isArchived = 0 AND
        ((:category = 'Invitations' AND kind = 'EmployeeInvitedToFlight') OR
         (:category = 'Reminders' AND kind = 'FlightReminder') OR
         (:category = 'FlightUpdates' AND kind NOT IN ('EmployeeInvitedToFlight', 'FlightReminder')))
    """)
    suspend fun deleteVisibleCategory(recipientUserId: String, category: String)

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
    suspend fun replaceAll(recipientUserId: String, rows: List<NotificationEntity>, category: String? = null) {
        val archived = archivedIds(recipientUserId).toHashSet()
        if (category == null) deleteVisible(recipientUserId) else deleteVisibleCategory(recipientUserId, category)
        upsertAll(rows.filterNot { it.id in archived })
    }

    @Transaction
    suspend fun queueCategoryRead(row: NotificationCategoryReadEntity, ids: List<String>, total: NotificationUnreadStateEntity) {
        markReadSnapshot(row.recipientUserId, ids, row.localReadAtUtc)
        upsertCategoryRead(row)
        upsertUnreadTotal(total)
    }

    @Transaction
    suspend fun completeCategoryRead(row: NotificationCategoryReadEntity, total: NotificationUnreadStateEntity) {
        upsertCategoryRead(row)
        upsertUnreadTotal(total)
    }

}
