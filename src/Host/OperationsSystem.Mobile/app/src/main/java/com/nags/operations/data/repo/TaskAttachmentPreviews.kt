package com.nags.operations.data.repo

import com.nags.operations.data.FlightTaskSaved
import com.nags.operations.data.FlightServiceAttachmentRecord
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

/** Private account/API-scoped media previews, separate from queue-owned upload files. */
class TaskAttachmentPreviews(
    private val directory: File,
    private val currentOwner: suspend () -> String?,
    private val online: () -> Boolean,
    private val fetch: suspend (String, String, String, String) -> ByteArray?,
) {
    private val gate = Mutex()
    @Volatile private var generation = 0L

    suspend fun load(flightId: String, taskId: String, attachmentId: String, uploadedAtUtc: String, expectedSizeBytes: Long? = null): File? {
        val owner = currentOwner() ?: return null
        val observed = generation
        val key = key(owner, flightId, taskId, attachmentId, uploadedAtUtc)
        val cached = gate.withLock {
            if (owner != currentOwner() || observed != generation) return@withLock null
            withContext(Dispatchers.IO) {
                File(directory, key).takeIf { it.isFile && it.length() in 1..MAX_BYTES.toLong() &&
                    (expectedSizeBytes == null || it.length() == expectedSizeBytes) }
            }
        }
        if (cached != null) return cached.takeIf { owner == currentOwner() && observed == generation }
        if (!online() || owner != currentOwner() || observed != generation) return null
        val bytes = fetch(flightId, taskId, attachmentId, uploadedAtUtc) ?: return null
        require(bytes.size in 1..MAX_BYTES && (expectedSizeBytes == null || bytes.size.toLong() == expectedSizeBytes)) {
            "The task attachment is unavailable."
        }
        return gate.withLock {
            if (owner != currentOwner() || observed != generation) return@withLock null
            write(key, bytes).takeIf { owner == currentOwner() && observed == generation }
        }
    }

    /** Full synchronization requires durable bytes for every current attachment, regardless of media kind. */
    suspend fun prefetch(flightId: String, taskId: String, attachments: List<FlightServiceAttachmentRecord>) {
        val owner = checkNotNull(currentOwner()) { "Sign in to prepare attachments offline." }
        val observed = generation
        for (attachment in attachments) {
            check(owner == currentOwner() && observed == generation) { "Flight access changed during offline preparation." }
            checkNotNull(load(flightId, taskId, attachment.id, attachment.uploadedAtUtc, attachment.sizeBytes)) {
                "The task attachment could not be prepared offline."
            }
        }
        check(owner == currentOwner() && observed == generation) { "Flight access changed during offline preparation." }
    }

    /** Preserve exact acknowledged bytes before the upload queue releases its private copies. */
    suspend fun recordAcknowledged(flightId: String, saved: FlightTaskSaved, files: Map<String, ByteArray>) {
        val owner = currentOwner() ?: return
        val observed = generation
        gate.withLock {
            if (owner != currentOwner() || observed != generation) return@withLock
            for (attachment in saved.task.attachments) {
                val bytes = files[attachment.id] ?: continue
                require(bytes.size.toLong() == attachment.sizeBytes && bytes.size in 1..MAX_BYTES) {
                    "The saved task attachment is unavailable."
                }
                write(key(owner, flightId, saved.task.id, attachment.id, attachment.uploadedAtUtc), bytes)
            }
        }
    }

    suspend fun clear() = gate.withLock {
        generation++
        withContext(Dispatchers.IO) { directory.deleteRecursively(); Unit }
    }

    private suspend fun write(key: String, bytes: ByteArray): File = withContext(Dispatchers.IO) {
        check(directory.isDirectory || directory.mkdirs()) { "Could not cache the task attachment." }
        val target = File(directory, key)
        val temporary = File(directory, "$key.part")
        try {
            FileOutputStream(temporary).use { output -> output.write(bytes); output.fd.sync() }
            Files.move(temporary.toPath(), target.toPath(), StandardCopyOption.ATOMIC_MOVE, StandardCopyOption.REPLACE_EXISTING)
            target
        } finally { temporary.delete() }
    }

    private fun key(owner: String, flightId: String, taskId: String, attachmentId: String, uploadedAtUtc: String): String {
        val bytes = "$owner\u0000$flightId\u0000$taskId\u0000$attachmentId\u0000$uploadedAtUtc".toByteArray(Charsets.UTF_8)
        return MessageDigest.getInstance("SHA-256").digest(bytes).joinToString("") { "%02x".format(it) } + ".bin"
    }

    private companion object { const val MAX_BYTES = 25 * 1024 * 1024 }
}
