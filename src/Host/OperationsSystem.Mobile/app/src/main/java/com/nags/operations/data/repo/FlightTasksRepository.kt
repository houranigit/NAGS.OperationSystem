package com.nags.operations.data.repo

import com.nags.operations.data.*
import com.nags.operations.data.db.dao.FlightTaskDao
import com.nags.operations.data.db.entities.FlightTaskPendingEntity
import io.ktor.client.plugins.HttpRequestTimeoutException
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

/** Normal performed tasks have stable identities; snapshots never replace durable local intent. */
@OptIn(ExperimentalCoroutinesApi::class)
class FlightTasksRepository(
    private val dao: FlightTaskDao,
    private val attachmentStore: TaskAttachmentStore,
    ownerFlow: Flow<String?>,
    private val currentOwner: suspend () -> String?,
    private val online: () -> Boolean,
    private val sendSave: suspend (String, String, SaveFlightTaskRequest, String) -> FlightTaskSaved,
    private val sendDelete: suspend (String, String, String) -> FlightTaskDeleted,
    private val acceptedSave: suspend (String, FlightTaskSaved) -> Unit,
    private val acceptedDelete: suspend (String, FlightTaskDeleted) -> Unit,
    private val schedule: () -> Unit,
    private val json: Json,
    private val hasSavedArrival: suspend (String) -> Boolean,
    private val acknowledgedPredecessorVersion: suspend (String, String) -> String,
    private val fetchFlight: suspend (String) -> FlightDetail,
    private val now: () -> Instant = Instant::now,
    private val resourceCatalogs: suspend () -> FlightTaskResourceCatalogs = { FlightTaskResourceCatalogs() },
    private val legacyDraftAttachment: suspend (String, String, String) -> ByteArray? = { _, _, _ -> null },
    private val hasUnacknowledgedSiblingWork: suspend (String) -> Boolean = { false },
    private val onAcknowledgedBeforeDelete: suspend (String, String, String) -> Unit = { _, _, _ -> },
    private val hasSavedCancellation: suspend (String) -> Boolean = { false },
    private val submissionGate: Mutex = Mutex(),
    private val onAttachmentsAcknowledged: suspend (String, FlightTaskSaved, Map<String, ByteArray>) -> Unit = { _, _, _ -> },
    private val hasUnacknowledgedDepartureAttempt: suspend (String) -> Boolean = { false },
    private val effectiveLocalActuals: suspend (FlightDetail) -> FlightServiceActualBounds = {
        FlightServiceActualBounds(it.visibleArrival()?.ata, it.visibleDeparture()?.atd)
    },
) {
    private val gate = Mutex()
    private val localWrites = Mutex()
    @Volatile private var accepting = true
    @Volatile private var generation = 0L
    private data class Ack(val id: String, val task: FlightTaskRecord?, val version: String)
    private var acknowledgedOwner: String? = null
    private val acknowledgments = linkedMapOf<String, LinkedHashMap<String, Ack>>()
    val changes: Flow<List<FlightTaskPendingEntity>> = ownerFlow.flatMapLatest { owner ->
        if (owner == null) flowOf(emptyList()) else dao.observe(owner)
    }

    suspend fun save(flight: FlightDetail, recordId: String, input: SaveFlightTaskRequest) =
        saveBatch(flight, listOf(FlightTaskChange(recordId, input)))
    suspend fun delete(flight: FlightDetail, recordId: String) = saveBatch(flight, listOf(FlightTaskChange(recordId, null)))

    /** Validate the complete batch and import its bytes before one Room transaction publishes task intent. */
    suspend fun saveBatch(flight: FlightDetail, changes: List<FlightTaskChange>): Unit = submissionGate.withLock {
        val owner = checkNotNull(currentOwner()) { "Sign in to save tasks." }
        val observed = generation
        check(flight.canSaveTasks && validArrivalId(flight.currentStaffMemberId)) { "You cannot save tasks on this flight." }
        check(!flight.hasCancellationSubmission && !hasSavedCancellation(flight.id)) { "Revert cancellation and wait for it to sync before saving tasks." }
        check(flight.isInFlightWindow(now())) { "This flight is outside your current flight window." }
        check(changes.map { it.taskRecordId.lowercase() }.distinct().size == changes.size && changes.all { validArrivalId(it.taskRecordId) }) {
            "Review the task identities before saving."
        }
        val predecessor = acknowledgedPredecessorVersion(flight.id, flight.rowVersion)
        val catalogs = resourceCatalogs()
        // Causally prior local ATA/ATD relaxations are durable even before their HTTP ACK.
        // They affect local validation only; fresh uploads recheck saved server actuals.
        val localActuals = effectiveLocalActuals(flight)
        val persistedVersion = localWrites.withLock {
            checkSession(owner, observed)
            resetAcknowledgments(owner)
            val existingRows = dao.rows(owner).filter { it.flightId == flight.id }.associateBy { it.taskRecordId }.toMutableMap()
            val version = follow(flight.id, predecessor).first
            // Build and validate every desired change before importing any files.
            val planned = changes.map { change ->
                val existing = existingRows[change.taskRecordId]
                val preserve = existing?.status == "pending"
                val followed = follow(flight.id, flight.rowVersion, change.taskRecordId,
                    flight.tasks.firstOrNull { it.id == change.taskRecordId })
                val reviewed = if (preserve) existing.reviewedTaskJson?.let { json.decodeFromString<FlightTaskRecord>(it) } else followed.second
                val oldInput = existing?.inputJson?.let { json.decodeFromString<SaveFlightTaskRequest>(it) }
                val value = change.input?.let { request -> request.copy(attachments = request.attachments ?: oldInput?.attachments ?: reviewed?.input()?.attachments ?: emptyList()).normalized() }
                validate(value, if (preserve) checkNotNull(existing).rowVersion else version, reviewed, flight, catalogs, localActuals)
                Triple(change, value, reviewed)
            }
            try {
                for ((change, value, reviewed) in planned) {
                    val existing = existingRows[change.taskRecordId]
                    val preserve = existing?.status == "pending"
                    if (value == null && reviewed == null && existing?.attemptOperation == null) {
                        existingRows.remove(change.taskRecordId)
                        continue
                    }
                    val attachmentSource = existing ?: FlightTaskPendingEntity(owner, flight.id, change.taskRecordId,
                        flight.flightNumber, "draft", "save", null, version, null, now().toString())
                    val files = value?.let { importAttachments(it, attachmentSource, reviewed) } ?: emptyMap()
                    val stripped = value?.withoutBytes()
                    val row = if (preserve) checkNotNull(existing).copy(operation = if (value == null) "delete" else "save",
                        inputJson = stripped?.let { json.encodeToString(it) }, attachmentFilesJson = json.encodeToString(files),
                        savedAtUtc = now().toString(), error = null)
                    else FlightTaskPendingEntity(owner, flight.id, change.taskRecordId, flight.flightNumber, UUID.randomUUID().toString(),
                        if (value == null) "delete" else "save", stripped?.let { json.encodeToString(it) }, version,
                        reviewed?.let { json.encodeToString(it) }, now().toString(), needsTaskComparison = version != flight.rowVersion,
                        attachmentFilesJson = json.encodeToString(files))
                    existingRows[change.taskRecordId] = row
                }
                checkSession(owner, observed)
                dao.replaceForFlight(owner, flight.id, existingRows.values.toList())
                version
            } finally { reclaimFiles() }
        }
        // A predecessor ACK racing batch publication is seen by this lookup or its callback.
        val latest = acknowledgedPredecessorVersion(flight.id, persistedVersion)
        if (latest != persistedVersion) onPriorWriteAcknowledged(flight.id, persistedVersion, latest)
        runCatching { schedule() }
    }

    suspend fun onPriorWriteAcknowledged(flightId: String, requestVersion: String, responseVersion: String) = localWrites.withLock {
        val owner = currentOwner() ?: return@withLock
        advanceRows(owner, flightId, requestVersion, responseVersion)
    }

    private suspend fun advanceRows(owner: String, flightId: String, before: String, after: String) {
        if (before == after) return
        dao.rows(owner).filter { it.flightId == flightId && it.status == "pending" && it.rowVersion == before }.forEach {
            dao.put(it.copy(rowVersion = after, needsTaskComparison = true))
        }
    }

    suspend fun acknowledgedVersion(flightId: String, reviewedVersion: String): String = localWrites.withLock {
        resetAcknowledgments(currentOwner())
        follow(flightId, reviewedVersion).first
    }

    suspend fun syncPending(): Boolean = gate.withLock {
        if (!accepting) return@withLock false
        val owner = currentOwner() ?: return@withLock false
        if (!online()) return@withLock dao.rows(owner).any { it.status == "pending" }
        val blocked = mutableSetOf<String>()
        val unresolved = mutableSetOf<String>()
        var retry = false
        // An edited desired value can move savedAtUtc after later rows. Unknown attempts
        // must still acknowledge first, including after process death.
        for (snapshot in localWrites.withLock { dao.rows(owner).filter { it.status == "pending" }
            .sortedBy { if (it.attemptOperation != null) 0 else 1 } }) {
            if (!accepting || owner != currentOwner()) return@withLock false
            if (snapshot.flightId in unresolved) continue
            if (snapshot.attemptOperation == null && (hasSavedArrival(snapshot.flightId) ||
                    hasUnacknowledgedDepartureAttempt(snapshot.flightId) || hasUnacknowledgedSiblingWork(snapshot.flightId))) {
                blocked += snapshot.flightId
                continue
            }
            var acknowledged = false
            try {
                val row = prepare(snapshot) ?: continue
                val version = checkNotNull(row.attemptRowVersion)
                val files = files(row.attemptAttachmentFilesJson)
                val value = row.attemptInputJson?.let { json.decodeFromString<SaveFlightTaskRequest>(it) }
                val ack = if (row.attemptOperation == "save") {
                    val hydrated = try {
                        checkNotNull(value).copy(attachments = value.attachments?.map { attachment ->
                            attachment.copy(content = files[attachment.id]?.let { key ->
                                val bytes = attachmentStore.read(key)
                                validateAttachmentBytes(attachment, bytes)
                                Base64.getEncoder().encodeToString(bytes)
                            })
                        })
                    } catch (cancelled: CancellationException) { throw cancelled }
                    catch (_: Exception) {
                        localWrites.withLock { pendingRow(row)?.let { dao.put(it.copy(status = "failed",
                            error = "The saved task attachment is unavailable. Reload the flight and review it.")) } }
                        continue
                    }
                    // Replays keep their exact bytes and reach the server to recover an
                    // earlier accepted write, even if newer completion rules would reject it.
                    validate(hydrated, version, row.reviewedTaskJson?.let { json.decodeFromString(it) },
                        replayingFrozenAttempt = true)
                    val result = sendSave(row.flightId, row.taskRecordId, hydrated, version)
                    acknowledged = true
                    if (owner != currentOwner()) return@withLock false
                    acceptedSave(row.flightId, result)
                    try { onAttachmentsAcknowledged(row.flightId, result, files.mapValues { attachmentStore.read(it.value) }) }
                    catch (cancelled: CancellationException) { throw cancelled }
                    catch (_: Exception) { /* Optional preview caching cannot undo an HTTP acknowledgment. */ }
                    Ack(row.taskRecordId, result.task, result.rowVersion)
                } else {
                    val result = sendDelete(row.flightId, row.taskRecordId, version)
                    acknowledged = true
                    if (owner != currentOwner()) return@withLock false
                    acceptedDelete(row.flightId, result)
                    Ack(row.taskRecordId, null, result.rowVersion)
                }
                localWrites.withLock { remember(owner, row.flightId, version, ack) }
                // Persist dependent queue versions before releasing this exact replay proof.
                onAcknowledgedBeforeDelete(row.flightId, version, ack.version)
                localWrites.withLock {
                    // Every sibling row must retain this version before the replay proof
                    // can be deleted, otherwise a crash strands later work at the old version.
                    advanceRows(owner, row.flightId, version, ack.version)
                    pendingRow(row)?.let { latest ->
                        if (latest.operation == row.attemptOperation && latest.inputJson == row.attemptInputJson && latest.attachmentFilesJson == row.attemptAttachmentFilesJson)
                            dao.delete(owner, row.flightId, row.taskRecordId, row.changeId)
                        else {
                            val desired = latest.inputJson?.let { json.decodeFromString<SaveFlightTaskRequest>(it) }
                            val uploadedIds = ack.task?.attachments?.map { it.id }?.toSet().orEmpty()
                            dao.put(latest.copy(rowVersion = ack.version, reviewedTaskJson = ack.task?.let { json.encodeToString(it) },
                                inputJson = desired?.let { json.encodeToString(it) }, attachmentFilesJson = json.encodeToString(files(latest.attachmentFilesJson).filterKeys { it !in uploadedIds }),
                                needsTaskComparison = false, attemptOperation = null, attemptInputJson = null,
                                attemptRowVersion = null, attemptAttachmentFilesJson = null, error = null))
                        }
                    }
                    reclaimFiles()
                }
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (error: Exception) {
                if (owner != currentOwner()) return@withLock false
                val transient = acknowledged || error is IOException || error is HttpRequestTimeoutException || error is SerializationException ||
                    error is ApiException && (error.statusCode >= 500 || error.statusCode in listOf(408, 429))
                val status = if (transient || error is ApiException && error.statusCode == 401) "pending"
                    else if (error is ApiException && error.statusCode in listOf(409, 412)) "conflict" else "failed"
                localWrites.withLock { pendingRow(snapshot)?.let { dao.put(it.copy(status = status, error = when (status) {
                    "pending" -> "Saved on this device. Waiting to sync; reconnect or sign in again."
                    "conflict" -> "The task changed. Reload the flight and review your saved task before saving again."
                    else -> if (error is ApiException) error.userMessage() else "Task was not accepted. Reload the flight and review it."
                })) } }
                retry = retry || transient
                if (status == "pending") unresolved += snapshot.flightId
                if (error is ApiException && error.statusCode == 401) return@withLock false
            }
        }
        retry || localWrites.withLock { dao.rows(owner).any { it.status == "pending" && it.flightId !in blocked } }
    }

    private suspend fun prepare(snapshot: FlightTaskPendingEntity): FlightTaskPendingEntity? {
        val original = localWrites.withLock { pendingRow(snapshot) } ?: return null
        if (original.attemptOperation != null) return original
        // Scope and catalog changes need not change the flight rowversion. Refresh every
        // new attempt, while an unknown transmitted attempt above must replay exactly.
        val live = fetchFlight(original.flightId)
        if (live.fromCache) throw IOException("Connect to reload the latest flight before synchronizing tasks.")
        return localWrites.withLock {
            val row = pendingRow(original) ?: return@withLock null
            if (row.rowVersion != original.rowVersion || row.reviewedTaskJson != original.reviewedTaskJson) return@withLock null
            val reviewed = row.reviewedTaskJson?.let { json.decodeFromString<FlightTaskRecord>(it) }
            val current = live.tasks.firstOrNull { it.id == row.taskRecordId }
            fun comparable(task: FlightTaskRecord?) = task?.copy(performers = task.performers.sortedBy { it.id },
                tools = task.tools.sortedBy { it.id }, materials = task.materials.sortedBy { it.id },
                generalSupports = task.generalSupports.sortedBy { it.id }, attachments = task.attachments.sortedBy { it.id },
                createdByDisplayName = null, endedByDisplayName = null)
            if (live.hasCancellationSubmission || live.rowVersion != row.rowVersion || comparable(current) != comparable(reviewed))
                throw ApiException(409, "Task details changed. Reload the flight and review them before saving again.")
            check(live.canSaveTasks) { "Reload the flight to check task access." }
            validate(row.inputJson?.let { json.decodeFromString(it) }, row.rowVersion, reviewed, live, resourceCatalogs())
            row.copy(attemptOperation = row.operation, attemptInputJson = row.inputJson,
                attemptRowVersion = row.rowVersion, attemptAttachmentFilesJson = row.attachmentFilesJson).also { dao.put(it) }
        }
    }

    private fun validate(input: SaveFlightTaskRequest?, version: String, baseline: FlightTaskRecord?,
        flight: FlightDetail? = null, catalogs: FlightTaskResourceCatalogs? = null, localActuals: FlightServiceActualBounds? = null, replayingFrozenAttempt: Boolean = false) {
        check(runCatching { Base64.getDecoder().decode(version).size == 8 }.getOrDefault(false)) { "Reload the flight before saving tasks." }
        val current = baseline?.let { reviewed -> flight?.tasks?.firstOrNull { it.id == reviewed.id } }
        if (input == null) {
            check((baseline == null || baseline.canDelete) && current?.canDelete != false) {
                "You cannot remove this task record."
            }
            return
        }
        if (baseline != null && input.normalized() == baseline.input()) {
            // Explicit review can restore an unchanged, now read-only record. A no-op
            // still checks request shape, then reaches the API's exact acknowledgment.
            check(input.validationErrors(version, baseline = baseline, requireClosedChildren = false).isEmpty()) {
                "Review the saved task fields."
            }
            return
        }
        val arrival = if (localActuals == null) flight?.visibleArrival()?.ata else localActuals.arrivalUtc
        val departure = if (localActuals == null) flight?.visibleDeparture()?.atd else localActuals.departureUtc
        check(input.validationErrors(version, arrival, departure, catalogs, baseline, requireClosedChildren = !replayingFrozenAttempt).isEmpty()) {
            "Complete the required task fields and review employee periods."
        }
        if (baseline != null) {
            val old = baseline.input()
            if (input.taskType != old.taskType) check(
                baseline.canChangeType && current?.canChangeType != false) {
                "Only the employee who created the task can change its type."
            }
            if (input.endUtc != old.endUtc) check(baseline.canSetEnd) { "Only the employee who entered the task end can change it." }
            if (!baseline.canEdit) check(baseline.canSetEnd && input.withoutBytes().copy(endUtc = old.endUtc) == old) {
                "Only the task creator can change its details. Other employees may record the end."
            }
            input.attachments.orEmpty().forEach { attachment ->
                baseline.attachments.firstOrNull { it.id == attachment.id }?.let { existing ->
                    check(attachment.kind == existing.kind && attachment.fileName == existing.fileName && attachment.contentType.equals(existing.contentType, ignoreCase = true) &&
                        attachment.content == null && attachment.contentUri == null) { "Use a new attachment identity when replacing a file." }
                }
            }
        }
    }

    private suspend fun importAttachments(input: SaveFlightTaskRequest, existing: FlightTaskPendingEntity?, baseline: FlightTaskRecord?): Map<String, String> {
        val previous = files(existing?.attachmentFilesJson)
        val result = linkedMapOf<String, String>()
        var total = 0L
        input.attachments.orEmpty().forEach { attachment ->
            var key = when {
                attachment.contentUri != null -> attachmentStore.import(attachment.contentUri)
                attachment.content != null -> {
                    check(attachment.content.length <= 34_952_536) { "Task attachments must be at most 25 MB." }
                    attachmentStore.save(Base64.getDecoder().decode(attachment.content))
                }
                else -> previous[attachment.id] ?: if (baseline?.attachments?.any { it.id == attachment.id } != true && existing != null) {
                    legacyDraftAttachment(existing.flightId, existing.taskRecordId, attachment.id)?.let { bytes ->
                        validateAttachmentBytes(attachment, bytes)
                        attachmentStore.save(bytes)
                    }
                } else null
            }
            if (key != null) {
                val bytes = attachmentStore.read(key)
                validateAttachmentBytes(attachment, bytes)
                previous[attachment.id]?.takeIf { it != key }?.let { old ->
                    check(attachmentStore.read(old).contentEquals(bytes)) { "Use a new attachment identity when replacing a file." }
                    key = old
                }
                result[attachment.id] = checkNotNull(key)
                total += bytes.size
            } else {
                val retained = baseline?.attachments?.firstOrNull { it.id == attachment.id }
                check(retained != null) { "The new task attachment is unavailable. Select it again before saving." }
            }
        }
        check(total <= 25L * 1024 * 1024) { "Task attachments must total at most 25 MB." }
        return result
    }
    private fun validateAttachmentBytes(attachment: FlightServiceAttachmentInput, bytes: ByteArray) {
        val mime = attachment.contentType.lowercase()
        fun starts(vararg prefix: Int) = bytes.size >= prefix.size && prefix.indices.all { bytes[it].toInt() and 255 == prefix[it] }
        fun ascii(offset: Int, value: String) = bytes.size >= offset + value.length && value.indices.all { bytes[offset + it] == value[it].code.toByte() }
        val valid = when (attachment.kind) {
            0 -> bytes.size in 12..10 * 1024 * 1024 && when (mime) {
                "image/png" -> starts(137, 80, 78, 71)
                "image/jpeg" -> starts(255, 216, 255)
                "image/webp" -> ascii(0, "RIFF") && ascii(8, "WEBP")
                else -> false
            }
            1 -> bytes.size in 4..25 * 1024 * 1024 && when (mime) {
                "audio/mp4", "audio/m4a" -> bytes.size >= 12 && ascii(4, "ftyp")
                "audio/mpeg", "audio/mp3" -> ascii(0, "ID3") || bytes.size >= 2 && bytes[0].toInt() and 255 == 255 && bytes[1].toInt() and 224 == 224
                "audio/ogg" -> ascii(0, "OggS")
                "audio/webm" -> starts(26, 69, 223, 163)
                else -> false
            }
            2 -> bytes.size <= 2 * 1024 * 1024 && mime == "application/pdf" && ascii(0, "%PDF-")
            else -> false
        }
        check(bytes.isNotEmpty() && valid) { "Review the task attachment type and size." }
    }
    private fun SaveFlightTaskRequest.withoutBytes() = copy(attachments = attachments?.map { it.copy(content = null, contentUri = null) }).normalized()
    private fun files(value: String?): Map<String, String> = value?.let { json.decodeFromString(it) } ?: emptyMap()
    private suspend fun reclaimFiles() {
        try { attachmentStore.retainOnly(dao.allRows().flatMap { files(it.attachmentFilesJson).values + files(it.attemptAttachmentFilesJson).values }.toSet()) }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (_: Exception) { /* Cleanup cannot undo a completed local transaction. */ }
    }
    private suspend fun checkSession(owner: String, observed: Long) = check(accepting && observed == generation && owner == currentOwner()) {
        "Your session changed. Reopen the flight."
    }
    private suspend fun pendingRow(row: FlightTaskPendingEntity) = dao.rows(row.ownerSubject).firstOrNull {
        it.flightId == row.flightId && it.taskRecordId == row.taskRecordId && it.changeId == row.changeId && it.status == "pending"
    }
    private fun resetAcknowledgments(owner: String?) { if (owner != acknowledgedOwner) { acknowledgments.clear(); acknowledgedOwner = owner } }
    private fun follow(flightId: String, original: String, recordId: String? = null, baseline: FlightTaskRecord? = null): Pair<String, FlightTaskRecord?> {
        var version = original
        var reviewed = baseline
        val visited = mutableSetOf<String>()
        while (visited.add(version)) {
            val ack = acknowledgments[flightId]?.get(version) ?: break
            if (ack.id == recordId) reviewed = ack.task
            if (ack.version == version) break
            version = ack.version
        }
        return version to reviewed
    }
    private fun remember(owner: String, flightId: String, before: String, ack: Ack) {
        resetAcknowledgments(owner)
        val chain = acknowledgments.remove(flightId) ?: linkedMapOf()
        chain[before] = ack
        while (chain.size > 64) chain.remove(chain.keys.first())
        acknowledgments[flightId] = chain
        while (acknowledgments.size > 64) acknowledgments.remove(acknowledgments.keys.first())
    }
    suspend fun hasSavedInput(flightId: String): Boolean = localWrites.withLock {
        val owner = currentOwner() ?: return@withLock false
        dao.rows(owner).any { it.flightId == flightId }
    }
    suspend fun hasUnacknowledgedAttempt(flightId: String): Boolean = localWrites.withLock {
        val owner = currentOwner() ?: return@withLock false
        dao.rows(owner).any { it.flightId == flightId && it.status == "pending" && it.attemptOperation != null }
    }
    suspend fun retry(flightId: String, recordId: String) = localWrites.withLock {
        val owner = currentOwner() ?: return@withLock
        check(accepting) { "Your session changed. Reopen Sync Center." }
        dao.rows(owner).firstOrNull { it.flightId == flightId && it.taskRecordId == recordId }?.let { dao.put(it.copy(status = "pending", error = null)) }
        runCatching { schedule() }
    }
    suspend fun discard(flightId: String, recordId: String) = localWrites.withLock {
        val owner = currentOwner() ?: return@withLock
        check(accepting) { "Your session changed. Reopen Sync Center." }
        dao.rows(owner).firstOrNull { it.flightId == flightId && it.taskRecordId == recordId }?.let {
            check(it.attemptOperation == null || it.status != "pending") { "This task may already be saved online. Retry synchronization before discarding it." }
            dao.delete(owner, flightId, recordId, it.changeId)
        }
        reclaimFiles()
    }
    suspend fun review(flightId: String): FlightDetail = fetchFlight(flightId).also { check(!it.fromCache) { "Connect to reload the latest flight." } }
    suspend fun attachmentPreview(flightId: String, recordId: String, attachmentId: String, attempted: Boolean = false): File? = localWrites.withLock {
        val owner = currentOwner() ?: return@withLock null
        val row = dao.rows(owner).firstOrNull { it.flightId == flightId && it.taskRecordId == recordId }
        val key = files(if (attempted) row?.attemptAttachmentFilesJson else row?.attachmentFilesJson)[attachmentId]

        key?.let { attachmentStore.previewFile(it) }
    }
    suspend fun pause() {
        localWrites.withLock { accepting = false; generation++ }
        gate.withLock { localWrites.withLock { acknowledgments.clear(); acknowledgedOwner = null } }
    }
    suspend fun resume() = localWrites.withLock { accepting = true; reclaimFiles() }
    suspend fun clear() = gate.withLock { localWrites.withLock { generation++; acknowledgments.clear(); acknowledgedOwner = null; dao.clear(); reclaimFiles() } }
}
