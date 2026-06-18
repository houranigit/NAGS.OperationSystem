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
import com.nags.operations.data.db.dao.AogFlightDao
import com.nags.operations.data.db.dao.CustomerDao
import com.nags.operations.data.db.dao.EmployeeDao
import com.nags.operations.data.db.dao.FlightDao
import com.nags.operations.data.db.dao.GeneralSupportDao
import com.nags.operations.data.db.dao.MaterialDao
import com.nags.operations.data.db.dao.ServiceDao
import com.nags.operations.data.db.dao.SyncStateDao
import com.nags.operations.data.db.dao.WorkOrderDraftDao
import com.nags.operations.data.db.dao.WorkOrderOutboxDao
import com.nags.operations.data.db.dao.ToolDao
import com.nags.operations.data.db.entities.AdHocFlightEntity
import com.nags.operations.data.db.entities.AircraftTypeEntity
import com.nags.operations.data.db.entities.AogFlightEntity
import com.nags.operations.data.db.entities.CustomerEntity
import com.nags.operations.data.db.entities.EmployeeEntity
import com.nags.operations.data.db.entities.FlightEntity
import com.nags.operations.data.db.entities.GeneralSupportEntity
import com.nags.operations.data.db.entities.MaterialEntity
import com.nags.operations.data.db.entities.ServiceEntity
import com.nags.operations.data.db.entities.SyncStateEntity
import com.nags.operations.data.db.entities.ToolEntity
import com.nags.operations.data.db.entities.WorkOrderDraftEntity
import com.nags.operations.data.db.entities.WorkOrderOutboxEntity

/**
 * On-device cache that mirrors the server's catalog + station roster + the
 * user's flight list. Everything the UI reads goes through this database —
 * the API client only writes to it (via the sync coordinator), it's never
 * called from a screen directly. That single rule is what makes the app
 * offline-first by construction.
 *
 * Migrations are now real — destructive fallback would wipe outbox rows the
 * user hasn't been able to upload yet (e.g. queued while offline before a
 * schema bump), which is the one bit of "user-authored" state on the device.
 * Catalog rows that happen to drop on a future migration are still safe; the
 * next sync re-populates them.
 */
@Database(
    // v10 — adds `assignedEmployees` JSON column to `flights_my` (cached flight crew for the invite screen).
    // v9 — replaces per-flight myWorkOrderId / myWorkOrderStatus with embedded `myWorkOrder` JSON.
    // v8 — adds the outbox table (`work_order_outbox`) for offline work-order submissions.
    // v7 added local work-order drafts (`work_order_drafts`).
    version = 10,
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
        AogFlightEntity::class,
        AdHocFlightEntity::class,
        SyncStateEntity::class,
        WorkOrderDraftEntity::class,
        WorkOrderOutboxEntity::class,
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
    abstract fun aogFlightDao(): AogFlightDao
    abstract fun adHocFlightDao(): AdHocFlightDao
    abstract fun syncStateDao(): SyncStateDao
    abstract fun workOrderDraftDao(): WorkOrderDraftDao
    abstract fun workOrderOutboxDao(): WorkOrderOutboxDao

    companion object {
        @Volatile private var instance: AppDatabase? = null

        /**
         * v7 → v8: introduces `work_order_outbox`. Pure additive — no other tables
         * touched, so user drafts and cached flights survive the bump. Columns,
         * defaults, and indexes mirror [WorkOrderOutboxEntity] exactly so Room's
         * schema verification on first open passes without complaint.
         */
        private val MIGRATION_7_8 = object : Migration(7, 8) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL(
                    """
                    CREATE TABLE IF NOT EXISTS `work_order_outbox` (
                        `clientMutationId` TEXT NOT NULL,
                        `flightId` TEXT NOT NULL,
                        `flightKind` INTEGER NOT NULL,
                        `clientFlightId` TEXT,
                        `payloadJson` TEXT NOT NULL,
                        `attachmentsDir` TEXT,
                        `status` INTEGER NOT NULL,
                        `attempts` INTEGER NOT NULL,
                        `lastError` TEXT,
                        `createdAtEpochMs` INTEGER NOT NULL,
                        `updatedAtEpochMs` INTEGER NOT NULL,
                        `serverWorkOrderId` TEXT,
                        PRIMARY KEY(`clientMutationId`)
                    )
                    """.trimIndent(),
                )
                db.execSQL("CREATE INDEX IF NOT EXISTS `index_work_order_outbox_flightId` ON `work_order_outbox` (`flightId`)")
                db.execSQL("CREATE INDEX IF NOT EXISTS `index_work_order_outbox_status` ON `work_order_outbox` (`status`)")
                db.execSQL("CREATE INDEX IF NOT EXISTS `index_work_order_outbox_createdAtEpochMs` ON `work_order_outbox` (`createdAtEpochMs`)")
            }
        }

        /**
         * v8 → v9: flight tables drop `myWorkOrderId` / `myWorkOrderStatus` and add nullable
         * `myWorkOrder` JSON. Full work-order blobs repopulate on the next sync.
         */
        private val MIGRATION_8_9 = object : Migration(8, 9) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL(
                    """
                    CREATE TABLE IF NOT EXISTS `flights_my_new` (
                        `id` TEXT NOT NULL,
                        `flightNumber` TEXT NOT NULL,
                        `customerName` TEXT NOT NULL,
                        `customerIataCode` TEXT NOT NULL,
                        `stationCode` TEXT NOT NULL,
                        `operationTypeCode` TEXT NOT NULL,
                        `sta` TEXT NOT NULL,
                        `std` TEXT NOT NULL,
                        `aircraftModel` TEXT,
                        `status` INTEGER NOT NULL,
                        `canceledAt` TEXT,
                        `assignedEmployeesCount` INTEGER NOT NULL,
                        `myWorkOrder` TEXT,
                        `otherWorkOrdersExist` INTEGER NOT NULL,
                        `services` TEXT NOT NULL,
                        PRIMARY KEY(`id`)
                    )
                    """.trimIndent(),
                )
                db.execSQL(
                    """
                    INSERT INTO `flights_my_new` (`id`, `flightNumber`, `customerName`, `customerIataCode`, `stationCode`, `operationTypeCode`, `sta`, `std`, `aircraftModel`, `status`, `canceledAt`, `assignedEmployeesCount`, `myWorkOrder`, `otherWorkOrdersExist`, `services`)
                    SELECT `id`, `flightNumber`, `customerName`, `customerIataCode`, `stationCode`, `operationTypeCode`, `sta`, `std`, `aircraftModel`, `status`, `canceledAt`, `assignedEmployeesCount`, NULL, `otherWorkOrdersExist`, `services` FROM `flights_my`
                    """.trimIndent(),
                )
                db.execSQL("DROP TABLE `flights_my`")
                db.execSQL("ALTER TABLE `flights_my_new` RENAME TO `flights_my`")

                db.execSQL(
                    """
                    CREATE TABLE IF NOT EXISTS `flights_aog_new` (
                        `id` TEXT NOT NULL,
                        `flightNumber` TEXT NOT NULL,
                        `customerName` TEXT NOT NULL,
                        `customerIataCode` TEXT NOT NULL,
                        `stationCode` TEXT NOT NULL,
                        `operationTypeCode` TEXT NOT NULL,
                        `sta` TEXT NOT NULL,
                        `std` TEXT NOT NULL,
                        `aircraftModel` TEXT,
                        `status` INTEGER NOT NULL,
                        `canceledAt` TEXT,
                        `assignedEmployeesCount` INTEGER NOT NULL,
                        `myWorkOrder` TEXT,
                        `otherWorkOrdersExist` INTEGER NOT NULL,
                        PRIMARY KEY(`id`)
                    )
                    """.trimIndent(),
                )
                db.execSQL(
                    """
                    INSERT INTO `flights_aog_new` (`id`, `flightNumber`, `customerName`, `customerIataCode`, `stationCode`, `operationTypeCode`, `sta`, `std`, `aircraftModel`, `status`, `canceledAt`, `assignedEmployeesCount`, `myWorkOrder`, `otherWorkOrdersExist`)
                    SELECT `id`, `flightNumber`, `customerName`, `customerIataCode`, `stationCode`, `operationTypeCode`, `sta`, `std`, `aircraftModel`, `status`, `canceledAt`, `assignedEmployeesCount`, NULL, `otherWorkOrdersExist` FROM `flights_aog`
                    """.trimIndent(),
                )
                db.execSQL("DROP TABLE `flights_aog`")
                db.execSQL("ALTER TABLE `flights_aog_new` RENAME TO `flights_aog`")

                db.execSQL(
                    """
                    CREATE TABLE IF NOT EXISTS `flights_ad_hoc_new` (
                        `id` TEXT NOT NULL,
                        `flightNumber` TEXT NOT NULL,
                        `customerName` TEXT NOT NULL,
                        `customerIataCode` TEXT NOT NULL,
                        `stationCode` TEXT NOT NULL,
                        `operationTypeCode` TEXT NOT NULL,
                        `sta` TEXT NOT NULL,
                        `std` TEXT NOT NULL,
                        `aircraftModel` TEXT,
                        `status` INTEGER NOT NULL,
                        `canceledAt` TEXT,
                        `assignedEmployeesCount` INTEGER NOT NULL,
                        `myWorkOrder` TEXT,
                        `otherWorkOrdersExist` INTEGER NOT NULL,
                        PRIMARY KEY(`id`)
                    )
                    """.trimIndent(),
                )
                db.execSQL(
                    """
                    INSERT INTO `flights_ad_hoc_new` (`id`, `flightNumber`, `customerName`, `customerIataCode`, `stationCode`, `operationTypeCode`, `sta`, `std`, `aircraftModel`, `status`, `canceledAt`, `assignedEmployeesCount`, `myWorkOrder`, `otherWorkOrdersExist`)
                    SELECT `id`, `flightNumber`, `customerName`, `customerIataCode`, `stationCode`, `operationTypeCode`, `sta`, `std`, `aircraftModel`, `status`, `canceledAt`, `assignedEmployeesCount`, NULL, `otherWorkOrdersExist` FROM `flights_ad_hoc`
                    """.trimIndent(),
                )
                db.execSQL("DROP TABLE `flights_ad_hoc`")
                db.execSQL("ALTER TABLE `flights_ad_hoc_new` RENAME TO `flights_ad_hoc`")
            }
        }

        /**
         * v9 → v10: adds `assignedEmployees` (JSON) to `flights_my` only — cached flight crew
         * for the invite screen. Rebuilt rather than `ALTER ... ADD COLUMN ... DEFAULT` so the
         * column carries no SQLite default (the entity declares none); a stray DB default would
         * fail Room's schema validation on first open. Existing rows are seeded with an empty
         * JSON array and repopulate on the next sync. `flights_aog` / `flights_ad_hoc` are
         * untouched (no assigned list on those tabs).
         */
        private val MIGRATION_9_10 = object : Migration(9, 10) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL(
                    """
                    CREATE TABLE IF NOT EXISTS `flights_my_new` (
                        `id` TEXT NOT NULL,
                        `flightNumber` TEXT NOT NULL,
                        `customerName` TEXT NOT NULL,
                        `customerIataCode` TEXT NOT NULL,
                        `stationCode` TEXT NOT NULL,
                        `operationTypeCode` TEXT NOT NULL,
                        `sta` TEXT NOT NULL,
                        `std` TEXT NOT NULL,
                        `aircraftModel` TEXT,
                        `status` INTEGER NOT NULL,
                        `canceledAt` TEXT,
                        `assignedEmployeesCount` INTEGER NOT NULL,
                        `myWorkOrder` TEXT,
                        `otherWorkOrdersExist` INTEGER NOT NULL,
                        `services` TEXT NOT NULL,
                        `assignedEmployees` TEXT NOT NULL,
                        PRIMARY KEY(`id`)
                    )
                    """.trimIndent(),
                )
                db.execSQL(
                    """
                    INSERT INTO `flights_my_new` (`id`, `flightNumber`, `customerName`, `customerIataCode`, `stationCode`, `operationTypeCode`, `sta`, `std`, `aircraftModel`, `status`, `canceledAt`, `assignedEmployeesCount`, `myWorkOrder`, `otherWorkOrdersExist`, `services`, `assignedEmployees`)
                    SELECT `id`, `flightNumber`, `customerName`, `customerIataCode`, `stationCode`, `operationTypeCode`, `sta`, `std`, `aircraftModel`, `status`, `canceledAt`, `assignedEmployeesCount`, `myWorkOrder`, `otherWorkOrdersExist`, `services`, '[]' FROM `flights_my`
                    """.trimIndent(),
                )
                db.execSQL("DROP TABLE `flights_my`")
                db.execSQL("ALTER TABLE `flights_my_new` RENAME TO `flights_my`")
            }
        }

        fun get(context: Context): AppDatabase {
            return instance ?: synchronized(this) {
                instance ?: Room
                    .databaseBuilder(context.applicationContext, AppDatabase::class.java, "operations.db")
                    .addMigrations(MIGRATION_7_8, MIGRATION_8_9, MIGRATION_9_10)
                    .build()
                    .also { instance = it }
            }
        }
    }
}
