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
import com.nags.operations.data.db.dao.FlightOperationsBatchDao
import com.nags.operations.data.db.entities.FlightOperationsBatchEntity
import com.nags.operations.data.db.dao.AdHocFlightDao
import com.nags.operations.data.db.entities.AdHocFlightContextEntity
import com.nags.operations.data.db.entities.AdHocFlightPendingEntity
import com.nags.operations.data.db.dao.FlightDiscoveryDao
import com.nags.operations.data.db.entities.FlightDiscoveryQueryEntity
import com.nags.operations.data.db.entities.FlightDiscoveryDetailEntity
import com.nags.operations.data.db.entities.FlightDiscoveryStationEntity
import com.nags.operations.data.db.entities.FlightLocalInteractionEntity

/** Mobile caches and operational intent remain separate from notifications and the untouched legacy database. */
@Database(version = 14, exportSchema = true, entities = [FlightOperationsBatchEntity::class, AdHocFlightContextEntity::class, AdHocFlightPendingEntity::class, FlightDiscoveryQueryEntity::class, FlightDiscoveryDetailEntity::class, FlightDiscoveryStationEntity::class, FlightLocalInteractionEntity::class,
    SharedCatalogSnapshotEntity::class, FlightSnapshotEntity::class, FlightDetailCacheEntity::class, FlightArrivalPendingEntity::class, FlightDeparturePendingEntity::class, FlightCancellationPendingEntity::class, FlightServicePendingEntity::class, FlightServiceFormDraftEntity::class, FlightTaskPendingEntity::class,
])
abstract class CatalogDatabase : RoomDatabase() {
    abstract fun flightDiscoveryDao(): FlightDiscoveryDao
    abstract fun adHocFlightDao(): AdHocFlightDao
    abstract fun flightOperationsBatchDao(): FlightOperationsBatchDao
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

        private val MIGRATION_10_11 = object : Migration(10, 11) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL("CREATE TABLE IF NOT EXISTS flight_operations_batch (ownerSubject TEXT NOT NULL, flightId TEXT NOT NULL, changeId TEXT NOT NULL, inputJson TEXT NOT NULL, rowVersion TEXT NOT NULL, status TEXT NOT NULL, error TEXT, attemptInputJson TEXT, attemptRowVersion TEXT, needsComparison INTEGER NOT NULL DEFAULT 0, attemptRejected INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(ownerSubject, flightId))")
            }
        }

        private fun ensureBatchFlags(db: SupportSQLiteDatabase) {
            // Intermediate v11/v12 layouts lack these flags; retain existing values.
            val batchColumns = db.query("PRAGMA table_info(flight_operations_batch)").use { cursor ->
                val name = cursor.getColumnIndexOrThrow("name")
                buildSet {
                    while (cursor.moveToNext()) add(cursor.getString(name))
                }
            }
            if ("needsComparison" !in batchColumns) {
                db.execSQL("ALTER TABLE flight_operations_batch ADD COLUMN needsComparison INTEGER NOT NULL DEFAULT 0")
            }
            if ("attemptRejected" !in batchColumns) {
                db.execSQL("ALTER TABLE flight_operations_batch ADD COLUMN attemptRejected INTEGER NOT NULL DEFAULT 0")
            }
        }

        private val MIGRATION_11_12 = object : Migration(11, 12) {
            override fun migrate(db: SupportSQLiteDatabase) {
                ensureBatchFlags(db)
            }
        }

        private val MIGRATION_12_13 = object : Migration(12, 13) {
            override fun migrate(db: SupportSQLiteDatabase) {
                // Either intermediate v12 feature may have been installed independently.
                ensureBatchFlags(db)
                listOf("flight_arrival_pending", "flight_departure_pending", "flight_service_pending", "flight_task_pending").forEach { table ->
                    val columns = db.query("PRAGMA table_info($table)").use { cursor ->
                        val name = cursor.getColumnIndexOrThrow("name")
                        buildSet {
                            while (cursor.moveToNext()) add(cursor.getString(name))
                        }
                    }
                    if ("reviewSourceInputJson" !in columns) {
                        db.execSQL("ALTER TABLE $table ADD COLUMN reviewSourceInputJson TEXT")
                    }
                }
            }
        }

        private val MIGRATION_13_14 = object : Migration(13, 14) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL("CREATE TABLE IF NOT EXISTS flight_adhoc_context (ownerSubject TEXT NOT NULL PRIMARY KEY, payloadJson TEXT NOT NULL)")
                db.execSQL("CREATE TABLE IF NOT EXISTS flight_adhoc_pending (ownerSubject TEXT NOT NULL, flightId TEXT NOT NULL, stationId TEXT NOT NULL, inputJson TEXT NOT NULL, localDetailJson TEXT NOT NULL, operationsBaseVersion TEXT NOT NULL, savedAtUtc TEXT NOT NULL, status TEXT NOT NULL, error TEXT, materializedDetailJson TEXT, attemptOperation TEXT, attemptInputJson TEXT, attemptRowVersion TEXT, attemptRejected INTEGER NOT NULL, viewRevoked INTEGER NOT NULL, acknowledgedCreationInputJson TEXT, PRIMARY KEY(ownerSubject, flightId))")
                db.execSQL("CREATE TABLE IF NOT EXISTS flight_discovery_queries (ownerSubject TEXT NOT NULL, source TEXT NOT NULL, query TEXT NOT NULL, payloadJson TEXT, lastAttemptUtc TEXT, lastSuccessUtc TEXT, error TEXT, denied INTEGER NOT NULL, accessStatus INTEGER, PRIMARY KEY(ownerSubject, source, query))")
                db.execSQL("CREATE TABLE IF NOT EXISTS flight_discovery_details (ownerSubject TEXT NOT NULL, flightId TEXT NOT NULL, payloadJson TEXT NOT NULL, revision INTEGER NOT NULL, PRIMARY KEY(ownerSubject, flightId))")
                db.execSQL("CREATE TABLE IF NOT EXISTS flight_discovery_station (ownerSubject TEXT NOT NULL PRIMARY KEY, stationId TEXT NOT NULL)")
                db.execSQL("CREATE TABLE IF NOT EXISTS flight_local_interactions (ownerSubject TEXT NOT NULL, flightId TEXT NOT NULL, stationId TEXT NOT NULL, payloadJson TEXT NOT NULL, interactionAtUtc TEXT NOT NULL, usedSearch INTEGER NOT NULL, acknowledged INTEGER NOT NULL, viewRevoked INTEGER NOT NULL, PRIMARY KEY(ownerSubject, flightId))")
            }
        }

        fun get(context: Context): CatalogDatabase = instance ?: synchronized(this) {
            instance ?: Room.databaseBuilder(
                context.applicationContext,
                CatalogDatabase::class.java,
                "shared-catalogs.db",
            ).addMigrations(MIGRATION_1_2, MIGRATION_2_3, MIGRATION_3_4, MIGRATION_4_5, MIGRATION_5_6, MIGRATION_6_7, MIGRATION_7_8, MIGRATION_8_9, MIGRATION_9_10, MIGRATION_10_11, MIGRATION_11_12, MIGRATION_12_13, MIGRATION_13_14).build().also { instance = it }
        }
    }
}
