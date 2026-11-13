package com.nags.operations.data.repo

import com.nags.operations.data.*
import com.nags.operations.data.api.HttpClientFactory
import com.nags.operations.data.db.dao.AdHocFlightDao
import com.nags.operations.data.db.entities.*
import io.ktor.client.plugins.HttpRequestTimeoutException
import java.io.IOException
import java.time.Instant
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.*
import kotlinx.serialization.SerializationException
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

/** Current-model creation/planning intent; normal work/media remain owned by the existing batch queues. */
@OptIn(ExperimentalCoroutinesApi::class)
class AdHocFlightsRepository(
    private val dao: AdHocFlightDao,
    private val owners: Flow<String?>,
    private val currentOwner: suspend () -> String?,
    private val currentUser: suspend () -> String?,
    private val currentDisplayName: suspend () -> String?,
    private val online: () -> Boolean,
    private val fetchContext: suspend () -> AdHocFlightContext,
    private val create: suspend (String, AdHocFlightInput) -> FlightDetail,
    private val update: suspend (String, AdHocFlightInput, String) -> FlightDetail,
    private val gate: FlightOperationsGate,
    private val commit: suspend (suspend () -> Unit, suspend () -> Unit, suspend () -> Unit) -> Unit,
    private val holdWork: suspend (String) -> String?,
    private val releaseWork: suspend (String, String, String) -> Unit,
    private val removeWork: suspend (String) -> Unit,
    private val hasWork: suspend (String) -> Boolean,
    private val transaction: suspend (suspend () -> Unit) -> Unit,
    private val schedule: () -> Unit,
    private val now: () -> String = { Instant.now().toString() },
    private val json: Json = HttpClientFactory.json,
    private val onPlanningAcknowledged: suspend (String, String, String) -> Unit = { _, _, _ -> },
    private val holdWorkForReview: suspend (String, String) -> Unit = { _, _ -> },
    private val onLocalSaved: suspend (FlightDetail) -> Unit = {},
    private val onAcknowledgedDetail: suspend (FlightDetail) -> Unit = {},
) {
    @Volatile private var generation = 0L
    val context: Flow<AdHocFlightContext?> = owners.flatMapLatest { owner ->
        if (owner == null) flowOf(null) else dao.observeContext(owner).map { decodeContext(it) }
    }
    val pending: Flow<List<AdHocFlightPendingEntity>> = owners.flatMapLatest { owner ->
        if (owner == null) flowOf(emptyList()) else dao.observe(owner).map { rows -> rows.filter { it.status != "acknowledged" } }
    }
    /** Only changes to the visible local source produce a hint; initial state and HTTP-attempt bookkeeping do not. */
    val localChanges: Flow<Unit> = owners.distinctUntilChanged().flatMapLatest { owner ->
        if (owner == null) emptyFlow() else combine(dao.observe(owner), dao.observeContext(owner)) { rows, savedContext ->
            val station = decodeContext(savedContext)?.stationId
            rows.filter { !it.viewRevoked && it.stationId == station }.sortedBy { it.flightId }.map {
                listOf(it.flightId, it.inputJson, it.localDetailJson, it.materializedDetailJson)
            }
        }.distinctUntilChanged().drop(1).map { Unit }
    }

    private fun decodeContext(row: AdHocFlightContextEntity?) = row?.let {
        runCatching { json.decodeFromString<AdHocFlightContext>(it.payloadJson) }.getOrNull()?.takeIf { it.isValid() }
    }
    suspend fun cachedContext(): AdHocFlightContext? = currentOwner()?.let { decodeContext(dao.context(it)) }

    suspend fun refreshContext() = gate.withLock {
        if (!online()) return@withLock
        val owner = currentOwner() ?: return@withLock
        val observed = generation
        try {
            val value = fetchContext()
            check(value.isValid()) { "AdHoc station information is unavailable." }
            if (owner == currentOwner() && observed == generation) dao.putContext(AdHocFlightContextEntity(owner, json.encodeToString(value)))
        } catch (cancelled: CancellationException) { throw cancelled }
        catch (error: Exception) {
            if (observed == generation && error is ApiException && error.statusCode in listOf(401, 403))
                revokeForOwner(owner)
            throw error
        }
    }

    suspend fun newFlight(id: String, input: AdHocFlightInput): FlightDetail = gate.withLock {
        val context = checkNotNull(cachedContext()) { "Connect and synchronize AdHoc station information first." }
        check(context.canCreate) { "AdHoc creation is not permitted." }
        FlightDetail(id, input.flightNumber, input.customerId, customerName = "", stationId = context.stationId,
            stationIata = context.stationIata, stationName = context.stationName, operationTypeId = ADHOC_OPERATION_TYPE_ID,
            operationTypeName = "AdHoc", scheduledArrivalUtc = input.scheduledArrivalUtc,
            scheduledDepartureUtc = input.scheduledDepartureUtc, isPerLanding = false, createdAtUtc = now(),
            rowVersion = LOCAL_ADHOC_VERSION, stationTimeZoneId = context.stationTimeZoneId,
            currentStaffMemberId = context.currentStaffMemberId, canSaveArrival = context.canSaveArrival,
            canSaveDeparture = context.canSaveDeparture, canSaveServices = context.canSaveServices,
            canSaveTasks = context.canSaveTasks, allowedServiceIds = context.allowedServiceIds,
            isAdHocFlight = true, createdByUserId = currentUser(), createdByDisplayName = currentDisplayName(), canEditFlightDetails = true)
    }

    suspend fun row(id: String): AdHocFlightPendingEntity? = currentOwner()?.let { owner -> dao.rows(owner).firstOrNull { it.flightId == id } }
    suspend fun isUnmaterialized(id: String): Boolean = row(id)?.let { it.materializedDetailJson == null } == true
    suspend fun hasPendingIdentity(id: String): Boolean = row(id)?.status?.let { it != "acknowledged" } == true

    /** Explicit local source, requiring this account's current cached creation station and normal rolling window. */
    suspend fun localFlights(): List<FlightDetail> {
        val owner = currentOwner() ?: return emptyList()
        val context = decodeContext(dao.context(owner)) ?: return emptyList()
        val observed = generation
        val details = dao.rows(owner).filter { !it.viewRevoked && it.stationId == context.stationId }.mapNotNull { row ->
            localDetail(row)?.takeIf { it.isInFlightWindow(Instant.parse(now())) }?.let { detail ->
                detail.copy(canEditFlightDetails = detail.canEditFlightDetails && !hasWork(row.flightId))
            }
        }
        return details.takeIf { owner == currentOwner() && observed == generation &&
            decodeContext(dao.context(owner))?.stationId == context.stationId }.orEmpty()
    }
    suspend fun localFlight(id: String): FlightDetail? = localFlights().firstOrNull { it.id == id }
    suspend fun recoveryFlight(id: String): FlightDetail? = gate.withLock {
        val row = row(id) ?: return@withLock null
        val detail = localDetail(row) ?: return@withLock null
        val context = cachedContext()
        if (!row.viewRevoked && context?.stationId == row.stationId && detail.isInFlightWindow(Instant.parse(now())) &&
            (row.materializedDetailJson != null || context.canCreate))
            detail.copy(canEditFlightDetails = detail.canEditFlightDetails && !hasWork(id))
        else detail.copy(aircraftTypeId = null, aircraftTypeModel = null, plannedServices = emptyList(), assignedEmployees = emptyList(),
            arrivalSubmissions = emptyList(), departureSubmissions = emptyList(), services = emptyList(), tasks = emptyList(),
            cancellationSubmissions = emptyList(), hasCancellationSubmission = false, canSaveArrival = false,
            canSaveDeparture = false, canSaveServices = false, canSaveTasks = false, canSaveCancellation = false,
            allowedServiceIds = emptyList(), canEditFlightDetails = false)
    }
    private fun localDetail(row: AdHocFlightPendingEntity): FlightDetail? = runCatching {
        val identity = json.decodeFromString<FlightDetail>(row.localDetailJson)
        val detail = row.materializedDetailJson?.let { json.decodeFromString<FlightDetail>(it) } ?: identity
        val input = json.decodeFromString<AdHocFlightInput>(row.inputJson)
        detail.copy(customerId = input.customerId, customerName = identity.customerName, customerIataCode = identity.customerIataCode,
            flightNumber = input.flightNumber, scheduledArrivalUtc = input.scheduledArrivalUtc, scheduledDepartureUtc = input.scheduledDepartureUtc, fromCache = true)
    }.getOrNull()

    /** The coordinator owns the outer transaction and defers media cleanup until every write commits. */
    suspend fun save(flight: FlightDetail, raw: AdHocFlightInput, saveSections: suspend () -> Unit) = gate.withLock {
        val owner = checkNotNull(currentOwner()) { "Sign in to save this flight." }
        val input = raw.normalized()
        check(input.validationMessage() == null) { checkNotNull(input.validationMessage()) }
        val previous = row(flight.id)
        val original = previous?.let { json.decodeFromString<AdHocFlightInput>(it.inputJson) }
        val identityChanged = input != original && (previous != null || flight.rowVersion == LOCAL_ADHOC_VERSION ||
            input != AdHocFlightInput(flight.customerId, flight.flightNumber, flight.scheduledArrivalUtc, flight.scheduledDepartureUtc).normalized())
        if (identityChanged) check(flight.canEditFlightDetails && !hasWork(flight.id)) { "Flight details lock after Arrival or work is saved." }
        val needsPlanning = identityChanged || previous?.status != "acknowledged" && previous != null
        if (!needsPlanning) { commit({}, saveSections, {}); return@withLock }
        val context = checkNotNull(cachedContext()) { "Connect to verify your current station." }
        check(context.stationId == flight.stationId) { "Your station changed. Retained input is kept for Review." }
        check(flight.copy(scheduledArrivalUtc = input.scheduledArrivalUtc, scheduledDepartureUtc = input.scheduledDepartureUtc)
            .isInFlightWindow(Instant.parse(now()))) { "AdHoc flight times must overlap the current 12-hour flight window." }
        if (previous == null && flight.rowVersion == LOCAL_ADHOC_VERSION) check(context.canCreate) { "AdHoc creation is not permitted." }
        val detail = flight.copy(customerId = input.customerId, flightNumber = input.flightNumber,
            scheduledArrivalUtc = input.scheduledArrivalUtc, scheduledDepartureUtc = input.scheduledDepartureUtc)
        val next = previous?.copy(inputJson = json.encodeToString(input), localDetailJson = json.encodeToString(detail),
            savedAtUtc = now(), status = "pending", error = null, viewRevoked = false,
            attemptOperation = previous.attemptOperation.takeUnless { previous.attemptRejected },
            attemptInputJson = previous.attemptInputJson.takeUnless { previous.attemptRejected },
            attemptRowVersion = previous.attemptRowVersion.takeUnless { previous.attemptRejected }, attemptRejected = false)
            ?: AdHocFlightPendingEntity(owner, flight.id, context.stationId, json.encodeToString(input), json.encodeToString(detail),
                flight.rowVersion, now(), materializedDetailJson = if (flight.rowVersion == LOCAL_ADHOC_VERSION) null else json.encodeToString(flight))
        commit({ check(owner == currentOwner()); dao.put(next); onLocalSaved(detail) }, saveSections, {
            holdWork(flight.id)?.let { version -> row(flight.id)?.let { dao.put(it.copy(operationsBaseVersion = version)) } }
        })
        schedule()
    }

    suspend fun retry(id: String) = gate.withLock {
        row(id)?.let { row ->
            dao.put(row.copy(status = "pending", error = null,
                attemptOperation = row.attemptOperation.takeUnless { row.attemptRejected },
                attemptInputJson = row.attemptInputJson.takeUnless { row.attemptRejected },
                attemptRowVersion = row.attemptRowVersion.takeUnless { row.attemptRejected }, attemptRejected = false))
            schedule()
        }
    }
    suspend fun discard(id: String) = gate.withLock {
        val row = row(id) ?: return@withLock
        check(row.attemptInputJson == null || row.attemptRejected) { "This creation may already be recorded online. Retry synchronization before discarding it." }
        val materialized = row.materializedDetailJson?.let { json.decodeFromString<FlightDetail>(it) }
        commit({ dao.delete(row.ownerSubject, id) }, {
            if (materialized == null) removeWork(id)
            else releaseWork(id, row.operationsBaseVersion, materialized.rowVersion)
        }, {})
    }

    /** Freeze exact creation/details requests before send. A lost response never changes their fingerprint. */
    suspend fun syncPending(): Boolean = gate.withLock {
        val owner = currentOwner() ?: return@withLock false
        if (!online()) return@withLock dao.rows(owner).any { it.status == "pending" }
        var retry = false
        for (snapshot in dao.rows(owner).filter { it.status == "pending" }) {
            if (owner != currentOwner()) return@withLock false
            var acknowledged = false
            try {
                val context = checkNotNull(cachedContext()) { "Connect to verify your current station." }
                check(snapshot.stationId == context.stationId) { "Your station changed. Retained input is kept for Review." }
                val materialized = snapshot.materializedDetailJson?.let { json.decodeFromString<FlightDetail>(it) }
                val row = if (snapshot.attemptInputJson != null) snapshot else snapshot.copy(
                    attemptOperation = if (materialized == null) "create" else "details", attemptInputJson = snapshot.inputJson,
                    attemptRowVersion = materialized?.rowVersion).also { dao.put(it) }
                val input = json.decodeFromString<AdHocFlightInput>(checkNotNull(row.attemptInputJson))
                val result = if (row.attemptOperation == "create") create(row.flightId, input)
                    else update(row.flightId, input, checkNotNull(row.attemptRowVersion))
                acknowledged = true
                if (owner != currentOwner()) return@withLock false
                check(result.id == row.flightId && result.stationId == row.stationId && result.isAdHocFlight) { "AdHoc acknowledgment did not match this flight." }
                val initialCreation = row.attemptOperation == "create"
                val samePlanning = AdHocFlightInput(result.customerId, result.flightNumber, result.scheduledArrivalUtc,
                    result.scheduledDepartureUtc).normalized() == input.normalized()
                val emptyWork = result.canEditFlightDetails && result.arrivalSubmissions.isEmpty() && result.departureSubmissions.isEmpty() &&
                    result.services.isEmpty() && result.tasks.isEmpty() && result.cancellationSubmissions.isEmpty() && !result.hasCancellationSubmission
                val changedCreation = initialCreation && (!samePlanning || !emptyWork)
                transaction {
                    val latest = this.row(row.flightId) ?: return@transaction
                    val complete = latest.inputJson == row.attemptInputJson
                    val adoptCurrentPlanning = complete && initialCreation && !samePlanning
                    dao.put(latest.copy(materializedDetailJson = json.encodeToString(result), status = if (complete) "acknowledged" else "pending",
                        inputJson = if (adoptCurrentPlanning) json.encodeToString(AdHocFlightInput(result.customerId, result.flightNumber,
                            result.scheduledArrivalUtc, result.scheduledDepartureUtc).normalized()) else latest.inputJson,
                        localDetailJson = if (adoptCurrentPlanning) json.encodeToString(result) else latest.localDetailJson,
                        acknowledgedCreationInputJson = if (initialCreation) row.attemptInputJson else latest.acknowledgedCreationInputJson,
                        error = null, attemptOperation = null, attemptInputJson = null, attemptRowVersion = null, attemptRejected = false))
                    if (changedCreation) holdWorkForReview(row.flightId,
                        "This created flight changed online. Review the saved operational input before synchronizing it.")
                    else if (complete) releaseWork(row.flightId, latest.operationsBaseVersion, result.rowVersion)
                }
                onAcknowledgedDetail(result)
                if (!changedCreation) onPlanningAcknowledged(row.flightId, materialized?.rowVersion ?: LOCAL_ADHOC_VERSION, result.rowVersion)
                retry = retry || this.row(row.flightId)?.status == "pending"
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (error: Exception) {
                if (owner != currentOwner()) return@withLock false
                val transient = acknowledged || error is IOException || error is HttpRequestTimeoutException || error is SerializationException ||
                    error is ApiException && (error.statusCode >= 500 || error.statusCode in listOf(401, 408, 429))
                val rejected = !acknowledged && error is ApiException && error.statusCode in 400..499 && error.statusCode !in listOf(401, 408, 429)
                this.row(snapshot.flightId)?.let { current -> dao.put(current.copy(status = if (transient) "pending" else "failed",
                    error = if (transient) "Saved on this device. Waiting to synchronize the AdHoc flight." else error.userMessage(),
                    attemptRejected = current.attemptRejected || rejected)) }
                retry = retry || transient
            }
        }
        retry
    }

    /** Keep an ACK fallback until the complete prepared manifest owns this flight's current detail. */
    suspend fun reconcileManifest(ids: Set<String>, complete: Boolean) {
        val owner = currentOwner() ?: return
        transaction {
            if (owner != currentOwner()) return@transaction
            for (row in dao.rows(owner).filter { it.materializedDetailJson != null }) {
                if (complete && row.status == "acknowledged" && row.flightId in ids) dao.delete(owner, row.flightId)
                else if (row.flightId !in ids) dao.put(row.copy(viewRevoked = true))
            }
        }
    }
    suspend fun recordAcknowledged(acknowledged: FlightDetail) {
        val detail = acknowledged.restrictCapabilitiesAfterKnownWork()
        val owner = currentOwner() ?: return
        transaction {
            if (owner == currentOwner()) row(detail.id)?.takeIf { it.materializedDetailJson != null }
                ?.let { dao.put(it.copy(materializedDetailJson = json.encodeToString(detail))) }
        }
    }
    suspend fun confirmStation(stationId: String) {
        val owner = currentOwner() ?: return
        transaction {
            if (owner == currentOwner() && decodeContext(dao.context(owner))?.stationId?.let { it != stationId } == true)
                dao.deleteContext(owner)
        }
    }
    suspend fun revoke(id: String? = null) {
        val owner = currentOwner() ?: return
        revokeForOwner(owner, id)
    }
    suspend fun revokeForOwner(owner: String, id: String? = null) {
        val active = currentOwner()
        if (active != owner && active != null) return
        transaction {
            val current = currentOwner()
            if (current != owner && current != null) return@transaction
            if (id == null) {
                dao.deleteContext(owner)
                for (row in dao.rows(owner).filter { it.materializedDetailJson != null }) dao.put(row.copy(viewRevoked = true))
            } else dao.rows(owner).firstOrNull { it.flightId == id }?.let { dao.put(it.copy(viewRevoked = true)) }
        }
    }
    suspend fun clear() = gate.withLock { generation++; dao.clearRows(); dao.clearContexts() }
}
