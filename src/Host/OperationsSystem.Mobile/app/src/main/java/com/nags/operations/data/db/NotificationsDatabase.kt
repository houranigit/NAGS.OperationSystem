package com.nags.operations.data.db

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase
import com.nags.operations.data.db.dao.NotificationDao
import com.nags.operations.data.db.entities.NotificationEntity

/** Notification cache for the flight baseline. The former operations.db remains untouched. */
@Database(version = 1, exportSchema = true, entities = [NotificationEntity::class])
abstract class NotificationsDatabase : RoomDatabase() {
    abstract fun notificationDao(): NotificationDao

    companion object {
        @Volatile private var instance: NotificationsDatabase? = null

        fun get(context: Context): NotificationsDatabase = instance ?: synchronized(this) {
            instance ?: Room.databaseBuilder(
                context.applicationContext,
                NotificationsDatabase::class.java,
                "operations-baseline.db",
            ).build().also { instance = it }
        }
    }
}
