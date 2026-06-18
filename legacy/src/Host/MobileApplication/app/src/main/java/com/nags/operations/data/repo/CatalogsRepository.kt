package com.nags.operations.data.repo

import com.nags.operations.data.db.AppDatabase
import com.nags.operations.data.db.entities.AircraftTypeEntity
import com.nags.operations.data.db.entities.CustomerEntity
import com.nags.operations.data.db.entities.GeneralSupportEntity
import com.nags.operations.data.db.entities.MaterialEntity
import com.nags.operations.data.db.entities.ServiceEntity
import com.nags.operations.data.db.entities.ToolEntity
import kotlinx.coroutines.flow.Flow

/**
 * Read-only facade over the local catalog tables. Every consumer (UI, sync
 * diagnostics, future work-order pickers) goes through this class so screens
 * never reach into Room or the network directly.
 *
 * Writes happen exclusively from the sync coordinator — there are no
 * insert/update entry points exposed here, by design.
 */
class CatalogsRepository(private val db: AppDatabase) {
    fun servicesFlow(): Flow<List<ServiceEntity>> = db.serviceDao().observeAll()
    fun toolsFlow(): Flow<List<ToolEntity>> = db.toolDao().observeAll()
    fun materialsFlow(): Flow<List<MaterialEntity>> = db.materialDao().observeAll()
    fun generalSupportsFlow(): Flow<List<GeneralSupportEntity>> = db.generalSupportDao().observeAll()
    fun customersFlow(): Flow<List<CustomerEntity>> = db.customerDao().observeAll()
    fun aircraftTypesFlow(): Flow<List<AircraftTypeEntity>> = db.aircraftTypeDao().observeAll()
}
