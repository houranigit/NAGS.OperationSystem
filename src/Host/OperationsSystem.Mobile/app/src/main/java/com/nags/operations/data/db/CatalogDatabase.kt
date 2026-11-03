package com.nags.operations.data.db

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase
import androidx.room.migration.Migration
import androidx.sqlite.db.SupportSQLiteDatabase
import com.nags.operations.data.db.dao.SharedCatalogDao
import com.nags.operations.data.db.entities.SharedCatalogSnapshotEntity

/** Shared reference data is separate from notifications and the untouched legacy database. */
@Database(version = 2, exportSchema = true, entities = [SharedCatalogSnapshotEntity::class])
abstract class CatalogDatabase : RoomDatabase() {
    abstract fun sharedCatalogDao(): SharedCatalogDao

    companion object {
        @Volatile private var instance: CatalogDatabase? = null

        private val MIGRATION_1_2 = object : Migration(1, 2) {
            override fun migrate(db: SupportSQLiteDatabase) {
                // Existing offline snapshots and their successful sync metadata remain intact.
                db.execSQL("ALTER TABLE shared_catalog_snapshots ADD COLUMN lastDurationMs INTEGER")
            }
        }

        fun get(context: Context): CatalogDatabase = instance ?: synchronized(this) {
            instance ?: Room.databaseBuilder(
                context.applicationContext,
                CatalogDatabase::class.java,
                "shared-catalogs.db",
            ).addMigrations(MIGRATION_1_2).build().also { instance = it }
        }
    }
}
