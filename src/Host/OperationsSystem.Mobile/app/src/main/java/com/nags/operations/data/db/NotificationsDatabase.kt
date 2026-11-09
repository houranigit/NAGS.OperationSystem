package com.nags.operations.data.db

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase
import androidx.room.migration.Migration
import androidx.sqlite.db.SupportSQLiteDatabase
import com.nags.operations.data.db.dao.NotificationDao
import com.nags.operations.data.db.entities.NotificationEntity
import com.nags.operations.data.db.entities.NotificationCategoryReadEntity
import com.nags.operations.data.db.entities.NotificationUnreadStateEntity

/** Notification cache for the flight baseline. The former operations.db remains untouched. */
@Database(version = 3, exportSchema = true, entities = [NotificationEntity::class, NotificationCategoryReadEntity::class, NotificationUnreadStateEntity::class])
abstract class NotificationsDatabase : RoomDatabase() {
    abstract fun notificationDao(): NotificationDao

    companion object {
        val MIGRATION_2_3 = object : Migration(2, 3) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL("ALTER TABLE notification_unread_state ADD COLUMN invitations INTEGER")
                db.execSQL("ALTER TABLE notification_unread_state ADD COLUMN reminders INTEGER")
                db.execSQL("ALTER TABLE notification_unread_state ADD COLUMN flightUpdates INTEGER")
            }
        }
        val MIGRATION_1_2 = object : Migration(1, 2) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL("""
                    CREATE TABLE IF NOT EXISTS notification_category_reads (
                        recipientUserId TEXT NOT NULL,
                        category TEXT NOT NULL,
                        throughCreatedAtUtc TEXT NOT NULL,
                        localReadAtUtc TEXT NOT NULL,
                        pending INTEGER NOT NULL,
                        unreadDelta INTEGER NOT NULL,
                        serverReadAtUtc TEXT,
                        PRIMARY KEY (recipientUserId, category)
                    )
                """.trimIndent())
                db.execSQL("CREATE TABLE IF NOT EXISTS notification_unread_state (recipientUserId TEXT NOT NULL PRIMARY KEY, count INTEGER NOT NULL)")
            }
        }
        @Volatile private var instance: NotificationsDatabase? = null

        fun get(context: Context): NotificationsDatabase = instance ?: synchronized(this) {
            instance ?: Room.databaseBuilder(
                context.applicationContext,
                NotificationsDatabase::class.java,
                "operations-baseline.db",
            ).addMigrations(MIGRATION_1_2, MIGRATION_2_3).build().also { instance = it }
        }
    }
}
