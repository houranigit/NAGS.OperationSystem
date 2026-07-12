package com.nags.operations.data.sync

import android.util.Log
import com.nags.operations.data.TokenStore
import com.nags.operations.data.api.MobileApi
import com.nags.operations.data.db.AppDatabase
import com.nags.operations.data.db.entities.AircraftTypeEntity
import com.nags.operations.data.db.entities.CustomerEntity
import com.nags.operations.data.db.entities.EmployeeEntity
import com.nags.operations.data.db.entities.GeneralSupportEntity
import com.nags.operations.data.db.entities.MaterialEntity
import com.nags.operations.data.db.entities.ServiceEntity
import com.nags.operations.data.db.entities.SyncStateEntity
import com.nags.operations.data.db.entities.ToolEntity
import com.nags.operations.data.outbox.WorkOrderOutboxRepository
import com.nags.operations.data.realtime.MobileSyncChangeDto
import com.nags.operations.data.realtime.MobileSyncOps
import com.nags.operations.data.realtime.MobileSyncTables
import com.nags.operations.data.toAdHocEntity
import com.nags.operations.data.toFlightEntity
import com.nags.operations.data.toPerLandingEntity
import com.nags.operations.data.userMessage
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

/**
 * The bridge between the server and the local Room cache. Every screen reads from Room; only
 * this class writes the synced tables.
 *
 * Design points the rest of the app depends on:
 *
 *  1. **Each table refreshes independently.** A failure on the flights call does not roll back
 *     a successful catalogs replacement; per-table errors land in [SyncStateEntity].
 *  2. **Replacements are atomic.** Delete-then-insert inside one Room transaction so readers
 *     never see an empty list mid-sync.
 *  3. **The HTTP calls fan out in parallel**; DB writes stay serial per table.
 *  4. **One refresh at a time** via [refreshMutex].
 */
class SyncCoordinator(
    private val api: MobileApi,
    private val db: AppDatabase,
    private val tokenStore: TokenStore,
    /**
     * Optional handle for the offline write outbox. When a server-pushed envelope carries an
     * `originMutationId` matching a locally queued row, that row (and its attachment files) is
     * deleted here — the optimistic chip disappears the moment the real cache row lands.
     */
    private val outboxRepository: WorkOrderOutboxRepository? = null,
) {
    /** True while a [refreshAll] is in flight. Drives the Sync Center spinner. */
    private val _isSyncing = MutableStateFlow(false)
    val isSyncing: StateFlow<Boolean> = _isSyncing.asStateFlow()

    private val refreshMutex = Mutex()

    /** Fan-out refresh of every synced table with a per-table outcome report. */
    suspend fun refreshAll(): SyncReport {
        if (!refreshMutex.tryLock()) return SyncReport.AlreadyRunning
        _isSyncing.value = true
        try {
            refreshStaffProfile()
            val outcomes = coroutineScope {
                val catalogsJob = async { syncCatalogs() }
                val employeesJob = async { syncEmployees() }
                val flightsJob = async { syncMyFlights() }
                val perLandingJob = async { syncPerLandingFlights() }
                val adHocFlightsJob = async { syncAdHocFlights() }

                catalogsJob.await() + listOf(
                    employeesJob.await(),
                    flightsJob.await(),
                    perLandingJob.await(),
                    adHocFlightsJob.await(),
                )
            }
            return SyncReport.Completed(outcomes)
        } finally {
            _isSyncing.value = false
            refreshMutex.unlock()
        }
    }

    /**
     * Single round-trip for the six catalog tables: one network call, six Room writes — one
     * transaction per table so a slow write on customers doesn't stall a services read.
     */
    private suspend fun syncCatalogs(): List<SyncOutcome> {
        return try {
            val payload = api.catalogs()
            timeAndRecord(SyncTable.Services) {
                db.serviceDao().replaceAll(payload.services.map { ServiceEntity(it.id, it.name, it.isAircraftPerLanding) })
            }
            timeAndRecord(SyncTable.Tools) {
                db.toolDao().replaceAll(payload.tools.map { ToolEntity(it.id, it.name) })
            }
            timeAndRecord(SyncTable.Materials) {
                db.materialDao().replaceAll(payload.materials.map { MaterialEntity(it.id, it.name) })
            }
            timeAndRecord(SyncTable.GeneralSupports) {
                db.generalSupportDao().replaceAll(payload.generalSupports.map { GeneralSupportEntity(it.id, it.name) })
            }
            timeAndRecord(SyncTable.Customers) {
                db.customerDao().replaceAll(payload.customers.map { CustomerEntity(it.id, it.iataCode, it.name) })
            }
            timeAndRecord(SyncTable.AircraftTypes) {
                db.aircraftTypeDao().replaceAll(
                    payload.aircraftTypes.map { AircraftTypeEntity(it.id, it.manufacturer, it.model) },
                )
            }

            listOf(
                SyncTable.Services,
                SyncTable.Tools,
                SyncTable.Materials,
                SyncTable.GeneralSupports,
                SyncTable.Customers,
                SyncTable.AircraftTypes,
            )
                .map { SyncOutcome.Success(it) }
        } catch (e: Exception) {
            val message = e.userMessage()
            // One failure burns down all catalog tables — they share the call.
            val catalogTables = listOf(
                SyncTable.Services, SyncTable.Tools, SyncTable.Materials,
                SyncTable.GeneralSupports, SyncTable.Customers, SyncTable.AircraftTypes,
            )
            catalogTables.forEach { recordFailure(it, message) }
            catalogTables.map { SyncOutcome.Failure(it, message) }
        }
    }

    /** Time just the Room write and stamp the per-table success row. */
    private suspend inline fun timeAndRecord(table: SyncTable, crossinline block: suspend () -> Unit) {
        val startedAt = System.currentTimeMillis()
        block()
        recordSuccess(table, System.currentTimeMillis() - startedAt)
    }

    /** Caches `/me` beside the JWT so work-order screens can hydrate offline. */
    private suspend fun refreshStaffProfile() {
        try {
            val me = api.me()
            tokenStore.saveEmployeeProfile(
                employeeId = me.staffMemberId,
                stationCode = me.stationIata,
                fullName = me.fullName,
            )
        } catch (e: Exception) {
            Log.w(TAG, "Staff profile refresh failed — screens fall back to cached data", e)
        }
    }

    private suspend fun syncEmployees(): SyncOutcome = runSync(SyncTable.Employees) {
        val rows = api.myStationEmployees().map { e ->
            EmployeeEntity(
                staffMemberId = e.staffMemberId,
                fullName = e.fullName,
                employeeNumber = e.employeeId,
            )
        }
        db.employeeDao().replaceAll(rows)
    }

    private suspend fun syncMyFlights(): SyncOutcome = runSync(SyncTable.Flights) {
        val rows = api.myFlights().map { it.toFlightEntity() }
        db.flightDao().replaceAll(rows)
    }

    private suspend fun syncPerLandingFlights(): SyncOutcome = runSync(SyncTable.PerLandingFlights) {
        val rows = api.perLandingFlights().map { it.toPerLandingEntity() }
        db.perLandingFlightDao().replaceAll(rows)
    }

    private suspend fun syncAdHocFlights(): SyncOutcome = runSync(SyncTable.AdHocFlights) {
        val rows = api.adHocFlights().map { it.toAdHocEntity() }
        db.adHocFlightDao().replaceAll(rows)
    }

    /**
     * Apply one server-pushed change envelope to the local cache:
     *
     *  - `refresh` — run the matching table's full sync (all catalogs together for a catalog table).
     *  - `delete` — drop the single row matching `entityId`.
     *  - `upsert` — for flight tables, fetch the canonical row from the mobile flight endpoint
     *    and upsert; catalog tables route to a refresh (no per-row catalog payloads).
     *
     * On every successful apply the per-table cursor in `sync_state` is stamped so the next
     * reconnect's catch-up `since=` is honest about cache freshness.
     */
    suspend fun applyChange(change: MobileSyncChangeDto) {
        try {
            when (change.op) {
                MobileSyncOps.Refresh -> applyRefresh(change.table)
                MobileSyncOps.Delete -> applyDelete(change.table, change.entityId)
                MobileSyncOps.Upsert -> applyUpsert(change.table, change.entityId)
                else -> Log.w(TAG, "Unknown mobile-sync op: ${change.op}")
            }
            // After the server-truth row lands, drop the matching outbox row + attachment files
            // so the optimistic chip is replaced by the real myWorkOrder chip in the same frame.
            val mutationId = change.originMutationId
            val repo = outboxRepository
            if (!mutationId.isNullOrBlank() && repo != null) {
                runCatching { repo.deleteAndCleanup(mutationId) }
                    .onFailure { e -> Log.w(TAG, "Failed to clean up outbox row for $mutationId", e) }
            }
            updateCursor(change.table, change.version)
        } catch (e: Exception) {
            Log.w(TAG, "Failed to apply mobile-sync change ${change.table}/${change.op}", e)
        }
    }

    /**
     * Re-fetch a single My-Flights row and upsert it. Used right after a mobile-originated
     * mutation (e.g. inviting teammates) so the inviter's cache reflects the new roster
     * immediately — the server's broadcast targets the invitees, not the inviter.
     */
    suspend fun refreshMyFlight(flightId: String) {
        val row = api.flightById(flightId)
        db.flightDao().upsert(row.toFlightEntity())
    }

    private suspend fun applyRefresh(table: String) {
        when (table) {
            MobileSyncTables.Flights -> syncMyFlights()
            MobileSyncTables.FlightsPerLanding -> syncPerLandingFlights()
            MobileSyncTables.FlightsAdHoc -> syncAdHocFlights()
            MobileSyncTables.Employees -> syncEmployees()
            // Catalog tables share one API call — refresh them all together.
            MobileSyncTables.Services,
            MobileSyncTables.Tools,
            MobileSyncTables.Materials,
            MobileSyncTables.GeneralSupports,
            MobileSyncTables.Customers,
            MobileSyncTables.AircraftTypes -> syncCatalogs()
            else -> Log.w(TAG, "Unknown table on refresh: $table")
        }
    }

    private suspend fun applyDelete(table: String, entityId: String?) {
        if (entityId.isNullOrBlank()) {
            Log.w(TAG, "Delete envelope for $table without entityId — ignored")
            return
        }
        when (table) {
            MobileSyncTables.Flights -> db.flightDao().deleteById(entityId)
            MobileSyncTables.FlightsPerLanding -> db.perLandingFlightDao().deleteById(entityId)
            MobileSyncTables.FlightsAdHoc -> db.adHocFlightDao().deleteById(entityId)
            // Catalog / employee deletes route through a refresh.
            else -> applyRefresh(table)
        }
    }

    private suspend fun applyUpsert(table: String, entityId: String?) {
        if (entityId.isNullOrBlank()) {
            Log.w(TAG, "Upsert envelope for $table without entityId — ignored")
            return
        }
        when (table) {
            MobileSyncTables.Flights -> {
                val row = api.flightById(entityId)
                db.flightDao().upsert(row.toFlightEntity())
            }
            MobileSyncTables.FlightsPerLanding -> {
                val row = api.flightById(entityId)
                db.perLandingFlightDao().upsert(row.toPerLandingEntity())
            }
            MobileSyncTables.FlightsAdHoc -> {
                val row = api.flightById(entityId)
                db.adHocFlightDao().upsert(row.toAdHocEntity())
            }
            // Catalog tables don't carry per-row payloads — route to a refresh.
            else -> applyRefresh(table)
        }
    }

    private suspend fun updateCursor(table: String, version: String) {
        val storageKey = storageKeyFor(table) ?: return
        db.syncStateDao().updateCursor(storageKey, version)
    }

    private fun storageKeyFor(table: String): String? = when (table) {
        MobileSyncTables.Flights -> SyncTable.Flights.storageKey
        MobileSyncTables.FlightsPerLanding -> SyncTable.PerLandingFlights.storageKey
        MobileSyncTables.FlightsAdHoc -> SyncTable.AdHocFlights.storageKey
        MobileSyncTables.Employees -> SyncTable.Employees.storageKey
        MobileSyncTables.Services -> SyncTable.Services.storageKey
        MobileSyncTables.Tools -> SyncTable.Tools.storageKey
        MobileSyncTables.Materials -> SyncTable.Materials.storageKey
        MobileSyncTables.GeneralSupports -> SyncTable.GeneralSupports.storageKey
        MobileSyncTables.Customers -> SyncTable.Customers.storageKey
        MobileSyncTables.AircraftTypes -> SyncTable.AircraftTypes.storageKey
        else -> null
    }

    companion object {
        private const val TAG = "SyncCoordinator"
    }

    /**
     * Runs [block] (network + Room replace), measures duration, and stamps the table's
     * [SyncStateEntity] with success or failure.
     */
    private suspend inline fun runSync(
        table: SyncTable,
        crossinline block: suspend () -> Unit,
    ): SyncOutcome {
        val startedAt = System.currentTimeMillis()
        return try {
            block()
            recordSuccess(table, System.currentTimeMillis() - startedAt)
            SyncOutcome.Success(table)
        } catch (e: Exception) {
            val message = e.userMessage()
            recordFailure(table, message)
            SyncOutcome.Failure(table, message)
        }
    }

    private suspend fun recordSuccess(table: SyncTable, durationMs: Long) {
        db.syncStateDao().upsert(
            SyncStateEntity(
                tableName = table.storageKey,
                lastSyncedAt = System.currentTimeMillis(),
                lastDurationMs = durationMs,
                lastError = null,
                cursor = nowIso(),
            ),
        )
    }

    private suspend fun recordFailure(table: SyncTable, message: String) {
        // Preserve the previous lastSyncedAt + cursor; only overwrite the error column.
        val previous = db.syncStateDao().get(table.storageKey)
        db.syncStateDao().upsert(
            SyncStateEntity(
                tableName = table.storageKey,
                lastSyncedAt = previous?.lastSyncedAt,
                lastDurationMs = previous?.lastDurationMs,
                lastError = message,
                cursor = previous?.cursor,
            ),
        )
    }

    private fun nowIso(): String = java.time.Instant.now().toString()

    /**
     * Wipes every synced table plus the metadata table. Called from logout so the next
     * sign-in doesn't read the previous user's data.
     */
    suspend fun clearForLogout() {
        refreshMutex.withLock {
            db.serviceDao().deleteAll()
            db.toolDao().deleteAll()
            db.materialDao().deleteAll()
            db.generalSupportDao().deleteAll()
            db.customerDao().deleteAll()
            db.aircraftTypeDao().deleteAll()
            db.employeeDao().deleteAll()
            db.flightDao().deleteAll()
            db.perLandingFlightDao().deleteAll()
            db.adHocFlightDao().deleteAll()
            db.syncStateDao().deleteAll()
            db.workOrderDraftDao().deleteAll()
            // Drop every queued write and its on-disk attachments too.
            runCatching { outboxRepository?.deleteAll() }
        }
        _isSyncing.update { false }
    }
}

/** Per-table outcome reported back by [SyncCoordinator.refreshAll]. */
sealed interface SyncOutcome {
    val table: SyncTable
    data class Success(override val table: SyncTable) : SyncOutcome
    data class Failure(override val table: SyncTable, val message: String) : SyncOutcome
}

/** Top-level result of a refresh — distinguishes "did not run" from "ran with mixed outcomes". */
sealed interface SyncReport {
    data object AlreadyRunning : SyncReport
    data class Completed(val outcomes: List<SyncOutcome>) : SyncReport
}
