package com.nags.operations.data.repo

import com.nags.operations.data.*
import com.nags.operations.data.db.dao.FlightReturnToRampDao
import com.nags.operations.data.db.entities.FlightReturnToRampPendingEntity
import java.io.File
import java.io.IOException
import java.time.Instant
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

/** One occurrence is a durable atomic save; unknown attempts retain their exact version and files. */
@OptIn(ExperimentalCoroutinesApi::class)
class FlightReturnsToRampRepository(
    private val dao: FlightReturnToRampDao,
    private val mediaStore: ServiceAttachmentStore,
    ownerFlow: Flow<String?>,
    private val currentOwner: suspend () -> String?,
    private val online: () -> Boolean,
    private val send: suspend (String, String, SaveFlightReturnToRampRequest, String) -> FlightDetail,
    private val accepted: suspend (FlightDetail, SaveFlightReturnToRampRequest) -> Unit,
    private val schedule: () -> Unit,
    private val json: Json,
    private val fetchFlight: suspend (String) -> FlightDetail,
    private val hasBlockingPriorWork: suspend (String) -> Boolean,
    private val operationGate: FlightOperationsGate = FlightOperationsGate(),
    private val operationSave: suspend (suspend () -> Unit) -> Unit = { it() },
    private val onAcknowledged: suspend (String, String, String) -> Unit = { _, _, _ -> },
    private val onLocalSaved: suspend (FlightDetail) -> Unit = {},
    private val now: () -> Instant = Instant::now,
) {
    private val drain = Mutex()
    @Volatile private var accepting = true
    @Volatile private var generation = 0L
    private var acknowledgedOwner: String? = null
    private val acknowledgments = linkedMapOf<String, LinkedHashMap<String, String>>()
    val changes: Flow<List<FlightReturnToRampPendingEntity>> = ownerFlow.flatMapLatest {
        if (it == null) flowOf(emptyList()) else dao.observe(it)
    }

    suspend fun save(flight: FlightDetail, occurrenceId: String, input: SaveFlightReturnToRampRequest,
        preview: FlightReturnToRampRecord, expectedChangeId: String? = null) = operationGate.withLock { operationSave {
        val owner = checkNotNull(currentOwner()) { "Sign in to save a return to ramp." }
        check(accepting && validArrivalId(occurrenceId) && occurrenceId == preview.id) { "Reopen the return to ramp before saving." }
        check(flight.isInFlightWindow(now()) && flight.canSaveReturnsToRamp && !flight.hasCancellationSubmission) {
            "You are not eligible to update this return to ramp. Reload the flight."
        }
        val baseline = flight.returnToRamps.firstOrNull { it.id == occurrenceId }
        val rows = dao.rows(owner).filter { it.flightId == flight.id }
        val pendingOccurrences = rows.filter { it.status == "pending" && it.occurrenceId != occurrenceId }
            .map { it.preview(json) }.associateBy { it.id }
        val priorOccurrences = flight.returnToRamps.filterNot { it.id in pendingOccurrences } + pendingOccurrences.values
        check(if (baseline == null) flight.canAddReturnToRamp(priorOccurrences) else flight.canEditReturnToRamp(baseline)) {
            if (baseline == null) "Save the flight departure and close any open return to ramp before adding another."
            else "This return to ramp cannot be edited. Reload the flight."
        }
        val normalized = input.normalized()
        check(normalized.validationErrors(flight.rowVersion, flight.allowedServiceIds?.toSet(), baseline = baseline).isEmpty()) {
            "Complete the required return-to-ramp fields and work periods before saving."
        }
        val existing = rows.firstOrNull { it.occurrenceId == occurrenceId }
        if (expectedChangeId != null) {
            check(existing?.changeId == expectedChangeId) { "This saved input changed. Reopen Review before continuing." }
            check(existing?.attemptInputJson == null) { "Retry synchronization before reviewing a request whose result is unknown." }
        }
        val pending = existing?.status == "pending" && expectedChangeId == null
        resetAcknowledgments(owner)
        val version = if (existing?.attemptInputJson != null) existing.rowVersion
            else acknowledgedVersion(flight.id, if (pending) checkNotNull(existing).rowVersion else flight.rowVersion)
        val previousFiles = existing?.filesJson?.let(::files).orEmpty()
        val staged = linkedMapOf<String, String>()
        suspend fun stage(key: String, content: String?, uri: String? = null) {
            check(content == null || content.length <= 34_952_536) { "Attachments must be at most 25 MB." }
            val file = when {
                content != null -> mediaStore.save(Base64.getDecoder().decode(content))
                uri != null -> mediaStore.import(uri)
                else -> previousFiles[key]
            }
            if (file != null) staged[key] = file
        }
        normalized.departure?.takeUnless { it.removeSignature }?.let { departure ->
            departure.signatureContent?.let { validateFlightSignatureBytes(Base64.getDecoder().decode(it)) }
            stage("signature", departure.signatureContent)
        }
        suspend fun attachments(kind: String, id: String, inputs: List<FlightServiceAttachmentInput>?): List<FlightServiceAttachmentInput>? {
            var total = 0L
            return inputs?.map { attachment ->
                val key = "$kind/$id/${attachment.id}"
                val published = if (kind == "service") baseline?.services?.firstOrNull { it.id == id }?.attachments
                    else baseline?.tasks?.firstOrNull { it.id == id }?.attachments
                check(previousFiles[key] != null || published.orEmpty().none { it.id == attachment.id } ||
                    attachment.content == null && attachment.contentUri == null) { "Use a new attachment identity when replacing a published file." }
                stage(key, attachment.content, attachment.contentUri)
                val retained = staged[key]
                if (retained != null) {
                    val bytes = mediaStore.read(retained)
                    validateAttachment(attachment, bytes)
                    previousFiles[key]?.let { check(mediaStore.read(it).contentEquals(bytes)) { "Use a new attachment identity when replacing a file." } }
                    total += bytes.size
                } else check(published.orEmpty().any { it.id == attachment.id }) { "The selected attachment is unavailable. Select it again." }
                check(total <= 25L * 1024 * 1024) { "Attachments on one work record must total at most 25 MB." }
                attachment.copy(content = null, contentUri = null)
            }
        }
        val oldInput = existing?.let { json.decodeFromString<SaveFlightReturnToRampRequest>(it.inputJson) }
        val clean = normalized.copy(departure = normalized.departure?.copy(signatureContent = null,
            signatureSignedAtUtc = if ("signature" in staged) normalized.departure?.signatureSignedAtUtc ?: oldInput?.departure?.signatureSignedAtUtc else null),
            services = normalized.services.map { it.copy(input = it.input.copy(attachments = attachments("service", it.recordId, it.input.attachments))) },
            tasks = normalized.tasks.map { it.copy(input = it.input.copy(attachments = attachments("task", it.recordId, it.input.attachments))) })
        check(staged.values.sumOf { mediaStore.read(it).size.toLong() } <= FlightOperationsBatchLimits.MAX_INLINE_BYTES) {
            "One return-to-ramp Save may contain at most 100 MB of new media."
        }
        check(owner == currentOwner()) { "Your session changed. Reopen the flight." }
        dao.put(FlightReturnToRampPendingEntity(owner, flight.id, occurrenceId, flight.flightNumber,
            UUID.randomUUID().toString(), json.encodeToString(clean), json.encodeToString(preview.copy(
                createdByUserId = preview.createdByUserId.ifBlank { owner.substringAfterLast('|') })),
            version,
            if (pending) existing?.reviewedOccurrenceJson else baseline?.let { json.encodeToString(it) },
            now().toString(), filesJson = json.encodeToString(staged), needsComparison = version != flight.rowVersion || pending && existing?.needsComparison == true,
            attemptInputJson = existing?.attemptInputJson, attemptRowVersion = existing?.attemptRowVersion,
            attemptFilesJson = existing?.attemptFilesJson))
        onLocalSaved(flight)
        // Cleanup is deliberately deferred: the outer combined Room Save may still roll back.
        runCatching { schedule() }
    } }

    suspend fun pendingForFlight(id: String): List<FlightReturnToRampPendingEntity> = operationGate.withLock {
        currentOwner()?.let { owner -> dao.rows(owner).filter { it.flightId == id } }.orEmpty()
    }
    suspend fun hasSavedInput(id: String) = pendingForFlight(id).isNotEmpty()
    suspend fun hasUnacknowledgedAttempt(id: String) = pendingForFlight(id).any { it.attemptInputJson != null }
    suspend fun pendingInput(id: String, occurrenceId: String, expectedChangeId: String? = null): SaveFlightReturnToRampRequest? = operationGate.withLock {
        val row = pendingForFlight(id).firstOrNull { it.occurrenceId == occurrenceId }
        if (expectedChangeId != null) check(row?.changeId == expectedChangeId) { "This saved input changed. Reopen Review before continuing." }
        row?.let { hydrate(it.inputJson, it.filesJson) }
    }
    suspend fun saveReviewed(flight: FlightDetail, occurrenceId: String, expectedChangeId: String,
        input: SaveFlightReturnToRampRequest, preview: FlightReturnToRampRecord) =
        save(flight, occurrenceId, input, preview, expectedChangeId)
    suspend fun discardReviewed(id: String, occurrenceId: String, expectedChangeId: String) = discard(id, occurrenceId, expectedChangeId)
    suspend fun signaturePreview(id: String, occurrenceId: String): ByteArray? = operationGate.withLock {
        pendingForFlight(id).firstOrNull { it.occurrenceId == occurrenceId }?.filesJson?.let(::files)?.get("signature")?.let { mediaStore.read(it) }
    }
    suspend fun attachmentPreview(id: String, occurrenceId: String, attachmentId: String): File? = operationGate.withLock {
        pendingForFlight(id).firstOrNull { it.occurrenceId == occurrenceId }?.filesJson?.let(::files)
            ?.entries?.firstOrNull { it.key.endsWith("/$attachmentId") }?.value?.let { mediaStore.previewFile(it) }
    }
    suspend fun project(flight: FlightDetail): FlightDetail {
        val pending = pendingForFlight(flight.id)
        if (pending.isEmpty()) return flight
        val ids = pending.map { it.occurrenceId }.toSet()
        return flight.copy(returnToRamps = (flight.returnToRamps.filterNot { it.id in ids } + pending.map { it.preview(json) }).sortedBy { it.sequence })
    }

    /** Only a known local ACK may advance the reviewed version; target content is still compared. */
    suspend fun onPriorWriteAcknowledged(id: String, before: String, after: String) = operationGate.withLock {
        val owner = currentOwner() ?: return@withLock
        resetAcknowledgments(owner)
        val chain = acknowledgments.getOrPut(id) { linkedMapOf() }
        chain[before] = after
        while (chain.size > 128) chain.remove(chain.keys.first())
        while (acknowledgments.size > 64) acknowledgments.remove(acknowledgments.keys.first())
        dao.rows(owner).filter { it.flightId == id && it.status == "pending" && it.attemptInputJson == null && it.rowVersion == before }
            .forEach { dao.put(it.copy(rowVersion = after, needsComparison = true)) }
    }

    suspend fun syncPending(): Boolean = drain.withLock {
        if (!accepting || !online()) return@withLock accepting && currentOwner()?.let { dao.rows(it).any { row -> row.status == "pending" } } == true
        val owner = currentOwner() ?: return@withLock false
        val observed = generation
        var retry = false
        // Editing RTR 1 again can make its save timestamp newer than RTR 2. Sequence,
        // rather than edit time, preserves close-before-next-start dependencies.
        for (snapshot in dao.rows(owner).filter { it.status == "pending" }
            .sortedWith(compareBy<FlightReturnToRampPendingEntity> { it.flightId }.thenBy { it.preview(json).sequence })) {
            operationGate.withLock rowLock@{
                if (!accepting || owner != currentOwner() || observed != generation) return@rowLock
                val row = dao.rows(owner).firstOrNull { it.flightId == snapshot.flightId && it.occurrenceId == snapshot.occurrenceId } ?: return@rowLock
                if (row.status != "pending") return@rowLock
                if (row.attemptInputJson == null && hasBlockingPriorWork(row.flightId)) { retry = true; return@rowLock }
                var transmitted = false
                var acknowledged = false
                try {
                    if (row.attemptInputJson == null && row.needsComparison) {
                        val latest = fetchFlight(row.flightId)
                        val baseline = row.reviewedOccurrenceJson?.let { json.decodeFromString<FlightReturnToRampRecord>(it) }
                        check(latest.returnToRamps.firstOrNull { it.id == row.occurrenceId } == baseline) {
                            "This return to ramp changed. Review your saved input before saving again."
                        }
                    }
                    val prepared = if (row.attemptInputJson != null) row else row.copy(attemptInputJson = row.inputJson,
                        attemptRowVersion = row.rowVersion, attemptFilesJson = row.filesJson).also { dao.put(it) }
                    val request = hydrate(checkNotNull(prepared.attemptInputJson), prepared.attemptFilesJson ?: "{}")
                    transmitted = true
                    val detail = send(prepared.flightId, prepared.occurrenceId, request, checkNotNull(prepared.attemptRowVersion))
                    acknowledged = true
                    if (owner != currentOwner() || observed != generation) return@rowLock
                    accepted(detail, request)
                    onAcknowledged(detail.id, checkNotNull(prepared.attemptRowVersion), detail.rowVersion)
                    onPriorWriteAcknowledged(detail.id, checkNotNull(prepared.attemptRowVersion), detail.rowVersion)
                    val current = dao.rows(owner).firstOrNull { it.flightId == row.flightId && it.occurrenceId == row.occurrenceId } ?: return@rowLock
                    if (current.inputJson == prepared.attemptInputJson && current.filesJson == prepared.attemptFilesJson)
                        dao.delete(owner, current.flightId, current.occurrenceId, current.changeId)
                    else dao.put(current.copy(rowVersion = detail.rowVersion, reviewedOccurrenceJson = detail.returnToRamps.firstOrNull { it.id == current.occurrenceId }?.let { json.encodeToString(it) },
                        attemptInputJson = null, attemptRowVersion = null, attemptFilesJson = null, needsComparison = false, error = null))
                } catch (cancelled: CancellationException) { throw cancelled }
                catch (error: Exception) {
                    if (owner != currentOwner() || observed != generation) return@rowLock
                    val rejected = transmitted && !acknowledged && error is ApiException && error.statusCode in 400..499 && error.statusCode !in listOf(401, 408, 429)
                    val releaseAttempt = rejected || !transmitted && row.attemptInputJson == null
                    val transient = acknowledged || error is IOException || error is io.ktor.client.plugins.HttpRequestTimeoutException || error is SerializationException || error is ApiException && (error.statusCode >= 500 || error.statusCode in listOf(401, 408, 429))
                    val state = if (transient) "pending" else if (error is ApiException && error.statusCode in listOf(409, 412) || error is IllegalStateException) "conflict" else "failed"
                    val message = if (state == "pending") "Saved on this device. Waiting to sync." else if (error is ApiException) error.userMessage() else "The return to ramp changed or its saved media is unavailable. Review it before saving again."
                    dao.rows(owner).firstOrNull { it.flightId == row.flightId && it.occurrenceId == row.occurrenceId }?.let { current ->
                        dao.put(current.copy(status = state, error = message,
                            attemptInputJson = if (releaseAttempt) null else current.attemptInputJson,
                            attemptRowVersion = if (releaseAttempt) null else current.attemptRowVersion,
                            attemptFilesJson = if (releaseAttempt) null else current.attemptFilesJson))
                    }
                    retry = retry || transient
                }
            }
        }
        cleanup()
        retry
    }

    suspend fun retry(id: String, occurrenceId: String) = operationGate.withLock {
        val owner = currentOwner() ?: return@withLock
        dao.rows(owner).firstOrNull { it.flightId == id && it.occurrenceId == occurrenceId }?.let {
            check(it.status == "pending") { "Review and Save this return to ramp against the current flight before retrying." }
            dao.put(it.copy(error = null))
        }
        schedule()
    }
    suspend fun discard(id: String, occurrenceId: String, expectedChangeId: String? = null) = operationGate.withLock {
        val owner = currentOwner()
        if (expectedChangeId != null) check(owner != null) { "Sign in to review this return to ramp." }
        if (owner == null) return@withLock
        val row = dao.rows(owner).firstOrNull { it.flightId == id && it.occurrenceId == occurrenceId }
        if (expectedChangeId != null) check(row?.changeId == expectedChangeId) { "This saved input changed. Reopen Review before continuing." }
        row?.let {
            check(it.attemptInputJson == null) { "Retry synchronization before discarding a request whose result is unknown." }
            dao.delete(owner, id, occurrenceId, it.changeId)
        }
        cleanup()
    }
    fun stopSession() { accepting = false; generation++ }
    fun resumeSession() { accepting = true; generation++ }
    suspend fun clear() = operationGate.withLock { generation++; acknowledgments.clear(); acknowledgedOwner = null; dao.clear(); cleanup() }
    private fun resetAcknowledgments(owner: String) {
        if (acknowledgedOwner != owner) { acknowledgments.clear(); acknowledgedOwner = owner }
    }
    private fun acknowledgedVersion(id: String, original: String): String {
        var version = original
        repeat(128) { version = acknowledgments[id]?.get(version)?.takeIf { next -> next != version } ?: return version }
        return version
    }
    private fun files(value: String): Map<String, String> = json.decodeFromString(value)
    private suspend fun cleanup() = operationGate.withLock {
        val references = dao.allRows().flatMap { files(it.filesJson).values + it.attemptFilesJson?.let(::files).orEmpty().values }.toSet()
        try { mediaStore.retainOnly(references) }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (_: Exception) { /* A later drain retries orphan cleanup. */ }
    }
    private suspend fun hydrate(inputJson: String, filesJson: String): SaveFlightReturnToRampRequest {
        val input = json.decodeFromString<SaveFlightReturnToRampRequest>(inputJson)
        val retained = files(filesJson)
        suspend fun attachments(kind: String, id: String, inputs: List<FlightServiceAttachmentInput>?) = inputs?.map { attachment ->
            attachment.copy(content = retained["$kind/$id/${attachment.id}"]?.let { Base64.getEncoder().encodeToString(mediaStore.read(it)) })
        }
        return input.copy(departure = input.departure?.copy(signatureContent = retained["signature"]?.let { Base64.getEncoder().encodeToString(mediaStore.read(it).also(::validateFlightSignatureBytes)) }),
            services = input.services.map { it.copy(input = it.input.copy(attachments = attachments("service", it.recordId, it.input.attachments))) },
            tasks = input.tasks.map { it.copy(input = it.input.copy(attachments = attachments("task", it.recordId, it.input.attachments))) })
    }
    private fun validateAttachment(file: FlightServiceAttachmentInput, bytes: ByteArray) {
        val mime = file.contentType.lowercase()
        fun starts(vararg prefix: Int) = bytes.size >= prefix.size && prefix.indices.all { bytes[it].toInt() and 255 == prefix[it] }
        fun ascii(offset: Int, text: String) = bytes.size >= offset + text.length && text.indices.all { bytes[offset + it] == text[it].code.toByte() }
        val allowed = when (file.kind) {
            0 -> bytes.size in 12..10 * 1024 * 1024 && when (mime) {
                "image/png" -> starts(137, 80, 78, 71)
                "image/jpeg" -> starts(255, 216, 255)
                "image/webp" -> ascii(0, "RIFF") && ascii(8, "WEBP")
                else -> false
            }
            1 -> bytes.size in 12..25 * 1024 * 1024 && when (mime) {
                "audio/mp4", "audio/m4a" -> ascii(4, "ftyp")
                "audio/mpeg", "audio/mp3" -> ascii(0, "ID3") || starts(255)
                "audio/ogg" -> ascii(0, "OggS")
                "audio/webm" -> starts(26, 69, 223, 163)
                else -> false
            }
            2 -> bytes.size in 5..2 * 1024 * 1024 && mime == "application/pdf" && ascii(0, "%PDF-")
            else -> false
        }
        check(allowed) { "The selected attachment has an invalid format or exceeds its size limit." }
    }
}
