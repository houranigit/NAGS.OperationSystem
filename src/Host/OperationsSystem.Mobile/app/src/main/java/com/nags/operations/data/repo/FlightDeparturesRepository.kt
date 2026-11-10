package com.nags.operations.data.repo

import com.nags.operations.data.ApiException
import com.nags.operations.data.FlightDepartureSaved
import com.nags.operations.data.FlightDepartureSubmission
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.SaveFlightDepartureRequest
import com.nags.operations.data.departureEditable
import com.nags.operations.data.db.dao.FlightDepartureDao
import com.nags.operations.data.db.entities.FlightDeparturePendingEntity
import com.nags.operations.data.hasOwnCancellation
import com.nags.operations.data.isInFlightWindow
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

/** Local-first departures retain signature files and unknown HTTP attempts until acknowledgment. */
@OptIn(ExperimentalCoroutinesApi::class)
class FlightDeparturesRepository(
    private val dao: FlightDepartureDao,
    private val signatureStore: DepartureSignatureStore,
    ownerFlow: Flow<String?>,
    private val currentOwner: suspend () -> String?,
    private val online: () -> Boolean,
    private val send: suspend (String, SaveFlightDepartureRequest, String) -> FlightDepartureSaved,
    private val accepted: suspend (String, FlightDepartureSaved) -> Unit,
    private val schedule: () -> Unit,
    private val json: Json,
    private val hasSavedArrival: suspend (String) -> Boolean,
    private val acknowledgedArrivalVersion: suspend (String, String) -> String,
    private val fetchFlight: suspend (String) -> FlightDetail,
    private val now: () -> Instant = Instant::now,
    private val onSignatureAcknowledged: suspend (String, FlightDepartureSaved, ByteArray) -> Unit = { _, _, _ -> },
    private val hasSavedCancellation: suspend (String) -> Boolean = { false },
    private val submissionGate: Mutex = Mutex(),
) {
    private val gate = Mutex()
    private val localWrites = Mutex()
    @Volatile private var accepting = true
    @Volatile private var generation = 0L
    private var acknowledgedOwner: String? = null
    private val ownAcknowledgments = linkedMapOf<String, LinkedHashMap<String, FlightDepartureSaved>>()
    val changes: Flow<List<FlightDeparturePendingEntity>> = ownerFlow.flatMapLatest { owner ->
        if (owner == null) flowOf(emptyList()) else dao.observe(owner)
    }

    suspend fun save(flight: FlightDetail, input: SaveFlightDepartureRequest): Unit = submissionGate.withLock {
        check(!flight.hasCancellationSubmission || input.acknowledgeConflictingSubmission) {
            "Another employee reported this flight as canceled. Confirm departure before saving."
        }
        check(!hasSavedCancellation(flight.id)) { "Revert your cancellation and wait for it to sync before saving work on this flight." }
        val owner = checkNotNull(currentOwner()) { "Sign in to save departure details." }
        val observed = generation
        check(flight.departureEditable()) { "Only the employee who submitted the departure can update it." }
        check(flight.isInFlightWindow(now())) { "This flight is outside your current flight window." }
        val ownAcknowledged = localWrites.withLock {
            resetAcknowledgments(owner)
            followOwnAcknowledgments(flight.id, flight.rowVersion, flight.departureSubmissions)
        }
        // The arrival lock must be acquired before, never while holding, departure.localWrites.
        val acknowledged = acknowledgedArrivalVersion(flight.id, ownAcknowledged.first)
        val persistedVersion = localWrites.withLock {
            check(accepting && observed == generation && owner == currentOwner()) { "Your session changed. Reopen the flight." }
            // An upload may have acknowledged/deleted the row during the arrival-lineage lookup.
            // Follow that exact own ACK again under the same lock that publishes the correction.
            val latestOwnAcknowledged = followOwnAcknowledgments(flight.id, acknowledged, ownAcknowledged.second)
            val existing = dao.rows(owner).firstOrNull { it.flightId == flight.id }
            val preserveAttempt = existing?.status == "pending"
            val normalized = input.normalized()
            val existingInput = existing?.let { runCatching { json.decodeFromString<SaveFlightDepartureRequest>(it.inputJson) }.getOrNull() }
            val reusedFile = existing?.signatureFile?.takeIf { normalized.signatureContent == null }
            val desired = normalized.copy(signatureContent = null,
                signatureSignedAtUtc = if (reusedFile != null) existingInput?.signatureSignedAtUtc
                    else if (normalized.signatureContent == null && latestOwnAcknowledged.first != flight.rowVersion) null
                    else normalized.signatureSignedAtUtc)
            val reviewed = if (preserveAttempt) checkNotNull(existing).reviewedDepartureJson else json.encodeToString(latestOwnAcknowledged.second)
            val version = if (preserveAttempt) checkNotNull(existing).rowVersion else latestOwnAcknowledged.first
            val bytes = normalized.signatureContent?.let(::decodeSignature)
            validate(desired, version, checkNotNull(flight.currentStaffMemberId), reviewed,
                hasSignature = bytes != null || reusedFile != null)
            var createdFile: String? = null
            try {
                createdFile = bytes?.let { signatureStore.save(it) }
                val file = createdFile ?: reusedFile
                val row = if (preserveAttempt) checkNotNull(existing).copy(
                    inputJson = json.encodeToString(desired), signatureFile = file, savedAtUtc = now().toString(), error = null)
                else FlightDeparturePendingEntity(owner, flight.id, flight.flightNumber, checkNotNull(flight.currentStaffMemberId),
                    UUID.randomUUID().toString(), json.encodeToString(desired), version, reviewed, now().toString(), file,
                    needsDepartureComparison = version != flight.rowVersion)
                dao.put(row)
                row.rowVersion
            } finally { reclaimSignatures() }
        }
        // Close the interval between reading arrival's ACK lineage and publishing this row.
        // Arrival records lineage before its persistent callback, so one side always sees the other.
        val latestAcknowledged = acknowledgedArrivalVersion(flight.id, persistedVersion)
        if (latestAcknowledged != persistedVersion) onArrivalAcknowledged(flight.id, persistedVersion, latestAcknowledged)
        runCatching { schedule() }
    }

    /** Called after an arrival HTTP acknowledgment and before its durable intent is removed. */
    suspend fun onArrivalAcknowledged(flightId: String, requestVersion: String, responseVersion: String) = localWrites.withLock {
        val owner = currentOwner() ?: return@withLock
        val row = dao.rows(owner).firstOrNull { it.flightId == flightId && it.status == "pending" } ?: return@withLock
        if (row.rowVersion == requestVersion && responseVersion != requestVersion) {
            // An already transmitted departure retains its original version and file.
            dao.put(row.copy(rowVersion = responseVersion, needsDepartureComparison = true))
        }
    }

    /** Arrival synchronization runs first; unresolved arrival intent blocks only that flight. */
    suspend fun syncPending(): Boolean = gate.withLock {
        if (!accepting) return@withLock false
        val owner = currentOwner() ?: return@withLock false
        if (!online()) return@withLock dao.rows(owner).any { it.status == "pending" }
        localWrites.withLock { reclaimSignatures() }
        var retry = false
        val blockedByArrival = mutableSetOf<String>()
        for (snapshot in localWrites.withLock { dao.rows(owner).filter { it.status == "pending" } }) {
            if (!accepting || owner != currentOwner()) return@withLock false
            if (hasSavedArrival(snapshot.flightId)) { blockedByArrival += snapshot.flightId; continue }
            try {
                val prepared = prepare(snapshot) ?: continue
                val input = runCatching { json.decodeFromString<SaveFlightDepartureRequest>(checkNotNull(prepared.attemptInputJson)) }.getOrNull()
                if (input == null) {
                    fail(prepared, "The saved departure is incomplete. Reload the flight and review it.")
                    continue
                }
                val hydrated = try {
                    val signature = prepared.attemptSignatureFile?.let { Base64.getEncoder().encodeToString(signatureStore.read(it).also(::checkSignatureBytes)) }
                    validate(input, checkNotNull(prepared.attemptRowVersion), prepared.staffMemberId,
                        prepared.reviewedDepartureJson, signature != null)
                    input.copy(signatureContent = signature)
                } catch (cancelled: CancellationException) { throw cancelled }
                catch (_: Exception) {
                    fail(prepared, "The saved customer signature or departure is unavailable. Reload the flight and review it.")
                    continue
                }
                val result = send(prepared.flightId, hydrated, checkNotNull(prepared.attemptRowVersion))
                if (owner != currentOwner()) return@withLock false
                hydrated.signatureContent?.let { content ->
                    try { onSignatureAcknowledged(prepared.flightId, result, Base64.getDecoder().decode(content)) }
                    catch (cancelled: CancellationException) { throw cancelled }
                    catch (_: Exception) { /* Preview caching must not invalidate the acknowledged departure. */ }
                }
                accepted(prepared.flightId, result)
                localWrites.withLock {
                    resetAcknowledgments(owner)
                    val chain = ownAcknowledgments.remove(prepared.flightId) ?: linkedMapOf()
                    chain[checkNotNull(prepared.attemptRowVersion)] = result
                    while (chain.size > 64) chain.remove(chain.keys.first())
                    ownAcknowledgments[prepared.flightId] = chain
                    while (ownAcknowledgments.size > 64) ownAcknowledgments.remove(ownAcknowledgments.keys.first())
                    pendingRow(prepared)?.let { latest ->
                        if (sameInput(latest.inputJson, prepared.attemptInputJson) && latest.signatureFile == prepared.attemptSignatureFile) {
                            dao.delete(owner, latest.flightId, latest.changeId)
                        } else {
                            val desired = json.decodeFromString<SaveFlightDepartureRequest>(latest.inputJson)
                            val sameSignature = latest.signatureFile == prepared.attemptSignatureFile
                            val updatedInput = if (sameSignature) desired.copy(signatureSignedAtUtc = null) else desired
                            val reviewed = json.decodeFromString<List<FlightDepartureSubmission>>(latest.reviewedDepartureJson)
                                .filterNot { it.id == result.submission.id } + result.submission
                            dao.put(latest.copy(inputJson = json.encodeToString(updatedInput), rowVersion = result.rowVersion,
                                reviewedDepartureJson = json.encodeToString(reviewed), signatureFile = if (sameSignature) null else latest.signatureFile,
                                needsDepartureComparison = false, attemptInputJson = null, attemptRowVersion = null,
                                attemptSignatureFile = null, error = null))
                        }
                    }
                    reclaimSignatures()
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
                    "conflict" -> "The flight changed. Reload it and review your saved departure before saving again."
                    else -> if (error is ApiException) error.userMessage() else "Departure was not accepted. Reload the flight and review it."
                }
                localWrites.withLock { pendingRow(snapshot)?.let { dao.put(it.copy(status = status, error = message)) } }
                retry = retry || transient
                if (error is ApiException && error.statusCode == 401) return@withLock false
            }
        }
        retry || localWrites.withLock { dao.rows(owner).any { it.status == "pending" && it.flightId !in blockedByArrival } }
    }

    private suspend fun prepare(snapshot: FlightDeparturePendingEntity): FlightDeparturePendingEntity? {
        val original = localWrites.withLock { pendingRow(snapshot) } ?: return null
        // Retry a frozen attempt exactly; the server may already contain that departure.
        if (original.attemptInputJson != null) return original
        if (original.needsDepartureComparison) {
            val current = fetchFlight(original.flightId)
            if (current.fromCache) throw IOException("Connect to reload the latest flight before synchronizing departure.")
            val reviewed = json.decodeFromString<List<FlightDepartureSubmission>>(original.reviewedDepartureJson)
            // Older cached/queued snapshots predate opaque signature versions. Ignore only that
            // absent metadata, while preserving every existing departure concurrency check.
            val comparable = current.departureSubmissions.map { live ->
                if (reviewed.firstOrNull { it.id == live.id }?.signatureVersion == null) live.copy(signatureVersion = null) else live
            }
            if (current.hasOwnCancellation() || (current.hasCancellationSubmission &&
                    !json.decodeFromString<SaveFlightDepartureRequest>(original.inputJson).acknowledgeConflictingSubmission) || comparable.sortedBy { it.id } != reviewed.sortedBy { it.id })
                throw ApiException(409, "Departure details changed. Reload the flight and review them before saving again.")
        }
        return localWrites.withLock {
            val latest = pendingRow(original) ?: return@withLock null
            if (latest.rowVersion != original.rowVersion || latest.reviewedDepartureJson != original.reviewedDepartureJson) return@withLock null
            latest.copy(attemptInputJson = latest.inputJson, attemptRowVersion = latest.rowVersion,
                attemptSignatureFile = latest.signatureFile).also { dao.put(it) }
        }
    }

    /** Preview only the current account's retained desired or frozen attempted signature. */
    suspend fun signaturePreview(flightId: String, changeId: String? = null, attempted: Boolean = false): ByteArray? = localWrites.withLock {
        val owner = currentOwner() ?: return@withLock null
        val row = dao.rows(owner).firstOrNull { it.flightId == flightId && (changeId == null || it.changeId == changeId) }
            ?: return@withLock null
        val key = if (attempted) row.attemptSignatureFile else row.signatureFile
        key?.let { signatureStore.read(it).also(::checkSignatureBytes) }
    }

    suspend fun hasSavedInput(flightId: String): Boolean = localWrites.withLock {
        val owner = currentOwner() ?: return@withLock false
        dao.rows(owner).any { it.flightId == flightId }
    }

    suspend fun retry(flightId: String) = localWrites.withLock {
        val owner = currentOwner() ?: return@withLock
        check(accepting) { "Your session changed. Reopen Sync Center." }
        dao.rows(owner).firstOrNull { it.flightId == flightId }?.let { dao.put(it.copy(status = "pending", error = null)) }
        runCatching { schedule() }
    }

    suspend fun discard(flightId: String) = localWrites.withLock {
        val owner = currentOwner() ?: return@withLock
        check(accepting) { "Your session changed. Reopen Sync Center." }
        dao.rows(owner).firstOrNull { it.flightId == flightId }?.let { dao.delete(owner, it.flightId, it.changeId) }
        reclaimSignatures()
    }

    private fun sameInput(first: String, second: String?): Boolean = runCatching {
        json.decodeFromString<SaveFlightDepartureRequest>(first).withoutAcknowledgement().normalized() ==
            json.decodeFromString<SaveFlightDepartureRequest>(checkNotNull(second)).withoutAcknowledgement().normalized()
    }.getOrDefault(false)

    private suspend fun pendingRow(row: FlightDeparturePendingEntity) = dao.rows(row.ownerSubject)
        .firstOrNull { it.flightId == row.flightId && it.changeId == row.changeId && it.status == "pending" }

    private suspend fun fail(row: FlightDeparturePendingEntity, message: String) = localWrites.withLock {
        pendingRow(row)?.let { dao.put(it.copy(status = "failed", error = message)) }
    }

    private suspend fun reclaimSignatures() {
        try { signatureStore.retainOnly(dao.signatureFiles().toSet()) }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (_: Exception) { /* A later resume/sync reclaims orphans; cleanup cannot undo a committed disk save. */ }
    }

    /** Caller holds localWrites; both the version and signature-reuse proof come from our own ACK. */
    private fun followOwnAcknowledgments(flightId: String, originalVersion: String,
        originalReviewed: List<FlightDepartureSubmission>): Pair<String, List<FlightDepartureSubmission>> {
        var version = originalVersion
        var reviewed = originalReviewed
        val visited = mutableSetOf<String>()
        val chain = ownAcknowledgments[flightId]
        while (visited.add(version)) {
            val result = chain?.get(version) ?: break
            reviewed = reviewed.filterNot { it.id == result.submission.id } + result.submission
            if (result.rowVersion == version) break
            version = result.rowVersion
        }
        return version to reviewed
    }

    private fun resetAcknowledgments(owner: String?) {
        if (acknowledgedOwner != owner) { ownAcknowledgments.clear(); acknowledgedOwner = owner }
    }

    suspend fun pause() {
        localWrites.withLock { accepting = false; generation++ }
        gate.withLock { localWrites.withLock { ownAcknowledgments.clear(); acknowledgedOwner = null } }
    }
    suspend fun resume() = localWrites.withLock { accepting = true; reclaimSignatures() }
    suspend fun clear() = gate.withLock { localWrites.withLock {
        generation++; ownAcknowledgments.clear(); acknowledgedOwner = null; dao.clear(); reclaimSignatures()
    } }

    private fun decodeSignature(content: String): ByteArray {
        check(content.length <= 2_796_204) { "The customer signature must be at most 2 MB." }
        return Base64.getDecoder().decode(content).also(::checkSignatureBytes)
    }

    private fun checkSignatureBytes(content: ByteArray) {
        check(content.size in 45..2_097_152 && content.copyOfRange(0, 8).contentEquals(byteArrayOf(137.toByte(), 80, 78, 71, 13, 10, 26, 10))) {
            "The customer signature must be a PNG image of at most 2 MB."
        }
    }

    private fun validate(input: SaveFlightDepartureRequest, version: String, staffId: String, reviewedJson: String, hasSignature: Boolean) {
        check(runCatching { Base64.getDecoder().decode(version).size == 8 }.getOrDefault(false)) { "Reload the flight before saving departure details." }
        val atd = runCatching { OffsetDateTime.parse(input.atd) }.getOrNull()
        check(atd != null && atd.year in 1..9999 && atd.toInstant() != Instant.parse("0001-01-01T00:00:00Z")) { "Actual departure time is required." }
        val own = json.decodeFromString<List<FlightDepartureSubmission>>(reviewedJson).any { it.submittedByEmployee.staffMemberId.equals(staffId, true) }
        if (hasSignature) {
            val signed = input.signatureSignedAtUtc?.let { runCatching { OffsetDateTime.parse(it) }.getOrNull() }
            check(signed != null && signed.year in 1..9999 && signed.toInstant() != Instant.parse("0001-01-01T00:00:00Z")) { "Customer signing time is required." }
        } else check(own && input.signatureSignedAtUtc == null) { "Customer signature is required." }
    }
}
