package com.nags.operations.data.repo

import com.nags.operations.data.FlightDepartureSaved
import com.nags.operations.data.FlightDepartureSubmission
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.SaveFlightDepartureRequest
import com.nags.operations.data.db.entities.FlightDeparturePendingEntity
import com.nags.operations.data.visibleDeparture
import java.io.File
import java.io.FileOutputStream
import java.nio.file.Files
import java.nio.file.StandardCopyOption
import java.security.MessageDigest
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json

/** Private account/API-scoped previews; queued signature ownership remains in the departure queue. */
class DepartureSignaturePreviews(
    private val directory: File,
    private val currentOwner: suspend () -> String?,
    private val online: () -> Boolean,
    private val fetch: suspend (String, String, String) -> ByteArray?,
    private val pendingBytes: suspend (String, String) -> ByteArray?,
) {
    private val gate = Mutex()
    @Volatile private var generation = 0L

    suspend fun load(flight: FlightDetail, pending: FlightDeparturePendingEntity?): ByteArray? {
        val owner = currentOwner() ?: return null
        val observed = generation
        if (pending?.signatureFile != null && pending.ownerSubject == owner && pending.flightId == flight.id &&
            pending.staffMemberId.equals(flight.currentStaffMemberId, true)) {
            val bytes = pendingBytes(flight.id, pending.changeId)
            return bytes?.takeIf { owner == currentOwner() && generation == observed }
        }
        val retained = pending?.takeIf { it.ownerSubject == owner && it.flightId == flight.id &&
            it.staffMemberId.equals(flight.currentStaffMemberId, true) }
        val targetSubmissionId = retained?.let { runCatching {
            Json.decodeFromString<SaveFlightDepartureRequest>(it.inputJson).targetSubmissionId
        }.getOrNull() }
        val submission = if (targetSubmissionId == null) flight.visibleDeparture()
            else flight.departureSubmissions.firstOrNull { it.id.equals(targetSubmissionId, true) }
                ?: retained.let { row -> runCatching {
                    Json.decodeFromString<List<FlightDepartureSubmission>>(row.reviewedDepartureJson)
                        .firstOrNull { it.id.equals(targetSubmissionId, true) }
                }.getOrNull() }
        if (submission == null || !submission.hasSignature) return null
        return loadSaved(flight.id, submission, owner, observed)
    }

    /** Unlike a visible preview, complete preparation includes every signed departure submission. */
    suspend fun prefetch(flight: FlightDetail) {
        val owner = checkNotNull(currentOwner()) { "Sign in to prepare signatures offline." }
        val observed = generation
        for (submission in flight.departureSubmissions.filter { it.hasSignature }) {
            checkNotNull(loadSaved(flight.id, submission, owner, observed, requireCached = true)) {
                "The customer signature could not be prepared offline."
            }
        }
        check(owner == currentOwner() && observed == generation) { "Flight access changed during offline preparation." }
    }

    private suspend fun loadSaved(flightId: String, submission: FlightDepartureSubmission, owner: String,
        observed: Long, requireCached: Boolean = false): ByteArray? {
        val key = key(owner, flightId, submission)
        val cached = gate.withLock {
            if (owner != currentOwner() || observed != generation) return@withLock null
            withContext(Dispatchers.IO) {
                val file = File(directory, key)
                try { file.takeIf { it.isFile && it.length() <= MAX_BYTES }?.readBytes()?.also(::validate) }
                catch (_: Exception) { file.delete(); null }
            }
        }
        if (cached != null) return cached.takeIf { owner == currentOwner() && generation == observed }
        if (!online() || owner != currentOwner() || observed != generation) return null
        val bytes = fetch(flightId, submission.id, submission.updatedAtUtc ?: submission.createdAtUtc) ?: return null
        validate(bytes)
        return gate.withLock {
            if (owner != currentOwner() || observed != generation) return@withLock null
            try { write(key, bytes) }
            catch (cancelled: CancellationException) { throw cancelled }
            catch (error: Exception) {
                if (requireCached) throw error
                // A visible online preview remains usable when its replaceable cache cannot be written.
            }
            bytes.takeIf { owner == currentOwner() && observed == generation }
        }
    }

    /** Preserve the exact acknowledged PNG before its queue-owned file is released. */
    suspend fun recordAcknowledged(flightId: String, saved: FlightDepartureSaved, bytes: ByteArray) {
        if (!saved.submission.hasSignature) return
        val owner = currentOwner() ?: return
        val observed = generation
        validate(bytes)
        gate.withLock {
            if (owner == currentOwner() && observed == generation) write(key(owner, flightId, saved.submission), bytes)
        }
    }

    suspend fun clear() = gate.withLock {
        generation++
        withContext(Dispatchers.IO) { directory.deleteRecursively(); Unit }
    }

    private suspend fun write(key: String, bytes: ByteArray) = withContext(Dispatchers.IO) {
        check(directory.isDirectory || directory.mkdirs()) { "Could not cache the customer signature preview." }
        val temporary = File(directory, "$key.part")
        try {
            FileOutputStream(temporary).use { output -> output.write(bytes); output.fd.sync() }
            Files.move(temporary.toPath(), File(directory, key).toPath(), StandardCopyOption.ATOMIC_MOVE, StandardCopyOption.REPLACE_EXISTING)
        } finally { temporary.delete() }
    }

    private fun key(owner: String, flightId: String, submission: FlightDepartureSubmission): String {
        val version = submission.signatureVersion ?: submission.updatedAtUtc ?: submission.createdAtUtc
        val bytes = "$owner\u0000$flightId\u0000${submission.id}\u0000$version".toByteArray(Charsets.UTF_8)
        return MessageDigest.getInstance("SHA-256").digest(bytes).joinToString("") { "%02x".format(it) } + ".png"
    }

    private fun validate(bytes: ByteArray) {
        require(bytes.size in 45..MAX_BYTES && bytes.copyOfRange(0, 8).contentEquals(byteArrayOf(137.toByte(), 80, 78, 71, 13, 10, 26, 10))) {
            "Customer signature preview is unavailable."
        }
    }

    private companion object { const val MAX_BYTES = 2 * 1024 * 1024 }
}
