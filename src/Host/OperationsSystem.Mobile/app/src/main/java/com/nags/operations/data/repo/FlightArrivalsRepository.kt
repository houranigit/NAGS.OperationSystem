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
    private val onAcknowledgedBeforeDelete: suspend (String, String, String) -> Unit = { _, _, _ -> },
) {
    // Network sends and session transitions share this gate. A new bearer cannot be
    // published while an old account's request is still using the HTTP client.
    private val gate = Mutex()
    private val localWrites = Mutex()
    @Volatile private var accepting = true
    @Volatile private var generation = 0L
    // Only our own acknowledged arrival writes may advance an open departure form's reviewed version.
    // This covers a background worker acknowledging a queued arrival before the form starts its save.
    private var acknowledgedOwner: String? = null
    private val acknowledgedVersions = linkedMapOf<String, LinkedHashMap<String, String>>()
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
            val existing = dao.rows(owner).firstOrNull { it.flightId == flight.id && it.status == "pending" }
            // Editing changes the desired input, never the request whose outcome is unknown.
            dao.put(existing?.copy(inputJson = json.encodeToString(normalized), savedAtUtc = now().toString(), error = null)
                ?: FlightArrivalPendingEntity(owner, flight.id, flight.flightNumber, UUID.randomUUID().toString(),
                    json.encodeToString(normalized), flight.rowVersion, now().toString()))
        }
        // WorkManager scheduling failure must not turn a completed disk save into a
        // failed form save. Session/reconnect/poll refresh also drains persisted rows.
        runCatching { schedule() }
    }

    /** True means transient work remains; terminal rows stay available for review. */
    suspend fun syncPending(onAcknowledged: (String, String) -> Unit = { _, _ -> }): Boolean = gate.withLock {
        if (!accepting) return@withLock false
        val owner = currentOwner() ?: return@withLock false
        if (!online()) return@withLock dao.rows(owner).any { it.status == "pending" }
        var retry = false
        for (snapshot in localWrites.withLock { dao.rows(owner).filter { it.status == "pending" } }) {
            if (!accepting || owner != currentOwner()) return@withLock false
            val row = localWrites.withLock {
                val current = pendingRow(snapshot) ?: return@withLock null
                current.copy(attemptInputJson = current.attemptInputJson ?: current.inputJson).also { dao.put(it) }
            } ?: continue
            val input = runCatching { json.decodeFromString<SaveFlightArrivalRequest>(checkNotNull(row.attemptInputJson)) }.getOrNull()
            if (input == null || input.validationErrors(row.rowVersion).isNotEmpty()) {
                updateIfPending(row, row.copy(status = "failed", error = "The saved arrival is incomplete. Reload the flight and review it."))
                continue
            }
            try {
                val result = send(row.flightId, input, row.rowVersion)
                if (owner != currentOwner()) return@withLock false
                accepted(row.flightId, result)
                // Publish our exact ACK before the dependent-queue callback. Departure saves
                // recheck this lineage after persistence, closing the concurrent-save gap.
                localWrites.withLock { rememberAcknowledgment(owner, row.flightId, row.rowVersion, result.rowVersion) }
                // Persist dependent departure intent before deleting arrival intent. Replaying this
                // exact arrival after a crash can safely repeat the dependency acknowledgement.
                onAcknowledgedBeforeDelete(row.flightId, row.rowVersion, result.rowVersion)
                localWrites.withLock {
                    pendingRow(row)?.let { latest ->
                        if (latest.inputJson == row.attemptInputJson) dao.delete(owner, row.flightId, row.changeId)
                        else {
                            // Only acknowledgment of our exact earlier request advances a correction.
                            // External conflicts still require explicit review; they are never rebased.
                            dao.put(latest.copy(rowVersion = result.rowVersion, attemptInputJson = null, error = null))
                        }
                    }
                }
                onAcknowledged(row.flightId, result.rowVersion)
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

    /** Follow only exact versions returned by this session's own arrival HTTP acknowledgments. */
    suspend fun acknowledgedVersion(flightId: String, reviewedVersion: String): String = localWrites.withLock {
        resetAcknowledgmentsForOwner(currentOwner())
        val chain = acknowledgedVersions[flightId] ?: return@withLock reviewedVersion
        var version = reviewedVersion
        val visited = mutableSetOf<String>()
        while (visited.add(version)) {
            val next = chain[version] ?: break
            if (next == version) break
            version = next
        }
        version
    }

    private fun resetAcknowledgmentsForOwner(owner: String?) {
        if (acknowledgedOwner != owner) {
            acknowledgedVersions.clear()
            acknowledgedOwner = owner
        }
    }

    /** Caller holds localWrites; retain a bounded session-only lineage, never a server refresh version. */
    private fun rememberAcknowledgment(owner: String, flightId: String, requestVersion: String, responseVersion: String) {
        resetAcknowledgmentsForOwner(owner)
        val chain = acknowledgedVersions.remove(flightId) ?: linkedMapOf()
        chain[requestVersion] = responseVersion
        while (chain.size > 64) chain.remove(chain.keys.first())
        acknowledgedVersions[flightId] = chain
        while (acknowledgedVersions.size > 64) acknowledgedVersions.remove(acknowledgedVersions.keys.first())
    }

    /** Online departure writes share the existing account-transition gate with arrival uploads. */
    suspend fun <T> withCurrentSession(block: suspend () -> T): T = gate.withLock {
        check(accepting && currentOwner() != null) { "Your session changed. Reopen the flight." }
        block()
    }

    suspend fun hasSavedInput(flightId: String): Boolean = localWrites.withLock {
        val owner = currentOwner() ?: return@withLock false
        dao.rows(owner).any { it.flightId == flightId }
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

    private suspend fun pendingRow(row: FlightArrivalPendingEntity): FlightArrivalPendingEntity? =
        dao.rows(row.ownerSubject).firstOrNull { it.flightId == row.flightId && it.changeId == row.changeId && it.status == "pending" }

    private suspend fun updateIfPending(original: FlightArrivalPendingEntity, updated: FlightArrivalPendingEntity) = localWrites.withLock {
        // Discard/replacement may finish during an HTTP request. A late failure must
        // never resurrect discarded input or replace a newer local correction.
        pendingRow(original)?.let { latest -> dao.put(latest.copy(status = updated.status, error = updated.error)) }
    }

    suspend fun pause() {
        localWrites.withLock { accepting = false; generation++ }
        gate.withLock {
            localWrites.withLock { acknowledgedVersions.clear(); acknowledgedOwner = null }
        }
    }
    suspend fun resume() = localWrites.withLock { accepting = true }
    suspend fun clear() = gate.withLock { localWrites.withLock { generation++; acknowledgedVersions.clear(); acknowledgedOwner = null; dao.clear() } }
}
