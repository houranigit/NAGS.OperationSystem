package com.nags.operations.data.db

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase
import androidx.room.migration.Migration
import androidx.sqlite.db.SupportSQLiteDatabase
import com.nags.operations.data.db.dao.FlightCacheDao
import com.nags.operations.data.db.dao.FlightCancellationDao
import com.nags.operations.data.db.dao.FlightTaskDao
import com.nags.operations.data.db.entities.FlightTaskPendingEntity
import com.nags.operations.data.db.dao.FlightServiceDao
import com.nags.operations.data.db.entities.FlightServicePendingEntity
import com.nags.operations.data.db.entities.FlightServiceFormDraftEntity
import com.nags.operations.data.db.entities.FlightCancellationPendingEntity
import com.nags.operations.data.db.dao.FlightDepartureDao
import com.nags.operations.data.db.entities.FlightDeparturePendingEntity
import com.nags.operations.data.db.dao.FlightArrivalDao
import com.nags.operations.data.db.entities.FlightArrivalPendingEntity
import com.nags.operations.data.db.dao.SharedCatalogDao
import com.nags.operations.data.db.entities.FlightDetailCacheEntity
import com.nags.operations.data.db.entities.FlightSnapshotEntity
import com.nags.operations.data.db.entities.SharedCatalogSnapshotEntity

/** Mobile caches and operational intent remain separate from notifications and the untouched legacy database. */
@Database(version = 10, exportSchema = true, entities = [
    SharedCatalogSnapshotEntity::class, FlightSnapshotEntity::class, FlightDetailCacheEntity::class, FlightArrivalPendingEntity::class, FlightDeparturePendingEntity::class, FlightCancellationPendingEntity::class, FlightServicePendingEntity::class, FlightServiceFormDraftEntity::class, FlightTaskPendingEntity::class,
])
abstract class CatalogDatabase : RoomDatabase() {
    abstract fun sharedCatalogDao(): SharedCatalogDao
    abstract fun flightCacheDao(): FlightCacheDao
    abstract fun flightArrivalDao(): FlightArrivalDao
    abstract fun flightDepartureDao(): FlightDepartureDao
    abstract fun flightCancellationDao(): FlightCancellationDao
    abstract fun flightServiceDao(): FlightServiceDao
    abstract fun flightTaskDao(): FlightTaskDao

    companion object {
        @Volatile private var instance: CatalogDatabase? = null

        private val MIGRATION_1_2 = object : Migration(1, 2) {
            override fun migrate(db: SupportSQLiteDatabase) {
                // Existing offline snapshots and their successful sync metadata remain intact.
                db.execSQL("ALTER TABLE shared_catalog_snapshots ADD COLUMN lastDurationMs INTEGER")
            }
        }

        private val MIGRATION_2_3 = object : Migration(2, 3) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL("CREATE TABLE IF NOT EXISTS flight_snapshots (ownerSubject TEXT NOT NULL PRIMARY KEY, payloadJson TEXT NOT NULL, itemCount INTEGER NOT NULL, lastAttemptUtc TEXT, lastSuccessUtc TEXT, error TEXT, permissionDenied INTEGER NOT NULL, lastDurationMs INTEGER)")
                db.execSQL("CREATE TABLE IF NOT EXISTS flight_detail_cache (ownerSubject TEXT NOT NULL, flightId TEXT NOT NULL, payloadJson TEXT NOT NULL, PRIMARY KEY(ownerSubject, flightId))")
            }
        }

        private val MIGRATION_3_4 = object : Migration(3, 4) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL("CREATE TABLE IF NOT EXISTS flight_arrival_pending (ownerSubject TEXT NOT NULL, flightId TEXT NOT NULL, flightNumber TEXT NOT NULL, changeId TEXT NOT NULL, inputJson TEXT NOT NULL, rowVersion TEXT NOT NULL, savedAtUtc TEXT NOT NULL, status TEXT NOT NULL, error TEXT, PRIMARY KEY(ownerSubject, flightId))")
            }
        }

        private val MIGRATION_4_5 = object : Migration(4, 5) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL("ALTER TABLE flight_arrival_pending ADD COLUMN attemptInputJson TEXT")
                // Older clients may already have sent these rows and lost the response.
                db.execSQL("UPDATE flight_arrival_pending SET attemptInputJson = inputJson WHERE status = 'pending'")
            }
        }

        private val MIGRATION_5_6 = object : Migration(5, 6) {
            override fun migrate(db: SupportSQLiteDatabase) {
                // Arrival intent and replaceable caches are retained unchanged.
                db.execSQL("CREATE TABLE IF NOT EXISTS flight_departure_pending (ownerSubject TEXT NOT NULL, flightId TEXT NOT NULL, flightNumber TEXT NOT NULL, staffMemberId TEXT NOT NULL, changeId TEXT NOT NULL, inputJson TEXT NOT NULL, rowVersion TEXT NOT NULL, reviewedDepartureJson TEXT NOT NULL, savedAtUtc TEXT NOT NULL, signatureFile TEXT, needsDepartureComparison INTEGER NOT NULL, status TEXT NOT NULL, error TEXT, attemptInputJson TEXT, attemptRowVersion TEXT, attemptSignatureFile TEXT, PRIMARY KEY(ownerSubject, flightId))")
            }
        }

        private val MIGRATION_6_7 = object : Migration(6, 7) {
            override fun migrate(db: SupportSQLiteDatabase) {
                // Existing arrival/departure intent and signature ownership remain intact.
                db.execSQL("CREATE TABLE IF NOT EXISTS flight_cancellation_pending (ownerSubject TEXT NOT NULL, flightId TEXT NOT NULL, flightNumber TEXT NOT NULL, staffMemberId TEXT NOT NULL, changeId TEXT NOT NULL, operation TEXT NOT NULL, inputJson TEXT, submissionId TEXT, rowVersion TEXT NOT NULL, savedAtUtc TEXT NOT NULL, status TEXT NOT NULL, error TEXT, attemptOperation TEXT, attemptInputJson TEXT, attemptSubmissionId TEXT, attemptRowVersion TEXT, PRIMARY KEY(ownerSubject, flightId))")
            }
        }

        private val MIGRATION_7_8 = object : Migration(7, 8) {
            override fun migrate(db: SupportSQLiteDatabase) {
                // Preserve all earlier submission intent, caches, and private-file references.
                db.execSQL("ALTER TABLE flight_arrival_pending ADD COLUMN reviewedArrivalJson TEXT")
                db.execSQL("ALTER TABLE flight_arrival_pending ADD COLUMN needsArrivalComparison INTEGER NOT NULL DEFAULT 0")
                db.execSQL("CREATE TABLE IF NOT EXISTS flight_service_pending (ownerSubject TEXT NOT NULL, flightId TEXT NOT NULL, serviceRecordId TEXT NOT NULL, flightNumber TEXT NOT NULL, changeId TEXT NOT NULL, operation TEXT NOT NULL, inputJson TEXT, rowVersion TEXT NOT NULL, reviewedServiceJson TEXT, savedAtUtc TEXT NOT NULL, needsServiceComparison INTEGER NOT NULL, status TEXT NOT NULL, error TEXT, attemptOperation TEXT, attemptInputJson TEXT, attemptRowVersion TEXT, attachmentFilesJson TEXT NOT NULL, attemptAttachmentFilesJson TEXT, PRIMARY KEY(ownerSubject, flightId, serviceRecordId))")
                db.execSQL("CREATE TABLE IF NOT EXISTS flight_service_form_drafts (ownerSubject TEXT NOT NULL, flightId TEXT NOT NULL, formJson TEXT NOT NULL, attachmentFilesJson TEXT NOT NULL, savedAtUtc TEXT NOT NULL, PRIMARY KEY(ownerSubject, flightId))")
            }
        }

        private val MIGRATION_8_9 = object : Migration(8, 9) {
            override fun migrate(db: SupportSQLiteDatabase) {
                // An installed intermediate v8 predates these fields and the draft table.
                // Final v8 already has them. Upgrade both without replacing any saved intent.
                val arrivalColumns = db.query("PRAGMA table_info(flight_arrival_pending)").use { cursor ->
                    val name = cursor.getColumnIndexOrThrow("name")
                    buildSet {
                        while (cursor.moveToNext()) add(cursor.getString(name))
                    }
                }
                if ("reviewedArrivalJson" !in arrivalColumns) {
                    db.execSQL("ALTER TABLE flight_arrival_pending ADD COLUMN reviewedArrivalJson TEXT")
                }
                if ("needsArrivalComparison" !in arrivalColumns) {
                    db.execSQL("ALTER TABLE flight_arrival_pending ADD COLUMN needsArrivalComparison INTEGER NOT NULL DEFAULT 0")
                }
                val hasFormDrafts = db.query("PRAGMA table_info(flight_service_form_drafts)").use { it.count > 0 }
                if (!hasFormDrafts) {
                    db.execSQL("CREATE TABLE IF NOT EXISTS flight_service_form_drafts (ownerSubject TEXT NOT NULL, flightId TEXT NOT NULL, formJson TEXT NOT NULL, attachmentFilesJson TEXT NOT NULL, savedAtUtc TEXT NOT NULL, PRIMARY KEY(ownerSubject, flightId))")
                }
            }
        }

        private val MIGRATION_9_10 = object : Migration(9, 10) {
            override fun migrate(db: SupportSQLiteDatabase) {
                // Task intent is additive. Earlier queues, form drafts and their files remain owned and intact.
                db.execSQL("CREATE TABLE IF NOT EXISTS flight_task_pending (ownerSubject TEXT NOT NULL, flightId TEXT NOT NULL, taskRecordId TEXT NOT NULL, flightNumber TEXT NOT NULL, changeId TEXT NOT NULL, operation TEXT NOT NULL, inputJson TEXT, rowVersion TEXT NOT NULL, reviewedTaskJson TEXT, savedAtUtc TEXT NOT NULL, needsTaskComparison INTEGER NOT NULL, status TEXT NOT NULL, error TEXT, attemptOperation TEXT, attemptInputJson TEXT, attemptRowVersion TEXT, attachmentFilesJson TEXT NOT NULL, attemptAttachmentFilesJson TEXT, PRIMARY KEY(ownerSubject, flightId, taskRecordId))")
            }
        }

        fun get(context: Context): CatalogDatabase = instance ?: synchronized(this) {
            instance ?: Room.databaseBuilder(
                context.applicationContext,
                CatalogDatabase::class.java,
                "shared-catalogs.db",
            ).addMigrations(MIGRATION_1_2, MIGRATION_2_3, MIGRATION_3_4, MIGRATION_4_5, MIGRATION_5_6, MIGRATION_6_7, MIGRATION_7_8, MIGRATION_8_9, MIGRATION_9_10).build().also { instance = it }
        }
    }
}
