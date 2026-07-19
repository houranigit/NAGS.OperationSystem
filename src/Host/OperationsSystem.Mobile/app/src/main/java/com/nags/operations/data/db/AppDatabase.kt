package com.nags.operations.data.db

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase
import androidx.room.TypeConverters
import androidx.room.migration.Migration
import androidx.sqlite.db.SupportSQLiteDatabase
import com.nags.operations.data.db.dao.AdHocFlightDao
import com.nags.operations.data.db.dao.AircraftTypeDao
import com.nags.operations.data.db.dao.CustomerDao
import com.nags.operations.data.db.dao.EmployeeDao
import com.nags.operations.data.db.dao.FlightDao
import com.nags.operations.data.db.dao.GeneralSupportDao
import com.nags.operations.data.db.dao.MaterialDao
import com.nags.operations.data.db.dao.NotificationDao
import com.nags.operations.data.db.dao.PerLandingFlightDao
import com.nags.operations.data.db.dao.ServiceDao
import com.nags.operations.data.db.dao.SyncStateDao
import com.nags.operations.data.db.dao.WorkOrderDraftDao
import com.nags.operations.data.db.dao.WorkOrderOutboxDao
import com.nags.operations.data.db.dao.ToolDao
import com.nags.operations.data.db.entities.AdHocFlightEntity
import com.nags.operations.data.db.entities.AircraftTypeEntity
import com.nags.operations.data.db.entities.CustomerEntity
import com.nags.operations.data.db.entities.EmployeeEntity
import com.nags.operations.data.db.entities.FlightEntity
import com.nags.operations.data.db.entities.GeneralSupportEntity
import com.nags.operations.data.db.entities.MaterialEntity
import com.nags.operations.data.db.entities.NotificationEntity
import com.nags.operations.data.db.entities.PerLandingFlightEntity
import com.nags.operations.data.db.entities.ServiceEntity
import com.nags.operations.data.db.entities.SyncStateEntity
import com.nags.operations.data.db.entities.ToolEntity
import com.nags.operations.data.db.entities.WorkOrderDraftEntity
import com.nags.operations.data.db.entities.WorkOrderOutboxEntity

/**
 * On-device cache that mirrors the server's catalogs + station roster + the user's flight
 * lists. Everything the UI reads goes through this database — the API client only writes to
 * it (via the sync coordinator), never from a screen directly. That single rule is what
 * makes the app offline-first by construction.
 *
 * v11 is the v1.0.0 rewrite baseline: flight rows carry the new backend shape (planned
 * services, staff-member ids, string statuses, RowVersion) and the Per-Landing tab replaces
 * the legacy AOG tab. Upgrades from the legacy app (v ≤ 10) are destructive on purpose —
 * legacy cached rows and queued outbox payloads target the retired `/api/mobile/v2` contract
 * and cannot be replayed against the new API; caches repopulate on first sync.
 */
@Database(
    version = 14,
    exportSchema = true,
    entities = [
        ServiceEntity::class,
        ToolEntity::class,
        MaterialEntity::class,
        GeneralSupportEntity::class,
        CustomerEntity::class,
        AircraftTypeEntity::class,
        EmployeeEntity::class,
        FlightEntity::class,
        PerLandingFlightEntity::class,
        AdHocFlightEntity::class,
        SyncStateEntity::class,
        WorkOrderDraftEntity::class,
        WorkOrderOutboxEntity::class,
        NotificationEntity::class,
    ],
)
@TypeConverters(
    FlightServiceConverters::class,
    MyWorkOrderCacheConverters::class,
    FlightAssignedEmployeeConverters::class,
)
abstract class AppDatabase : RoomDatabase() {
    abstract fun serviceDao(): ServiceDao
    abstract fun toolDao(): ToolDao
    abstract fun materialDao(): MaterialDao
    abstract fun generalSupportDao(): GeneralSupportDao
    abstract fun customerDao(): CustomerDao
    abstract fun aircraftTypeDao(): AircraftTypeDao
    abstract fun employeeDao(): EmployeeDao
    abstract fun flightDao(): FlightDao
    abstract fun perLandingFlightDao(): PerLandingFlightDao
    abstract fun adHocFlightDao(): AdHocFlightDao
    abstract fun syncStateDao(): SyncStateDao
    abstract fun workOrderDraftDao(): WorkOrderDraftDao
    abstract fun workOrderOutboxDao(): WorkOrderOutboxDao
    abstract fun notificationDao(): NotificationDao

    companion object {
        @Volatile private var instance: AppDatabase? = null

        fun get(context: Context): AppDatabase {
            return instance ?: synchronized(this) {
                instance ?: Room
                    .databaseBuilder(context.applicationContext, AppDatabase::class.java, "operations.db")
                    // Only legacy schemas target the retired API contract. Starting with v11,
                    // every schema change must provide an explicit migration so pending field
                    // work can never be erased by an accidentally omitted migration.
                    .fallbackToDestructiveMigrationFrom(true, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10)
                    .addMigrations(MIGRATION_11_12, MIGRATION_12_13, MIGRATION_13_14)
                    .build()
                    .also { instance = it }
            }
        }

        private val MIGRATION_11_12 = object : Migration(11, 12) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL(
                    """
                    CREATE TABLE IF NOT EXISTS notifications (
                        id TEXT NOT NULL PRIMARY KEY,
                        recipientUserId TEXT,
                        kind TEXT NOT NULL,
                        titleEn TEXT NOT NULL,
                        bodyEn TEXT NOT NULL,
                        titleAr TEXT NOT NULL,
                        bodyAr TEXT NOT NULL,
                        payloadJson TEXT NOT NULL,
                        isRead INTEGER NOT NULL,
                        createdAtUtc TEXT NOT NULL,
                        readAtUtc TEXT
                    )
                    """.trimIndent(),
                )
            }
        }

        private val MIGRATION_12_13 = object : Migration(12, 13) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL(
                    "ALTER TABLE notifications ADD COLUMN isArchived INTEGER NOT NULL DEFAULT 0",
                )
            }
        }

        private val MIGRATION_13_14 = object : Migration(13, 14) {
            override fun migrate(db: SupportSQLiteDatabase) {
                // Fail closed until the next personalized catalog sync supplies the allowance set.
                db.execSQL(
                    "ALTER TABLE services ADD COLUMN isAllowedPerformedService INTEGER NOT NULL DEFAULT 0",
                )
            }
        }
    }
}
