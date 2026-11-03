package com.nags.operations.data.db.dao

import androidx.room.Dao
import androidx.room.Query
import androidx.room.Upsert
import com.nags.operations.data.db.entities.SharedCatalogSnapshotEntity
import kotlinx.coroutines.flow.Flow

@Dao
interface SharedCatalogDao {
    @Query("SELECT * FROM shared_catalog_snapshots WHERE ownerSubject = :ownerSubject")
    fun observe(ownerSubject: String): Flow<List<SharedCatalogSnapshotEntity>>

    @Query("SELECT * FROM shared_catalog_snapshots WHERE ownerSubject = :ownerSubject AND catalog = :catalog")
    suspend fun snapshot(ownerSubject: String, catalog: String): SharedCatalogSnapshotEntity?

    @Upsert
    suspend fun put(snapshot: SharedCatalogSnapshotEntity)

    @Query("DELETE FROM shared_catalog_snapshots")
    suspend fun clearAll()
}
