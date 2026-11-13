package com.nags.operations.data.repo

import com.nags.operations.data.ApiException
import com.nags.operations.data.FlightArrivalSaved
import com.nags.operations.data.FlightArrivalSubmission
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.SaveFlightArrivalRequest
import com.nags.operations.data.arrivalEditable
import com.nags.operations.data.canUpdateArrivalSubmission
import com.nags.operations.data.validArrivalId
import com.nags.operations.data.db.dao.FlightArrivalDao
import com.nags.operations.data.db.entities.FlightArrivalPendingEntity
import com.nags.operations.data.isInFlightWindow
import com.nags.operations.data.validationErrors
import com.nags.operations.data.userMessage
import io.ktor.client.plugins.HttpRequestTimeoutException
import java.io.IOException
import java.time.Instant
import java.time.OffsetDateTime
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
    private val hasSavedCancellation: suspend (String) -> Boolean = { false },
    private val submissionGate: Mutex = Mutex(),
    private val fetchFlight: suspend (String) -> FlightDetail = { error("Reload the flight before synchronizing arrival.") },
    private val hasUnacknowledgedOtherWork: suspend (String) -> Boolean = { false },
    private val hasSavedServices: suspend (String) -> Boolean = { false },
    private val grouped: suspend (String, String?) -> Boolean = { _, _ -> false },
    private val groupedRetry: suspend (String, String?) -> Boolean = { _, _ -> false },
    private val groupedDiscard: suspend (String, String?) -> Boolean = { _, _ -> false },
    private val operationWrite: suspend (suspend () -> Unit) -> Unit = { it() },
    private val operationSave: suspend (suspend () -> Unit) -> Unit = operationWrite,
    private val operationGate: FlightOperationsGate = FlightOperationsGate(),
    private val onAttemptRejected: suspend (String, String?) -> Unit = { _, _ -> },
) {
    // Network sends and session transitions share this gate. A new bearer cannot be
    // published while an old account's request is still using the HTTP client.
    private val gate = Mutex()
    private val localWrites = FlightOperationsLocalLock(operationGate)
    @Volatile private var accepting = true
    @Volatile private var generation = 0L
    // Only exact acknowledgments of this session's writes can advance a reviewed version.
    private data class Acknowledgment(val version: String, val arrival: FlightArrivalSubmission? = null,
        val targetSubmissionId: String? = null)
    private var acknowledgedOwner: String? = null
    private val acknowledgedVersions = linkedMapOf<String, LinkedHashMap<String, Acknowledgment>>()
    val changes: Flow<List<FlightArrivalPendingEntity>> = ownerFlow.flatMapLatest { owner ->
        if (owner == null) flowOf(emptyList()) else dao.observe(owner)
    }

    suspend fun save(flight: FlightDetail, input: SaveFlightArrivalRequest) = operationSave { saveWithinGate(flight, input) }
    private suspend fun saveWithinGate(flight: FlightDetail, input: SaveFlightArrivalRequest) {
        val owner = checkNotNull(currentOwner()) { "Sign in to save arrival details." }
        val observed = generation
        val normalized = input.normalized()
        check(normalized.validationErrors(flight.rowVersion).isEmpty()) { "Complete the required arrival fields." }
        check(if (normalized.targetSubmissionId == null) flight.arrivalEditable()
            else validArrivalId(normalized.targetSubmissionId) && flight.canUpdateArrivalSubmission(normalized.targetSubmissionId)) {
            "You are not eligible to update this arrival submission."
        }
        check(flight.isInFlightWindow(now())) { "This flight is outside your current flight window." }
        submissionGate.withLock {
            check(!flight.hasCancellationSubmission || normalized.acknowledgeConflictingSubmission) {
                "Another employee reported this flight as canceled. Confirm arrival before saving."
            }
            check(!hasSavedCancellation(flight.id)) { "Revert your cancellation and wait for it to sync before saving work on this flight." }
            localWrites.withLock {
                check(accepting && generation == observed && owner == currentOwner()) { "Your session changed. Reopen the flight." }
                val retained = dao.rows(owner).firstOrNull { it.flightId == flight.id }
                check(retained == null || runCatching { json.decodeFromString<SaveFlightArrivalRequest>(retained.inputJson)
                    .targetSubmissionId.equals(normalized.targetSubmissionId, true) }.getOrDefault(false)) {
                    "Sync or discard the saved arrival before updating a different submission."
                }
                val existing = retained?.takeIf { it.status == "pending" }
                // Editing changes the desired input, never the request whose outcome is unknown.
                val desired = existing?.copy(inputJson = json.encodeToString(normalized), savedAtUtc = now().toString(), error = null)
                    ?: FlightArrivalPendingEntity(owner, flight.id, flight.flightNumber, UUID.randomUUID().toString(),
                        json.encodeToString(normalized), flight.rowVersion, now().toString(),
                        reviewedArrivalJson = json.encodeToString(reviewedArrival(flight, normalized.targetSubmissionId)), reviewSourceInputJson = retained?.reviewSourceInputJson)
                dao.put(advanceAcknowledged(desired))
            }
        }
        // WorkManager scheduling failure must not turn a completed disk save into a
        // failed form save. Session/reconnect/poll refresh also drains persisted rows.
        runCatching { schedule() }
    }

    /** True means transient work remains; terminal rows stay available for review. */
    suspend fun syncPending(priorToServices: Boolean = false, onAcknowledged: (String, String) -> Unit = { _, _ -> }): Boolean = gate.withLock {
        if (!accepting) return@withLock false
        val owner = currentOwner() ?: return@withLock false
        if (!online()) return@withLock dao.rows(owner).any { it.status == "pending" }
        var retry = false
        val blocked = mutableSetOf<String>()
        for (snapshot in localWrites.withLock { dao.rows(owner).filter { it.status == "pending" } }) {
            if (!accepting || owner != currentOwner()) return@withLock false
            if (snapshot.attemptInputJson == null && grouped(snapshot.flightId, null)) { blocked += snapshot.flightId; continue }
            if (snapshot.attemptInputJson == null && !relaxesArrival(snapshot) &&
                (priorToServices || hasSavedServices(snapshot.flightId))) {
                blocked += snapshot.flightId
                continue
            }
            // Resolve an unknown older work request before transmitting a new arrival version.
            // An already frozen arrival always replays its original request, avoiding a dependency cycle.
            if (snapshot.attemptInputJson == null && hasUnacknowledgedOtherWork(snapshot.flightId)) {
                blocked += snapshot.flightId
                continue
            }
            var didSend = false
            var acknowledged = false
            try {
                val row = prepare(snapshot) ?: continue
                val input = runCatching { json.decodeFromString<SaveFlightArrivalRequest>(checkNotNull(row.attemptInputJson)) }.getOrNull()
                if (input == null || input.validationErrors(row.rowVersion).isNotEmpty()) {
                    updateIfPending(row, row.copy(status = "failed", error = "The saved arrival is incomplete. Reload the flight and review it."))
                    continue
                }
                didSend = true
                val result = send(row.flightId, input, row.rowVersion)
                acknowledged = true
                if (owner != currentOwner()) return@withLock false
                accepted(row.flightId, result)
                // Publish our exact ACK before the dependent-queue callback. Departure saves
                // recheck this lineage after persistence, closing the concurrent-save gap.
                localWrites.withLock { rememberAcknowledgment(owner, row.flightId, row.rowVersion, result.rowVersion,
                    result.submission, input.targetSubmissionId) }
                // Persist dependent departure intent before deleting arrival intent. Replaying this
                // exact arrival after a crash can safely repeat the dependency acknowledgement.
                onAcknowledgedBeforeDelete(row.flightId, row.rowVersion, result.rowVersion)
                localWrites.withLock {
                    pendingRow(row)?.let { latest ->
                        if (sameInput(latest.inputJson, row.attemptInputJson)) dao.delete(owner, row.flightId, row.changeId)
                        else {
                            // Only acknowledgment of our exact earlier request advances a correction.
                            // External conflicts still require explicit review; they are never rebased.
                            dao.put(latest.copy(rowVersion = result.rowVersion, attemptInputJson = null, error = null,
                                reviewedArrivalJson = json.encodeToString<FlightArrivalSubmission?>(result.submission), needsArrivalComparison = false))
                        }
                    }
                }
                onAcknowledged(row.flightId, result.rowVersion)
            } catch (cancelled: CancellationException) {
                throw cancelled // A cancelled send remains pending and replays safely.
            } catch (error: Exception) {
                if (owner != currentOwner()) return@withLock false
                val rejected = didSend && !acknowledged && error is ApiException && error.statusCode in 400..499 && error.statusCode !in listOf(401, 408, 429)
                if (rejected) onAttemptRejected(snapshot.flightId, null)
                val transient = acknowledged || error is IOException || error is HttpRequestTimeoutException || error is SerializationException ||
                    (error is ApiException && (error.statusCode >= 500 || error.statusCode in listOf(408, 429)))
                val status = if (transient || (error is ApiException && error.statusCode == 401)) "pending"
                    else if (error is ApiException && error.statusCode in listOf(409, 412)) "conflict" else "failed"
                val message = when (status) {
                    "pending" -> "Saved on this device. Waiting to sync; reconnect or sign in again."
                    "conflict" -> "The flight changed. Reload it and review your saved arrival before saving again."
                    else -> if (error is ApiException) error.userMessage()
                        else "Arrival was not accepted. Reload the flight to check access and arrival details."
                }
                updateIfPending(snapshot, snapshot.copy(status = status, error = message), clearRejectedAttempt = rejected)
                retry = retry || transient
                if (error is ApiException && error.statusCode == 401) return@withLock false
            }
        }
        retry || localWrites.withLock { dao.rows(owner).any { it.status == "pending" && it.flightId !in blocked } }
    }

    private fun reviewedArrival(flight: FlightDetail, targetSubmissionId: String?): FlightArrivalSubmission? = flight.arrivalSubmissions.firstOrNull {
        if (targetSubmissionId == null) it.submittedByEmployee.staffMemberId.equals(flight.currentStaffMemberId, true)
        else it.id.equals(targetSubmissionId, true)
    }

    /** Advance only known, unsent intent; a request with an unknown result retains its exact version. */
    private fun advanceAcknowledged(row: FlightArrivalPendingEntity): FlightArrivalPendingEntity {
        resetAcknowledgmentsForOwner(row.ownerSubject)
        if (row.attemptInputJson != null || row.reviewedArrivalJson == null) return row
        val chain = acknowledgedVersions[row.flightId] ?: return row
        var version = row.rowVersion
        var reviewed = row.reviewedArrivalJson
        val targetSubmissionId = runCatching { json.decodeFromString<SaveFlightArrivalRequest>(row.inputJson).targetSubmissionId }.getOrNull()
        val visited = mutableSetOf<String>()
        while (visited.add(version)) {
            val next = chain[version] ?: break
            if (next.version == version) break
            next.arrival?.takeIf { next.targetSubmissionId.equals(targetSubmissionId, true) }
                ?.let { reviewed = json.encodeToString<FlightArrivalSubmission?>(it) }
            version = next.version
        }
        return if (version == row.rowVersion) row else row.copy(rowVersion = version,
            reviewedArrivalJson = reviewed, needsArrivalComparison = true)
    }

    suspend fun onPriorWriteAcknowledged(flightId: String, requestVersion: String, responseVersion: String) = localWrites.withLock {
        val owner = currentOwner() ?: return@withLock
        rememberAcknowledgment(owner, flightId, requestVersion, responseVersion)
        dao.rows(owner).firstOrNull { it.flightId == flightId && it.status == "pending" }?.let { row ->
            val advanced = advanceAcknowledged(row)
            if (advanced != row) dao.put(advanced)
        }
    }

    private suspend fun prepare(snapshot: FlightArrivalPendingEntity): FlightArrivalPendingEntity? {
        val original = localWrites.withLock { pendingRow(snapshot) } ?: return null
        if (original.attemptInputJson != null) return original
        if (original.needsArrivalComparison) {
            val current = fetchFlight(original.flightId)
            if (current.fromCache) throw IOException("Connect to reload the latest flight before synchronizing arrival.")
            val reviewed = original.reviewedArrivalJson?.let { json.decodeFromString<FlightArrivalSubmission?>(it) }
            val input = json.decodeFromString<SaveFlightArrivalRequest>(original.inputJson)
            val editable = input.targetSubmissionId?.let(current::canUpdateArrivalSubmission) ?: current.arrivalEditable()
            if (original.reviewedArrivalJson == null || !editable ||
                current.hasCancellationSubmission && !input.acknowledgeConflictingSubmission || reviewedArrival(current, input.targetSubmissionId) != reviewed)
                throw ApiException(409, "Arrival details changed. Reload the flight and review them before saving again.")
            // This read proves only the reviewed arrival is unchanged. Its rowversion is never adopted.
        }
        return localWrites.withLock {
            val latest = pendingRow(original) ?: return@withLock null
            if (latest.rowVersion != original.rowVersion || latest.reviewedArrivalJson != original.reviewedArrivalJson) return@withLock null
            latest.copy(attemptInputJson = latest.inputJson).also { dao.put(it) }
        }
    }

    /** Follow only exact versions returned by this session's own arrival HTTP acknowledgments. */
    suspend fun acknowledgedVersion(flightId: String, reviewedVersion: String): String = localWrites.withLock {
        resetAcknowledgmentsForOwner(currentOwner())
        val chain = acknowledgedVersions[flightId] ?: return@withLock reviewedVersion
        var version = reviewedVersion
        val visited = mutableSetOf<String>()
        while (visited.add(version)) {
            val next = chain[version] ?: break
            if (next.version == version) break
            version = next.version
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
    private fun rememberAcknowledgment(owner: String, flightId: String, requestVersion: String, responseVersion: String,
        arrival: FlightArrivalSubmission? = null, targetSubmissionId: String? = null) {
        resetAcknowledgmentsForOwner(owner)
        val chain = acknowledgedVersions.remove(flightId) ?: linkedMapOf()
        // A repeated peer callback must not erase proof of our own acknowledged arrival input.
        chain[requestVersion] = Acknowledgment(responseVersion, arrival ?: chain[requestVersion]?.arrival,
            if (arrival != null) targetSubmissionId else chain[requestVersion]?.targetSubmissionId)
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

    suspend fun hasUnacknowledgedAttempt(flightId: String): Boolean = localWrites.withLock {
        val owner = currentOwner() ?: return@withLock false
        dao.rows(owner).any { it.flightId == flightId && it.status == "pending" && it.attemptInputJson != null }
    }

    private fun relaxesArrival(row: FlightArrivalPendingEntity): Boolean = runCatching {
        val previous = row.reviewedArrivalJson?.let { json.decodeFromString<FlightArrivalSubmission?>(it) } ?: return@runCatching false
        val desired = json.decodeFromString<SaveFlightArrivalRequest>(row.inputJson)
        !OffsetDateTime.parse(desired.ata).toInstant().isAfter(OffsetDateTime.parse(previous.ata).toInstant())
    }.getOrDefault(false)

    /** New/later arrival bounds wait until their service corrections have synchronized. */
    suspend fun mustSyncBeforeServices(flightId: String): Boolean = localWrites.withLock {
        val owner = currentOwner() ?: return@withLock false
        dao.rows(owner).any { it.flightId == flightId && (it.status != "pending" || it.attemptInputJson != null || relaxesArrival(it)) }
    }

    suspend fun pendingInput(flightId: String): SaveFlightArrivalRequest? = localWrites.withLock {
        val owner = currentOwner() ?: return@withLock null
        dao.rows(owner).firstOrNull { it.flightId == flightId && it.status == "pending" }
            ?.let { runCatching { json.decodeFromString<SaveFlightArrivalRequest>(it.inputJson) }.getOrNull() }
    }

    /** Retry retains the exact request and original version; it never silently rebases a conflict. */
    suspend fun retry(flightId: String) = operationWrite { retryWithinGate(flightId) }
    private suspend fun retryWithinGate(flightId: String) {
        if (groupedRetry(flightId, null)) return
        val owner = currentOwner() ?: return
        val observed = generation
        localWrites.withLock {
            check(accepting && observed == generation && owner == currentOwner()) { "Your session changed. Reopen Sync Center." }
            val row = dao.rows(owner).firstOrNull { it.flightId == flightId } ?: return@withLock
            dao.put(row.copy(status = "pending", error = null))
        }
        runCatching { schedule() }
    }

    suspend fun discard(flightId: String) = operationWrite { discardWithinGate(flightId) }
    private suspend fun discardWithinGate(flightId: String) {
        if (groupedDiscard(flightId, null)) return
        val owner = currentOwner() ?: return
        val observed = generation
        localWrites.withLock {
            check(accepting && observed == generation && owner == currentOwner()) { "Your session changed. Reopen Sync Center." }
            val row = dao.rows(owner).firstOrNull { it.flightId == flightId } ?: return@withLock
            dao.delete(owner, row.flightId, row.changeId)
        }
    }

    private fun sameInput(first: String, second: String?): Boolean = runCatching {
        json.decodeFromString<SaveFlightArrivalRequest>(first).withoutAcknowledgement().normalized() ==
            json.decodeFromString<SaveFlightArrivalRequest>(checkNotNull(second)).withoutAcknowledgement().normalized()
    }.getOrDefault(false)

    private suspend fun pendingRow(row: FlightArrivalPendingEntity): FlightArrivalPendingEntity? =
        dao.rows(row.ownerSubject).firstOrNull { it.flightId == row.flightId && it.changeId == row.changeId && it.status == "pending" }

    private suspend fun updateIfPending(original: FlightArrivalPendingEntity, updated: FlightArrivalPendingEntity, clearRejectedAttempt: Boolean = false) = localWrites.withLock {
        // Discard/replacement may finish during an HTTP request. A late failure must
        // never resurrect discarded input or replace a newer local correction.
        pendingRow(original)?.let { latest -> dao.put(latest.copy(status = updated.status, error = updated.error, attemptInputJson = if (clearRejectedAttempt) null else latest.attemptInputJson)) }
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
