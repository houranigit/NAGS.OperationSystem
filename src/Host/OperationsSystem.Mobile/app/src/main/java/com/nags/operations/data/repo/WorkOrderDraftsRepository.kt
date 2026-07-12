package com.nags.operations.data.repo

import com.nags.operations.data.db.AppDatabase
import com.nags.operations.data.db.entities.WorkOrderDraftEntity
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map

/** Local-only work order drafts (Room). */
class WorkOrderDraftsRepository(private val db: AppDatabase) {

    fun observeDrafts(): Flow<List<WorkOrderDraftEntity>> = db.workOrderDraftDao().observeAll()

    /** For each flight, the draft row with the newest [WorkOrderDraftEntity.updatedAtEpochMs]. */
    fun observeFlightIdToLatestDraftId(): Flow<Map<String, String>> =
        observeDrafts().map { drafts ->
            drafts
                .groupBy { it.flightId }
                .mapValues { (_, rows) ->
                    rows.maxBy { it.updatedAtEpochMs }.draftId
                }
        }

    suspend fun getDraft(id: String): WorkOrderDraftEntity? = db.workOrderDraftDao().getById(id)

    suspend fun upsertDraft(entity: WorkOrderDraftEntity) {
        db.workOrderDraftDao().upsert(entity)
    }

    suspend fun deleteDraft(id: String) {
        db.workOrderDraftDao().deleteById(id)
    }

    suspend fun deleteAllDrafts() {
        db.workOrderDraftDao().deleteAll()
    }
}
