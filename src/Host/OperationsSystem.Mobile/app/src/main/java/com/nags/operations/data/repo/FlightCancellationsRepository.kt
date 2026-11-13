package com.nags.operations.data.repo

import com.nags.operations.data.ApiException
import com.nags.operations.data.FlightCancellationReverted
import com.nags.operations.data.FlightCancellationSaved
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.SaveFlightCancellationRequest
import com.nags.operations.data.cancellationEditable
import com.nags.operations.data.db.dao.FlightCancellationDao
import com.nags.operations.data.db.entities.FlightCancellationPendingEntity
import com.nags.operations.data.hasOwnWork
import com.nags.operations.data.isInFlightWindow
import com.nags.operations.data.ownCancellation
import com.nags.operations.data.userMessage
import io.ktor.client.plugins.HttpRequestTimeoutException
import java.io.IOException
import java.time.Instant
import java.time.OffsetDateTime
import java.util.Base64
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

/** Cancellation intent remains durable and mutually exclusive with this employee's work intent. */
@OptIn(ExperimentalCoroutinesApi::class)
class FlightCancellationsRepository(
    private val dao: FlightCancellationDao,
    ownerFlow: Flow<String?>,
    private val currentOwner: suspend () -> String?,
    private val online: () -> Boolean,
    private val send: suspend (String, SaveFlightCancellationRequest, String) -> FlightCancellationSaved,
    private val revert: suspend (String, String, String) -> FlightCancellationReverted,
    private val accepted: suspend (String, FlightCancellationSaved) -> Unit,
    private val onReverted: suspend (String, FlightCancellationReverted) -> Unit,
    private val schedule: () -> Unit,
    private val json: Json,
    private val hasSavedWork: suspend (String) -> Boolean,
    private val submissionGate: Mutex = Mutex(),
    private val now: () -> Instant = Instant::now,
    private val operationGate: FlightOperationsGate = FlightOperationsGate(),
    private val operationWrite: suspend (suspend () -> Unit) -> Unit = { it() },
) {
    private val gate = Mutex()
    private val localWrites = FlightOperationsLocalLock(operationGate)
    @Volatile private var accepting = true
    @Volatile private var generation = 0L
    val changes: Flow<List<FlightCancellationPendingEntity>> = ownerFlow.flatMapLatest { owner ->
        if (owner == null) flowOf(emptyList()) else dao.observe(owner)
    }

    suspend fun save(flight: FlightDetail, input: SaveFlightCancellationRequest) = operationWrite { saveWithinGate(flight, input) }
    private suspend fun saveWithinGate(flight: FlightDetail, input: SaveFlightCancellationRequest): Unit = submissionGate.withLock {
        val owner = checkNotNull(currentOwner()) { "Sign in to cancel this flight." }
        val observed = generation
        check(flight.cancellationEditable() && !flight.hasOwnWork() && !hasSavedWork(flight.id)) {
            "You have saved work on this flight and cannot also submit a cancellation."
        }
        check(flight.isInFlightWindow(now())) { "This flight is outside your current flight window." }
        val normalized = input.normalized()
        validate(normalized, flight.rowVersion)
        val otherWork = flight.arrivalSubmissions.any { !it.submittedByEmployee.staffMemberId.equals(flight.currentStaffMemberId, true) } ||
            flight.departureSubmissions.any { !it.submittedByEmployee.staffMemberId.equals(flight.currentStaffMemberId, true) }
        check(!otherWork || normalized.acknowledgeConflictingSubmission) {
            "Another employee reported this flight as arrived or departed. Confirm cancellation before saving."
        }
        localWrites.withLock {
            check(accepting && observed == generation && owner == currentOwner()) { "Your session changed. Reopen the flight." }
            val existing = dao.rows(owner).firstOrNull { it.flightId == flight.id }
            check(existing == null || existing.operation != "revert" || existing.status != "pending") {
                "Wait for your cancellation to be reverted before submitting again."
            }
            val row = if (existing?.status == "pending") existing.copy(inputJson = json.encodeToString(normalized), savedAtUtc = now().toString(), error = null)
                else FlightCancellationPendingEntity(owner, flight.id, flight.flightNumber, checkNotNull(flight.currentStaffMemberId),
                    UUID.randomUUID().toString(), "save", json.encodeToString(normalized), flight.ownCancellation()?.id,
                    flight.rowVersion, now().toString())
            dao.put(row)
        }
        runCatching { schedule() }
    }

    /** Unsent local cancellation is discarded; an unknown save is acknowledged before its exact ID is reverted. */
    suspend fun revert(flight: FlightDetail) = operationWrite { revertWithinGate(flight) }
    private suspend fun revertWithinGate(flight: FlightDetail): Unit = submissionGate.withLock {
        val owner = checkNotNull(currentOwner()) { "Sign in to revert your cancellation." }
        val observed = generation
        check(flight.canSaveCancellation) { "You cannot revert this cancellation." }
        check(flight.isInFlightWindow(now())) { "This flight is outside your current flight window." }
        localWrites.withLock {
            check(accepting && observed == generation && owner == currentOwner()) { "Your session changed. Reopen the flight." }
            val existing = dao.rows(owner).firstOrNull { it.flightId == flight.id }
            if (existing?.operation == "revert" && existing.status == "pending") return@withLock
            val own = flight.ownCancellation()
            if (own == null && (existing == null || existing.attemptOperation == null || existing.status != "pending")) {
                existing?.let { dao.delete(owner, it.flightId, it.changeId) }
                return@withLock
            }
            check(validVersion(flight.rowVersion)) { "Reload the flight before reverting your cancellation." }
            val row = if (existing?.status == "pending" && existing.attemptOperation != null) existing.copy(
                operation = "revert", inputJson = null, submissionId = own?.id ?: existing.submissionId, savedAtUtc = now().toString(), error = null)
                else FlightCancellationPendingEntity(owner, flight.id, flight.flightNumber, checkNotNull(flight.currentStaffMemberId),
                    UUID.randomUUID().toString(), "revert", null, checkNotNull(own).id, flight.rowVersion, now().toString())
            dao.put(row)
        }
        runCatching { schedule() }
    }

    suspend fun hasSavedInput(flightId: String): Boolean = localWrites.withLock {
        val owner = currentOwner() ?: return@withLock false
        dao.rows(owner).any { it.flightId == flightId }
    }

    /** Replays frozen attempts exactly. Conflicts stay on this device for explicit review. */
    suspend fun syncPending(): Boolean = gate.withLock {
        if (!accepting) return@withLock false
        val owner = currentOwner() ?: return@withLock false
        if (!online()) return@withLock dao.rows(owner).any { it.status == "pending" }
        var retry = false
        for (snapshot in localWrites.withLock { dao.rows(owner).filter { it.status == "pending" } }) {
            // A save followed by Revert can finish in this drain without waiting for a later poll.
            for (step in 0..1) {
                if (!accepting || owner != currentOwner()) return@withLock false
                val row = localWrites.withLock {
                    val latest = pendingRow(snapshot) ?: return@withLock null
                    if (latest.attemptOperation != null) latest else latest.copy(attemptOperation = latest.operation,
                        attemptInputJson = latest.inputJson, attemptSubmissionId = latest.submissionId,
                        attemptRowVersion = latest.rowVersion).also { dao.put(it) }
                } ?: break
                try {
                    val version = checkNotNull(row.attemptRowVersion)
                    check(validVersion(version)) { "Reload the flight and review the saved cancellation." }
                    when (row.attemptOperation) {
                        "save" -> {
                            val input = checkNotNull(runCatching {
                                json.decodeFromString<SaveFlightCancellationRequest>(checkNotNull(row.attemptInputJson))
                            }.getOrNull()) { "The saved cancellation is incomplete. Reload the flight and review it." }
                            validate(input, version)
                            val result = send(row.flightId, input, version)
                            if (owner != currentOwner()) return@withLock false
                            accepted(row.flightId, result)
                            localWrites.withLock {
                                pendingRow(row)?.let { latest ->
                                    if (latest.operation == "save" && sameInput(latest.inputJson, row.attemptInputJson))
                                        dao.delete(owner, row.flightId, row.changeId)
                                    else dao.put(latest.copy(submissionId = result.submission.id, rowVersion = result.rowVersion,
                                        attemptOperation = null, attemptInputJson = null, attemptSubmissionId = null,
                                        attemptRowVersion = null, error = null))
                                }
                            }
                        }
                        "revert" -> {
                            val result = revert(row.flightId, checkNotNull(row.attemptSubmissionId), version)
                            if (owner != currentOwner()) return@withLock false
                            onReverted(row.flightId, result)
                            localWrites.withLock {
                                pendingRow(row)?.let { latest ->
                                    if (latest.operation == "revert") dao.delete(owner, row.flightId, row.changeId)
                                    else dao.put(latest.copy(submissionId = null, rowVersion = result.rowVersion,
                                        attemptOperation = null, attemptInputJson = null, attemptSubmissionId = null,
                                        attemptRowVersion = null, error = null))
                                }
                            }
                        }
                        else -> error("The saved cancellation action is invalid. Reload the flight and review it.")
                    }
                } catch (cancelled: CancellationException) { throw cancelled }
                catch (error: Exception) {
                    if (owner != currentOwner()) return@withLock false
                    val transient = error is IOException || error is HttpRequestTimeoutException || error is SerializationException ||
                        error is ApiException && (error.statusCode >= 500 || error.statusCode in listOf(408, 429))
                    val status = if (transient || error is ApiException && error.statusCode == 401) "pending"
                        else if (error is ApiException && error.statusCode in listOf(409, 412)) "conflict" else "failed"
                    val message = when (status) {
                        "pending" -> "Saved on this device. Waiting to sync; reconnect or sign in again."
                        "conflict" -> "The flight changed. Reload it and review your cancellation before saving again."
                        else -> if (error is ApiException) error.userMessage() else "Cancellation was not accepted. Reload the flight and review it."
                    }
                    localWrites.withLock { pendingRow(row)?.let { dao.put(it.copy(status = status, error = message)) } }
                    retry = retry || transient
                    if (error is ApiException && error.statusCode == 401) return@withLock false
                    break
                }
            }
        }
        retry || localWrites.withLock { dao.rows(owner).any { it.status == "pending" } }
    }

    suspend fun retry(flightId: String) = operationWrite { retryWithinGate(flightId) }
    private suspend fun retryWithinGate(flightId: String) = localWrites.withLock {
        val owner = currentOwner() ?: return@withLock
        check(accepting) { "Your session changed. Reopen Sync Center." }
        dao.rows(owner).firstOrNull { it.flightId == flightId }?.let { dao.put(it.copy(status = "pending", error = null)) }
        runCatching { schedule() }
    }

    /** A transmitted cancellation cannot be discarded as though it had never reached the server. */
    suspend fun discard(flightId: String) = operationWrite { discardWithinGate(flightId) }
    private suspend fun discardWithinGate(flightId: String) = localWrites.withLock {
        val owner = currentOwner() ?: return@withLock
        check(accepting) { "Your session changed. Reopen Sync Center." }
        dao.rows(owner).firstOrNull { it.flightId == flightId }?.let {
            check(it.attemptOperation == null || it.status != "pending") {
                "This cancellation may already be submitted. Reopen the flight and choose Revert cancellation."
            }
            dao.delete(owner, it.flightId, it.changeId)
        }
    }

    private fun sameInput(first: String?, second: String?): Boolean = runCatching {
        json.decodeFromString<SaveFlightCancellationRequest>(checkNotNull(first)).withoutAcknowledgement().normalized() ==
            json.decodeFromString<SaveFlightCancellationRequest>(checkNotNull(second)).withoutAcknowledgement().normalized()
    }.getOrDefault(false)

    private suspend fun pendingRow(row: FlightCancellationPendingEntity) = dao.rows(row.ownerSubject)
        .firstOrNull { it.flightId == row.flightId && it.changeId == row.changeId && it.status == "pending" }

    suspend fun pause() {
        localWrites.withLock { accepting = false; generation++ }
        gate.withLock { }
    }
    suspend fun resume() = localWrites.withLock { accepting = true }
    suspend fun clear() = gate.withLock { localWrites.withLock { generation++; dao.clear() } }

    private fun validVersion(version: String): Boolean = runCatching { Base64.getDecoder().decode(version).size == 8 }.getOrDefault(false)
    private fun validate(input: SaveFlightCancellationRequest, version: String) {
        check(validVersion(version)) { "Reload the flight before saving cancellation." }
        val canceled = runCatching { OffsetDateTime.parse(input.canceledAtUtc) }.getOrNull()
        check(canceled != null && canceled.year in 1..9999 && canceled.toInstant() != Instant.parse("0001-01-01T00:00:00Z")) {
            "Cancellation time is required."
        }
    }
}
