package com.nags.operations.data.repo

import com.nags.operations.data.db.AppDatabase
import com.nags.operations.data.db.entities.EmployeeEntity
import kotlinx.coroutines.flow.Flow

class EmployeesRepository(private val db: AppDatabase) {
    /** Reactive list of every active employee at the signed-in user's station. */
    fun observe(): Flow<List<EmployeeEntity>> = db.employeeDao().observeAll()

    /** One-shot read of the cached station roster (Room). */
    suspend fun snapshot(): List<EmployeeEntity> = db.employeeDao().snapshot()
}
