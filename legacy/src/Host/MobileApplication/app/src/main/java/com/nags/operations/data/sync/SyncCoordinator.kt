package com.nags.operations.data.sync

import android.util.Log
import com.nags.operations.data.TokenStore
import com.nags.operations.data.api.MobileApi
import com.nags.operations.data.db.AppDatabase
import com.nags.operations.data.MobileFlightSummaryDto
import com.nags.operations.data.db.entities.AdHocFlightEntity
import com.nags.operations.data.db.entities.AogFlightEntity
import com.nags.operations.data.db.entities.AircraftTypeEntity
import com.nags.operations.data.db.entities.CustomerEntity
import com.nags.operations.data.db.entities.EmployeeEntity
import com.nags.operations.data.db.entities.FlightAssignedEmployeeSummary
import com.nags.operations.data.db.entities.FlightEntity
import com.nags.operations.data.db.entities.FlightServiceSummary
import com.nags.operations.data.db.entities.GeneralSupportEntity
import com.nags.operations.data.db.entities.MaterialEntity
import com.nags.operations.data.db.entities.ServiceEntity
import com.nags.operations.data.db.entities.SyncStateEntity
import com.nags.operations.data.db.entities.ToolEntity
import com.nags.operations.data.outbox.WorkOrderOutboxRepository
import com.nags.operations.data.realtime.MobileSyncChangeDto
import com.nags.operations.data.realtime.MobileSyncOps
import com.nags.operations.data.realtime.MobileSyncTables
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
 * The bridge between the server and the local Room cache. Every screen reads
 * from Room; only this class writes to it.
 *
 * Design points the rest of the app depends on:
 *
 *  1. **Each table is refreshed independently.** A failure on the flights call
 *     does not roll back a successful catalogs replacement. Per-table errors
 *     are recorded in [SyncStateEntity] and surfaced in the Sync Center.
 *  2. **Replacements are atomic.** Inside each table's slice we delete-then-
 *     insert in a single Room transaction so concurrent readers never see an
 *     empty list mid-sync.
 *  3. **The HTTP calls fan out in parallel.** `coroutineScope { async {...} }`
 *     starts all five GETs at once; the slowest one bounds the wall-clock
 *     duration. The DB writes still happen serially per table to avoid
 *     SQLite write contention.
 *  4. **One refresh at a time.** [refreshMutex] turns concurrent
 *     `refreshAll()` calls into a no-op queue so the periodic timer and the
 *     "Refresh now" button can't pile up duplicate work.
 *
 * The mutex is not held across the network calls — only across the
 * housekeeping. The HTTP work happens on a child coroutine scope while the
 * mutex is held, so re-entry only matters for accidental concurrent calls.
 */
class SyncCoordinator(
    private val api: MobileApi,
    private val db: AppDatabase,
    private val tokenStore: TokenStore,
    /**
     * Optional handle for the offline write outbox. When a server-pushed
     * change envelope carries an `originMutationId` that matches a row we
     * queued locally, we delete the row (and its attachment files) right here
     * — the chip on the flight card disappears the moment the real cache row
     * lands. Defaulted to null so the fetch-only read path can keep
     * constructing this class without the write lane wired up (tests etc.).
     */
    private val outboxRepository: WorkOrderOutboxRepository? = null,
) {
    /** True while a [refreshAll] is currently in flight. Drives the spinner in the Sync Center. */
    private val _isSyncing = MutableStateFlow(false)
    val isSyncing: StateFlow<Boolean> = _isSyncing.asStateFlow()

    private val refreshMutex = Mutex()

    /**
     * Fan-out refresh of every synced table. Returns a [SyncReport] summarising
     * per-table outcomes — handy for logs and tests, ignorable for screens
     * that just want "did anything happen".
     */
    suspend fun refreshAll(): SyncReport {
        if (!refreshMutex.tryLock()) return SyncReport.AlreadyRunning
        _isSyncing.value = true
        try {
            refreshEmployeeProfile()
            val outcomes = coroutineScope {
                val catalogsJob = async { syncCatalogs() }
                val employeesJob = async { syncEmployees() }
                val flightsJob = async { syncMyFlights() }
                val aogFlightsJob = async { syncAogFlights() }
                val adHocFlightsJob = async { syncAdHocFlights() }

                // Catalogs returns five outcomes (one per catalog table); the others return one each.
                catalogsJob.await() + listOf(
                    employeesJob.await(),
                    flightsJob.await(),
                    aogFlightsJob.await(),
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
     * Single round-trip for the five catalog tables. We do one network call
     * (the server bundles them so the wire is cheap) and five Room writes —
     * one transaction per table so a slow row count on customers doesn't
     * stall a Services read.
     *
     * Each table records its own Room-write duration via [timeAndRecord] so
     * the Sync Center surfaces honest per-DAO timing instead of repeating the
     * network call's wall-clock across all five rows.
     */
    private suspend fun syncCatalogs(): List<SyncOutcome> {
        return try {
            val payload = api.catalogs()
            timeAndRecord(SyncTable.Services) {
                db.serviceDao().replaceAll(payload.services.map { ServiceEntity(it.serviceId, it.name, it.isAog) })
            }
            timeAndRecord(SyncTable.Tools) {
                db.toolDao().replaceAll(payload.tools.map { ToolEntity(it.toolId, it.name) })
            }
            timeAndRecord(SyncTable.Materials) {
                db.materialDao().replaceAll(payload.materials.map { MaterialEntity(it.materialId, it.name) })
            }
            timeAndRecord(SyncTable.GeneralSupports) {
                db.generalSupportDao().replaceAll(payload.generalSupports.map { GeneralSupportEntity(it.generalSupportId, it.name) })
            }
            timeAndRecord(SyncTable.Customers) {
                db.customerDao().replaceAll(payload.customers.map { CustomerEntity(it.customerId, it.iataCode, it.name) })
            }
            timeAndRecord(SyncTable.AircraftTypes) {
                db.aircraftTypeDao().replaceAll(
                    payload.aircraftTypes.map { AircraftTypeEntity(it.aircraftTypeId, it.model) },
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
            // One failure burns down all catalog tables — they share the call,
            // so it would be misleading to mark some as fresh and others as stale.
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
    private suspend fun refreshEmployeeProfile() {
        try {
            val me = api.me()
            tokenStore.saveEmployeeProfile(
                employeeId = me.employeeId,
                stationCode = me.stationCode,
                fullName = me.fullName,
            )
        } catch (e: Exception) {
            Log.w(TAG, "Employee profile refresh failed — screens fall back to cached roster", e)
        }
    }

    private suspend fun syncEmployees(): SyncOutcome = runSync(SyncTable.Employees) {
        val rows = api.myStationEmployees().map { e ->
            EmployeeEntity(
                employeeId = e.employeeId,
                fullName = e.fullName,
                stationId = e.stationSnapshot.stationId,
                stationCode = e.stationSnapshot.iataCode,
                stationName = e.stationSnapshot.name,
                manpowerTypeId = e.manpowerTypeSnapshot.manpowerTypeId,
                manpowerTypeName = e.manpowerTypeSnapshot.name,
            )
        }
        db.employeeDao().replaceAll(rows)
    }

    private suspend fun syncMyFlights(): SyncOutcome = runSync(SyncTable.Flights) {
        val rows = api.myFlights().map { it.toFlightEntity() }
        db.flightDao().replaceAll(rows)
    }

    private suspend fun syncAogFlights(): SyncOutcome = runSync(SyncTable.AogFlights) {
        // AOG payload intentionally has no `services` list — the AOG chip is implicit on
        // the AOG tab, and matching the lean wire shape lets us drop the column entirely
        // (see AogFlightEntity for the rationale).
        val rows = api.aogFlights().map { f ->
            AogFlightEntity(
                id = f.id,
                flightNumber = f.flightNumber,
                customerName = f.customerName,
                customerIataCode = f.customerIataCode,
                stationCode = f.stationCode,
                operationTypeCode = f.operationTypeCode,
                sta = f.sta,
                std = f.std,
                aircraftModel = f.aircraftModel,
                status = f.status,
                canceledAt = f.canceledAt,
                assignedEmployeesCount = f.assignedEmployeesCount,
                myWorkOrder = f.myWorkOrder,
                otherWorkOrdersExist = f.otherWorkOrdersExist,
            )
        }
        db.aogFlightDao().replaceAll(rows)
    }

    private suspend fun syncAdHocFlights(): SyncOutcome = runSync(SyncTable.AdHocFlights) {
        val rows = api.adHocFlights().map { f ->
            AdHocFlightEntity(
                id = f.id,
                flightNumber = f.flightNumber,
                customerName = f.customerName,
                customerIataCode = f.customerIataCode,
                stationCode = f.stationCode,
                operationTypeCode = f.operationTypeCode,
                sta = f.sta,
                std = f.std,
                aircraftModel = f.aircraftModel,
                status = f.status,
                canceledAt = f.canceledAt,
                assignedEmployeesCount = f.assignedEmployeesCount,
                myWorkOrder = f.myWorkOrder,
                otherWorkOrdersExist = f.otherWorkOrdersExist,
            )
        }
        db.adHocFlightDao().replaceAll(rows)
    }

    /**
     * Same projection for both flight lists — keeps the wire DTO -> row entity
     * mapping in one place so a future field added to the service shape only
     * needs editing here.
     */
    private fun MobileFlightSummaryDto.toServiceSummaries(): List<FlightServiceSummary> =
        services.map { s -> FlightServiceSummary(s.serviceId, s.name, s.isAog) }

    /** DTO -> cached assigned-employee rows (My Flights only — drives the invite screen). */
    private fun MobileFlightSummaryDto.toAssignedEmployeeSummaries(): List<FlightAssignedEmployeeSummary> =
        assignedEmployees.map { e ->
            FlightAssignedEmployeeSummary(e.employeeId, e.fullName, e.manpowerTypeName)
        }

    /** DTO → my-flights row, used by both the bulk and the single-flight paths. */
    private fun MobileFlightSummaryDto.toFlightEntity(): FlightEntity = FlightEntity(
        id = id,
        flightNumber = flightNumber,
        customerName = customerName,
        customerIataCode = customerIataCode,
        stationCode = stationCode,
        operationTypeCode = operationTypeCode,
        sta = sta,
        std = std,
        aircraftModel = aircraftModel,
        status = status,
        canceledAt = canceledAt,
        assignedEmployeesCount = assignedEmployeesCount,
        myWorkOrder = myWorkOrder,
        otherWorkOrdersExist = otherWorkOrdersExist,
        services = toServiceSummaries(),
        assignedEmployees = toAssignedEmployeeSummaries(),
    )

    /** DTO → AOG-flights row. AOG cache deliberately drops the per-flight services list. */
    private fun MobileFlightSummaryDto.toAogFlightEntity(): AogFlightEntity = AogFlightEntity(
        id = id,
        flightNumber = flightNumber,
        customerName = customerName,
        customerIataCode = customerIataCode,
        stationCode = stationCode,
        operationTypeCode = operationTypeCode,
        sta = sta,
        std = std,
        aircraftModel = aircraftModel,
        status = status,
        canceledAt = canceledAt,
        assignedEmployeesCount = assignedEmployeesCount,
        myWorkOrder = myWorkOrder,
        otherWorkOrdersExist = otherWorkOrdersExist,
    )

    /** Single-flight row shape for the Ad Hoc cache (no services list). */
    private fun MobileFlightSummaryDto.toAdHocFlightEntity(): AdHocFlightEntity = AdHocFlightEntity(
        id = id,
        flightNumber = flightNumber,
        customerName = customerName,
        customerIataCode = customerIataCode,
        stationCode = stationCode,
        operationTypeCode = operationTypeCode,
        sta = sta,
        std = std,
        aircraftModel = aircraftModel,
        status = status,
        canceledAt = canceledAt,
        assignedEmployeesCount = assignedEmployeesCount,
        myWorkOrder = myWorkOrder,
        otherWorkOrdersExist = otherWorkOrdersExist,
    )

    /**
     * Apply one server-pushed change envelope to the local cache. Three operations:
     *
     *  - `refresh` — invoke the matching table's full sync function (or every
     *    catalog table on a catalog refresh). Used for bulk imports and as the
     *    catch-up endpoint's reply.
     *  - `delete` — drop the single row matching `entityId` from the right DAO.
     *  - `upsert` — for flight tables, fetch the canonical row from
     *    `GET /api/mobile/v2/flights/{id}` and upsert. For catalog tables we
     *    currently route to a full refresh (the server doesn't emit per-row
     *    catalog payloads in phase 1).
     *
     * On every successful apply we stamp the per-table cursor in `sync_state`
     * so the next reconnect's catch-up `since=` is honest about how recent the
     * cache is.
     */
    suspend fun applyChange(change: MobileSyncChangeDto) {
        try {
            when (change.op) {
                MobileSyncOps.Refresh -> applyRefresh(change.table)
                MobileSyncOps.Delete -> applyDelete(change.table, change.entityId)
                MobileSyncOps.Upsert -> applyUpsert(change.table, change.entityId)
                else -> Log.w(TAG, "Unknown mobile-sync op: ${'$'}{change.op}")
            }
            // After the server-truth row lands in `flights_*`, drop the matching
            // outbox row + its attachment files so the optimistic chip is replaced
            // by the real `myWorkOrder` chip in the same frame. We
            // deliberately do *not* short-circuit applying the echo — the cache
            // must always reflect the server state regardless of who triggered it.
            val mutationId = change.originMutationId
            val repo = outboxRepository
            if (!mutationId.isNullOrBlank() && repo != null) {
                runCatching { repo.deleteAndCleanup(mutationId) }
                    .onFailure { e ->
                        Log.w(TAG, "Failed to clean up outbox row for $mutationId", e)
                    }
            }
            updateCursor(change.table, change.version)
        } catch (e: Exception) {
            Log.w(TAG, "Failed to apply mobile-sync change ${'$'}{change.table}/${'$'}{change.op}", e)
        }
    }

    /**
     * Re-fetch a single My-Flights row from the server and upsert it into the local cache.
     * Used right after a mobile-originated mutation (e.g. inviting teammates) so the inviter's
     * cache reflects the new assigned-employee roster immediately — the server's auto-broadcast
     * targets the invitees, not the inviter.
     */
    suspend fun refreshMyFlight(flightId: String) {
        val row = api.flightById(flightId)
        db.flightDao().upsert(row.toFlightEntity())
    }

    private suspend fun applyRefresh(table: String) {
        when (table) {
            MobileSyncTables.Flights -> syncMyFlights()
            MobileSyncTables.FlightsAog -> syncAogFlights()
            MobileSyncTables.FlightsAdHoc -> syncAdHocFlights()
            MobileSyncTables.Employees -> syncEmployees()
            // Catalog tables share one API call — refresh them all together. Slightly
            // over-broad for a single-table refresh but cheap, and keeps the network
            // call count low under bulk-import storms.
            MobileSyncTables.Services,
            MobileSyncTables.Tools,
            MobileSyncTables.Materials,
            MobileSyncTables.GeneralSupports,
            MobileSyncTables.Customers,
            MobileSyncTables.AircraftTypes -> syncCatalogs()
            else -> Log.w(TAG, "Unknown table on refresh: ${'$'}table")
        }
    }

    private suspend fun applyDelete(table: String, entityId: String?) {
        if (entityId.isNullOrBlank()) {
            Log.w(TAG, "Delete envelope for ${'$'}table without entityId — ignored")
            return
        }
        when (table) {
            MobileSyncTables.Flights -> db.flightDao().deleteById(entityId)
            MobileSyncTables.FlightsAog -> db.aogFlightDao().deleteById(entityId)
            MobileSyncTables.FlightsAdHoc -> db.adHocFlightDao().deleteById(entityId)
            // Catalog / employees deletes route through a refresh; per-row delete
            // DAO methods can be added if the server ever wires fine-grained
            // catalog change events.
            else -> applyRefresh(table)
        }
    }

    private suspend fun applyUpsert(table: String, entityId: String?) {
        if (entityId.isNullOrBlank()) {
            Log.w(TAG, "Upsert envelope for ${'$'}table without entityId — ignored")
            return
        }
        when (table) {
            MobileSyncTables.Flights -> {
                val row = api.flightById(entityId)
                db.flightDao().upsert(row.toFlightEntity())
            }
            MobileSyncTables.FlightsAog -> {
                val row = api.flightById(entityId)
                db.aogFlightDao().upsert(row.toAogFlightEntity())
            }
            MobileSyncTables.FlightsAdHoc -> {
                val row = api.flightById(entityId)
                db.adHocFlightDao().upsert(row.toAdHocFlightEntity())
            }
            // Catalog tables don't carry per-row payloads yet — route to a refresh.
            else -> applyRefresh(table)
        }
    }

    private suspend fun updateCursor(table: String, version: String) {
        val storageKey = storageKeyFor(table) ?: return
        val updated = db.syncStateDao().updateCursor(storageKey, version)
        if (updated == 0) {
            // The sync_state row hasn't been written yet — the next periodic
            // sync will recreate it with a fresh cursor. Not worth conjuring
            // a placeholder row here that pretends an initial sync happened.
        }
    }

    private fun storageKeyFor(table: String): String? = when (table) {
        MobileSyncTables.Flights -> SyncTable.Flights.storageKey
        MobileSyncTables.FlightsAog -> SyncTable.AogFlights.storageKey
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
     * Runs [block] (the network call + the Room replace), measures wall-clock
     * duration, and stamps the table's [SyncStateEntity] with either a success
     * or a failure. Used by every single-table slice; the catalogs slice
     * inlines the recording because it owns five tables for one network call.
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
        // A successful per-table refresh has just replaced every row with the
        // server's current state, so we can bump the cursor to "now" — the
        // next reconnect's catch-up asks for "since now" and only gets the
        // deltas. Format matches the server envelope's ISO-8601 UTC version.
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
        // Preserve the previous lastSyncedAt + cursor — the data on disk is
        // whatever the previous successful sync wrote, so the "X minutes ago"
        // badge should keep ticking forward and the cursor must not regress.
        // We only overwrite the error column.
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
     * Wipes every synced table plus the metadata table. Called from logout
     * so the next sign-in doesn't read stale data from the previous user
     * (which could be a different employee at a different station).
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
            db.aogFlightDao().deleteAll()
            db.adHocFlightDao().deleteAll()
            db.syncStateDao().deleteAll()
            db.workOrderDraftDao().deleteAll()
            // Drop every queued write and its on-disk attachments so the next
            // sign-in doesn't inherit the previous user's pending submissions.
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
