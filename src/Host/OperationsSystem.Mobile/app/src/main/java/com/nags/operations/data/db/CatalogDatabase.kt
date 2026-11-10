package com.nags.operations.data.db

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase
import androidx.room.migration.Migration
import androidx.sqlite.db.SupportSQLiteDatabase
import com.nags.operations.data.db.dao.FlightCacheDao
import com.nags.operations.data.db.dao.FlightDepartureDao
import com.nags.operations.data.db.entities.FlightDeparturePendingEntity
import com.nags.operations.data.db.dao.FlightArrivalDao
import com.nags.operations.data.db.entities.FlightArrivalPendingEntity
import com.nags.operations.data.db.dao.SharedCatalogDao
import com.nags.operations.data.db.entities.FlightDetailCacheEntity
import com.nags.operations.data.db.entities.FlightSnapshotEntity
import com.nags.operations.data.db.entities.SharedCatalogSnapshotEntity

/** Mobile caches and operational intent remain separate from notifications and the untouched legacy database. */
@Database(version = 6, exportSchema = true, entities = [
    SharedCatalogSnapshotEntity::class, FlightSnapshotEntity::class, FlightDetailCacheEntity::class, FlightArrivalPendingEntity::class, FlightDeparturePendingEntity::class,
])
abstract class CatalogDatabase : RoomDatabase() {
    abstract fun sharedCatalogDao(): SharedCatalogDao
    abstract fun flightCacheDao(): FlightCacheDao
    abstract fun flightArrivalDao(): FlightArrivalDao
    abstract fun flightDepartureDao(): FlightDepartureDao

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

        fun get(context: Context): CatalogDatabase = instance ?: synchronized(this) {
            instance ?: Room.databaseBuilder(
                context.applicationContext,
                CatalogDatabase::class.java,
                "shared-catalogs.db",
            ).addMigrations(MIGRATION_1_2, MIGRATION_2_3, MIGRATION_3_4, MIGRATION_4_5, MIGRATION_5_6).build().also { instance = it }
        }
    }
}
