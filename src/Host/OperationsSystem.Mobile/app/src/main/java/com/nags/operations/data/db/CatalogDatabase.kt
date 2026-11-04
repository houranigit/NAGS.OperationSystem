package com.nags.operations.data.db

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase
import androidx.room.migration.Migration
import androidx.sqlite.db.SupportSQLiteDatabase
import com.nags.operations.data.db.dao.FlightCacheDao
import com.nags.operations.data.db.dao.SharedCatalogDao
import com.nags.operations.data.db.entities.FlightDetailCacheEntity
import com.nags.operations.data.db.entities.FlightSnapshotEntity
import com.nags.operations.data.db.entities.SharedCatalogSnapshotEntity

/** Read-only mobile snapshots remain separate from notifications and the untouched legacy database. */
@Database(version = 3, exportSchema = true, entities = [
    SharedCatalogSnapshotEntity::class, FlightSnapshotEntity::class, FlightDetailCacheEntity::class,
])
abstract class CatalogDatabase : RoomDatabase() {
    abstract fun sharedCatalogDao(): SharedCatalogDao
    abstract fun flightCacheDao(): FlightCacheDao

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

        fun get(context: Context): CatalogDatabase = instance ?: synchronized(this) {
            instance ?: Room.databaseBuilder(
                context.applicationContext,
                CatalogDatabase::class.java,
                "shared-catalogs.db",
            ).addMigrations(MIGRATION_1_2, MIGRATION_2_3).build().also { instance = it }
        }
    }
}
