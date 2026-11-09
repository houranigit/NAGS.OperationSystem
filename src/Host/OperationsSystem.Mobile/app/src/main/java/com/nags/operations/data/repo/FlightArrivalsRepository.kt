package com.nags.operations.data.repo

import com.nags.operations.data.ApiException
import com.nags.operations.data.FlightArrivalSaved
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.SaveFlightArrivalRequest
import com.nags.operations.data.arrivalEditable
import com.nags.operations.data.db.dao.FlightArrivalDao
import com.nags.operations.data.db.entities.FlightArrivalPendingEntity
import com.nags.operations.data.isInFlightWindow
import com.nags.operations.data.validationErrors
import com.nags.operations.data.userMessage
import io.ktor.client.plugins.HttpRequestTimeoutException
import java.io.IOException
import java.time.Instant
import java.util.UUID
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.serialization.SerializationException
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

/** Local-first arrival saves. Snapshots never own or replace this durable intent. */
@OptIn(ExperimentalCoroutinesApi::class)
class FlightArrivalsRepository(
    private val dao: FlightArrivalDao,
    ownerFlow: Flow<String?>,
    private val currentOwner: suspend () -> String?,
    private val online: () -> Boolean,
    private val send: suspend (String, SaveFlightArrivalRequest, String) -> FlightArrivalSaved,
    private val accepted: suspend (String, FlightArrivalSaved) -> Unit,
    private val schedule: () -> Unit,
    private val json: Json,
    private val now: () -> Instant = Instant::now,
) {
    // Network sends and session transitions share this gate. A new bearer cannot be
    // published while an old account's request is still using the HTTP client.
    private val gate = Mutex()
    private val localWrites = Mutex()
    @Volatile private var accepting = true
    @Volatile private var generation = 0L
    val changes: Flow<List<FlightArrivalPendingEntity>> = ownerFlow.flatMapLatest { owner ->
        if (owner == null) flowOf(emptyList()) else dao.observe(owner)
    }

    suspend fun save(flight: FlightDetail, input: SaveFlightArrivalRequest) {
        val owner = checkNotNull(currentOwner()) { "Sign in to save arrival details." }
        val observed = generation
        val normalized = input.normalized()
        check(normalized.validationErrors(flight.rowVersion).isEmpty()) { "Complete the required arrival fields." }
        check(flight.arrivalEditable()) { "Only the employee who submitted the arrival can update it." }
        check(flight.isInFlightWindow(now())) { "This flight is outside your current flight window." }
        localWrites.withLock {
            check(accepting && generation == observed && owner == currentOwner()) { "Your session changed. Reopen the flight." }
            // Never replace an unresolved upload with another input: its success response
            // could have been lost. Resolve/retry it before recording a correction.
            check(dao.rows(owner).none { it.flightId == flight.id && it.status == "pending" }) { "This arrival is already saved locally. Resolve its sync status first." }
            dao.put(FlightArrivalPendingEntity(owner, flight.id, flight.flightNumber, UUID.randomUUID().toString(),
                json.encodeToString(normalized), flight.rowVersion, now().toString()))
        }
        // WorkManager scheduling failure must not turn a completed disk save into a
        // failed form save. Session/reconnect/poll refresh also drains persisted rows.
        runCatching { schedule() }
    }

    /** True means transient work remains; terminal rows stay available for review. */
    suspend fun syncPending(): Boolean = gate.withLock {
        if (!accepting) return@withLock false
        val owner = currentOwner() ?: return@withLock false
        if (!online()) return@withLock dao.rows(owner).any { it.status == "pending" }
        var retry = false
        for (row in localWrites.withLock { dao.rows(owner).filter { it.status == "pending" } }) {
            if (!accepting || owner != currentOwner()) return@withLock false
            if (!localWrites.withLock { isPending(row) }) continue
            val input = runCatching { json.decodeFromString<SaveFlightArrivalRequest>(row.inputJson) }.getOrNull()
            if (input == null || input.validationErrors(row.rowVersion).isNotEmpty()) {
                updateIfPending(row, row.copy(status = "failed", error = "The saved arrival is incomplete. Reload the flight and review it."))
                continue
            }
            try {
                val result = send(row.flightId, input, row.rowVersion)
                if (owner != currentOwner()) return@withLock false
                accepted(row.flightId, result)
                localWrites.withLock { dao.delete(owner, row.flightId, row.changeId) }
            } catch (cancelled: CancellationException) {
                throw cancelled // A cancelled send remains pending and replays safely.
            } catch (error: Exception) {
                if (owner != currentOwner()) return@withLock false
                val transient = error is IOException || error is HttpRequestTimeoutException || error is SerializationException ||
                    (error is ApiException && (error.statusCode >= 500 || error.statusCode in listOf(408, 429)))
                val status = if (transient || (error is ApiException && error.statusCode == 401)) "pending"
                    else if (error is ApiException && error.statusCode in listOf(409, 412)) "conflict" else "failed"
                val message = when (status) {
                    "pending" -> "Saved on this device. Waiting to sync; reconnect or sign in again."
                    "conflict" -> "The flight changed. Reload it and review your saved arrival before saving again."
                    else -> if (error is ApiException) error.userMessage()
                        else "Arrival was not accepted. Reload the flight to check access and arrival details."
                }
                updateIfPending(row, row.copy(status = status, error = message))
                retry = retry || transient
                if (error is ApiException && error.statusCode == 401) return@withLock false
            }
        }
        retry || localWrites.withLock { dao.rows(owner).any { it.status == "pending" } }
    }

    /** Retry retains the exact request and original version; it never silently rebases a conflict. */
    suspend fun retry(flightId: String) {
        val owner = currentOwner() ?: return
        val observed = generation
        localWrites.withLock {
            check(accepting && observed == generation && owner == currentOwner()) { "Your session changed. Reopen Sync Center." }
            val row = dao.rows(owner).firstOrNull { it.flightId == flightId } ?: return
            dao.put(row.copy(status = "pending", error = null))
        }
        runCatching { schedule() }
    }

    suspend fun discard(flightId: String) {
        val owner = currentOwner() ?: return
        val observed = generation
        localWrites.withLock {
            check(accepting && observed == generation && owner == currentOwner()) { "Your session changed. Reopen Sync Center." }
            val row = dao.rows(owner).firstOrNull { it.flightId == flightId } ?: return
            dao.delete(owner, row.flightId, row.changeId)
        }
    }

    private suspend fun isPending(row: FlightArrivalPendingEntity): Boolean =
        dao.rows(row.ownerSubject).any { it.flightId == row.flightId && it.changeId == row.changeId && it.status == "pending" }

    private suspend fun updateIfPending(original: FlightArrivalPendingEntity, updated: FlightArrivalPendingEntity) = localWrites.withLock {
        // Discard/replacement may finish during an HTTP request. A late failure must
        // never resurrect discarded input or replace a newer local correction.
        if (isPending(original)) dao.put(updated)
    }

    suspend fun pause() {
        localWrites.withLock { accepting = false; generation++ }
        gate.withLock { /* Wait for the current authenticated upload before changing bearer. */ }
    }
    suspend fun resume() = localWrites.withLock { accepting = true }
    suspend fun clear() = gate.withLock { localWrites.withLock { generation++; dao.clear() } }
}
