package com.nags.operations.data.repo

import com.nags.operations.data.*
import com.nags.operations.data.db.dao.FlightServiceDao
import com.nags.operations.data.db.entities.FlightServicePendingEntity
import com.nags.operations.data.db.entities.FlightServiceFormDraftEntity
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

/** Normal performed services have stable identities; snapshots never replace durable local intent. */
@OptIn(ExperimentalCoroutinesApi::class)
class FlightServicesRepository(
    private val dao: FlightServiceDao,
    private val attachmentStore: ServiceAttachmentStore,
    ownerFlow: Flow<String?>,
    private val currentOwner: suspend () -> String?,
    private val online: () -> Boolean,
    private val sendSave: suspend (String, String, SaveFlightServiceRequest, String) -> FlightServiceSaved,
    private val sendDelete: suspend (String, String, String) -> FlightServiceDeleted,
    private val acceptedSave: suspend (String, FlightServiceSaved) -> Unit,
    private val acceptedDelete: suspend (String, FlightServiceDeleted) -> Unit,
    private val schedule: () -> Unit,
    private val json: Json,
    private val hasSavedArrival: suspend (String) -> Boolean,
    private val acknowledgedPredecessorVersion: suspend (String, String) -> String,
    private val fetchFlight: suspend (String) -> FlightDetail,
    private val now: () -> Instant = Instant::now,
    private val onAcknowledgedBeforeDelete: suspend (String, String, String) -> Unit = { _, _, _ -> },
    private val hasSavedCancellation: suspend (String) -> Boolean = { false },
    private val submissionGate: Mutex = Mutex(),
    private val onAttachmentsAcknowledged: suspend (String, FlightServiceSaved, Map<String, ByteArray>) -> Unit = { _, _, _ -> },
    private val hasUnacknowledgedDepartureAttempt: suspend (String) -> Boolean = { false },
    private val hasUnacknowledgedSiblingWork: suspend (String) -> Boolean = { false },
    private val effectiveLocalActuals: suspend (FlightDetail) -> FlightServiceActualBounds = {
        FlightServiceActualBounds(it.visibleArrival()?.ata, it.visibleDeparture()?.atd)
    },
) {
    private val gate = Mutex()
    private val localWrites = Mutex()
    @Volatile private var accepting = true
    @Volatile private var generation = 0L
    private data class Ack(val id: String, val service: FlightServiceRecord?, val version: String)
    private var acknowledgedOwner: String? = null
    private val acknowledgments = linkedMapOf<String, LinkedHashMap<String, Ack>>()
    val changes: Flow<List<FlightServicePendingEntity>> = ownerFlow.flatMapLatest { owner ->
        if (owner == null) flowOf(emptyList()) else dao.observe(owner)
    }
    val drafts: Flow<List<FlightServiceFormDraftEntity>> = ownerFlow.flatMapLatest { owner ->
        if (owner == null) flowOf(emptyList()) else dao.observeDrafts(owner)
    }

    suspend fun save(flight: FlightDetail, recordId: String, input: SaveFlightServiceRequest) =
        saveBatch(flight, listOf(FlightServiceChange(recordId, input)))
    suspend fun delete(flight: FlightDetail, recordId: String) = saveBatch(flight, listOf(FlightServiceChange(recordId, null)))

    /** Validate the complete batch and import its bytes before one Room transaction publishes it. */
    suspend fun saveBatch(flight: FlightDetail, changes: List<FlightServiceChange>, formDraft: FlightServiceFormDraft? = null): Unit = submissionGate.withLock {
        val owner = checkNotNull(currentOwner()) { "Sign in to save services." }
        val observed = generation
        val draftOnly = changes.isEmpty() && formDraft != null
        val taskAuthorizedDraft = draftOnly && flight.canSaveTasks
        check(validArrivalId(flight.currentStaffMemberId) && (flight.canSaveServices || taskAuthorizedDraft)) {
            "You cannot save work on this flight."
        }
        check(taskAuthorizedDraft || flight.allowedServiceIds != null) { "Connect to reload eligible services before saving this flight." }
        check(!flight.hasCancellationSubmission && !hasSavedCancellation(flight.id)) { "Revert cancellation and wait for it to sync before saving services." }
        check(flight.isInFlightWindow(now())) { "This flight is outside your current flight window." }
        check(changes.map { it.serviceRecordId.lowercase() }.distinct().size == changes.size && changes.all { validArrivalId(it.serviceRecordId) }) {
            "Review the service identities before saving."
        }
        val predecessor = acknowledgedPredecessorVersion(flight.id, flight.rowVersion)
        val eligible = flight.allowedServiceIds.orEmpty().toSet()
        // Causally prior local ATA/ATD relaxations are durable even before their HTTP ACK.
        // They affect local validation only; fresh uploads recheck saved server actuals.
        val localActuals = effectiveLocalActuals(flight)
        if (formDraft != null) {
            json.parseToJsonElement(formDraft.json)
            check(formDraft.attachments.all { validArrivalId(it.serviceRecordId) }) { "Review the draft service identities." }
            check(formDraft.attachments.map { it.serviceRecordId to it.attachment.id }.distinct().size == formDraft.attachments.size) { "Review the draft attachment identities." }
        }
        val persistedVersion = localWrites.withLock {
            checkSession(owner, observed)
            resetAcknowledgments(owner)
            val existingRows = dao.rows(owner).filter { it.flightId == flight.id }.associateBy { it.serviceRecordId }.toMutableMap()
            val oldDraftFiles = dao.drafts(owner).firstOrNull { it.flightId == flight.id }?.attachmentFilesJson?.let(::files).orEmpty()
            val version = follow(flight.id, predecessor).first
            // Build and validate every desired change before importing any files.
            val planned = changes.map { change ->
                val existing = existingRows[change.serviceRecordId]
                val preserve = existing?.status == "pending"
                val followed = follow(flight.id, flight.rowVersion, change.serviceRecordId,
                    flight.services.firstOrNull { it.id == change.serviceRecordId })
                val reviewed = if (preserve) existing.reviewedServiceJson?.let { json.decodeFromString<FlightServiceRecord>(it) } else followed.second
                val oldInput = existing?.inputJson?.let { json.decodeFromString<SaveFlightServiceRequest>(it) }
                val value = change.input?.let { request -> request.copy(attachments = request.attachments ?: oldInput?.attachments ?: reviewed?.input()?.attachments ?: emptyList()).normalized() }
                validate(value, if (preserve) checkNotNull(existing).rowVersion else version, reviewed, flight, eligible, localActuals)
                Triple(change, value, reviewed)
            }
            try {
                for ((change, value, reviewed) in planned) {
                    val existing = existingRows[change.serviceRecordId]
                    val preserve = existing?.status == "pending"
                    if (value == null && reviewed == null && existing?.attemptOperation == null) {
                        existingRows.remove(change.serviceRecordId)
                        continue
                    }
                    val draftFiles = oldDraftFiles.filterKeys { it.startsWith("${change.serviceRecordId}/") }.mapKeys { it.key.substringAfter('/') }
                    val attachmentSource = existing?.copy(attachmentFilesJson = json.encodeToString(draftFiles + files(existing.attachmentFilesJson)))
                        ?: FlightServicePendingEntity(owner, flight.id, change.serviceRecordId, flight.flightNumber, "draft", "save", null, version,
                            null, now().toString(), attachmentFilesJson = json.encodeToString(draftFiles))
                    val files = value?.let { importAttachments(it, attachmentSource, reviewed) } ?: emptyMap()
                    val stripped = value?.withoutBytes()
                    val row = if (preserve) checkNotNull(existing).copy(operation = if (value == null) "delete" else "save",
                        inputJson = stripped?.let { json.encodeToString(it) }, attachmentFilesJson = json.encodeToString(files),
                        savedAtUtc = now().toString(), error = null)
                    else FlightServicePendingEntity(owner, flight.id, change.serviceRecordId, flight.flightNumber, UUID.randomUUID().toString(),
                        if (value == null) "delete" else "save", stripped?.let { json.encodeToString(it) }, version,
                        reviewed?.let { json.encodeToString(it) }, now().toString(), needsServiceComparison = version != flight.rowVersion,
                        attachmentFilesJson = json.encodeToString(files))
                    existingRows[change.serviceRecordId] = row
                }
                val draft = formDraft?.let { draft ->
                    val oldFiles = oldDraftFiles
                    val draftFiles = linkedMapOf<String, String>()
                    draft.attachments.groupBy { it.serviceRecordId }.forEach { (recordId, attachments) ->
                        val values = attachments.map { it.attachment }
                        val input = SaveFlightServiceRequest(recordId, "", attachments = values).normalized()
                        check(input.attachments.orEmpty().size <= 10 && input.attachments.orEmpty().all {
                            validArrivalId(it.id) && it.kind in 0..2 && it.fileName.isNotBlank() && it.contentType.isNotBlank() && it.contentType.length <= 100
                        }) { "Review the draft service attachments." }
                        val old = oldFiles.filterKeys { it.startsWith("$recordId/") }.mapKeys { it.key.substringAfter('/') }
                        val sourceRow = existingRows[recordId]
                        val prior = sourceRow?.copy(attachmentFilesJson = json.encodeToString(files(sourceRow.attachmentFilesJson) + old))
                            ?: FlightServicePendingEntity(owner, flight.id, recordId, flight.flightNumber, "draft", "save", null, version, null, now().toString(), attachmentFilesJson = json.encodeToString(old))
                        val retained = flight.services.firstOrNull { it.id == recordId }
                        importAttachments(input, prior, retained).forEach { (id, file) -> draftFiles["$recordId/$id"] = file }
                    }
                    FlightServiceFormDraftEntity(owner, flight.id, draft.json, json.encodeToString(draftFiles), now().toString())
                }
                checkSession(owner, observed)
                dao.replaceForFlight(owner, flight.id, existingRows.values.toList(), draft)
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
            dao.put(it.copy(rowVersion = after, needsServiceComparison = true))
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
                val value = row.attemptInputJson?.let { json.decodeFromString<SaveFlightServiceRequest>(it) }
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
                            error = "The saved service attachment is unavailable. Reload the flight and review it.")) } }
                        continue
                    }
                    // Replay an immutable attempt from an older app even when a new closing
                    // rule would now reject it. The server must see it to acknowledge an
                    // already accepted write; fresh desired saves were checked before freezing.
                    validate(hydrated, version, row.reviewedServiceJson?.let { json.decodeFromString(it) },
                        replayingFrozenAttempt = true)
                    val result = sendSave(row.flightId, row.serviceRecordId, hydrated, version)
                    acknowledged = true
                    if (owner != currentOwner()) return@withLock false
                    acceptedSave(row.flightId, result)
                    try { onAttachmentsAcknowledged(row.flightId, result, files.mapValues { attachmentStore.read(it.value) }) }
                    catch (cancelled: CancellationException) { throw cancelled }
                    catch (_: Exception) { /* Optional preview caching cannot undo an HTTP acknowledgment. */ }
                    Ack(row.serviceRecordId, result.service, result.rowVersion)
                } else {
                    val result = sendDelete(row.flightId, row.serviceRecordId, version)
                    acknowledged = true
                    if (owner != currentOwner()) return@withLock false
                    acceptedDelete(row.flightId, result)
                    Ack(row.serviceRecordId, null, result.rowVersion)
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
                            dao.delete(owner, row.flightId, row.serviceRecordId, row.changeId)
                        else {
                            val desired = latest.inputJson?.let { json.decodeFromString<SaveFlightServiceRequest>(it) }
                            val uploadedIds = ack.service?.attachments?.map { it.id }?.toSet().orEmpty()
                            dao.put(latest.copy(rowVersion = ack.version, reviewedServiceJson = ack.service?.let { json.encodeToString(it) },
                                inputJson = desired?.let { json.encodeToString(it) }, attachmentFilesJson = json.encodeToString(files(latest.attachmentFilesJson).filterKeys { it !in uploadedIds }),
                                needsServiceComparison = false, attemptOperation = null, attemptInputJson = null,
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
                    "conflict" -> "The service changed. Reload the flight and review your saved service before saving again."
                    else -> if (error is ApiException) error.userMessage() else "Service was not accepted. Reload the flight and review it."
                })) } }
                retry = retry || transient
                if (status == "pending") unresolved += snapshot.flightId
                if (error is ApiException && error.statusCode == 401) return@withLock false
            }
        }
        retry || localWrites.withLock { dao.rows(owner).any { it.status == "pending" && it.flightId !in blocked } }
    }

    private suspend fun prepare(snapshot: FlightServicePendingEntity): FlightServicePendingEntity? {
        val original = localWrites.withLock { pendingRow(snapshot) } ?: return null
        if (original.attemptOperation != null) return original
        // Scope and catalog changes need not change the flight rowversion. Refresh every
        // new attempt, while an unknown transmitted attempt above must replay exactly.
        val live = fetchFlight(original.flightId)
        if (live.fromCache) throw IOException("Connect to reload the latest flight before synchronizing services.")
        return localWrites.withLock {
            val row = pendingRow(original) ?: return@withLock null
            if (row.rowVersion != original.rowVersion || row.reviewedServiceJson != original.reviewedServiceJson) return@withLock null
            val reviewed = row.reviewedServiceJson?.let { json.decodeFromString<FlightServiceRecord>(it) }
            val current = live.services.firstOrNull { it.id == row.serviceRecordId }
            fun comparable(service: FlightServiceRecord?) = service?.copy(performers = service.performers.sortedBy { it.id },
                attachments = service.attachments.sortedBy { it.id }, canChangeService = service.serviceSelectionEditable(),
                canDelete = service.serviceDeletable(), createdByDisplayName = null, endedByDisplayName = null)
            if (live.hasCancellationSubmission || live.rowVersion != row.rowVersion || comparable(current) != comparable(reviewed))
                throw ApiException(409, "Service details changed. Reload the flight and review them before saving again.")
            check(live.canSaveServices && live.allowedServiceIds != null) { "Reload the flight to check service access and eligibility." }
            validate(row.inputJson?.let { json.decodeFromString(it) }, row.rowVersion, reviewed, live, live.allowedServiceIds.toSet())
            row.copy(attemptOperation = row.operation, attemptInputJson = row.inputJson,
                attemptRowVersion = row.rowVersion, attemptAttachmentFilesJson = row.attachmentFilesJson).also { dao.put(it) }
        }
    }

    private fun validate(input: SaveFlightServiceRequest?, version: String, baseline: FlightServiceRecord?,
        flight: FlightDetail? = null, eligible: Set<String>? = null, localActuals: FlightServiceActualBounds? = null,
        replayingFrozenAttempt: Boolean = false) {
        check(runCatching { Base64.getDecoder().decode(version).size == 8 }.getOrDefault(false)) { "Reload the flight before saving services." }
        val current = baseline?.let { reviewed -> flight?.services?.firstOrNull { it.id == reviewed.id } }
        if (input == null) {
            check((baseline == null || baseline.serviceDeletable()) && current?.serviceDeletable() != false) {
                "You cannot remove this service record."
            }
            return
        }
        val arrival = if (localActuals == null) flight?.visibleArrival()?.ata else localActuals.arrivalUtc
        val departure = if (localActuals == null) flight?.visibleDeparture()?.atd else localActuals.departureUtc
        check(input.validationErrors(version, arrival, departure, eligible, requireClosedPerformers = !replayingFrozenAttempt).isEmpty()) {
            "Complete the required service fields and review employee periods."
        }
        if (baseline != null) {
            val old = baseline.input()
            if (!replayingFrozenAttempt && !input.serviceId.equals(old.serviceId, true)) check(
                baseline.serviceSelectionEditable() && current?.serviceSelectionEditable() != false) {
                "The selected service cannot change after another employee has entered its End time."
            }
            if (input.endUtc != old.endUtc) check(baseline.canSetEnd) { "Only the employee who entered the service end can change it." }
            if (!baseline.canEdit) check(baseline.canSetEnd && input.withoutBytes().copy(endUtc = old.endUtc) == old) {
                "Only the service creator can change its details. Other employees may record the end."
            }
            input.attachments.orEmpty().forEach { attachment ->
                baseline.attachments.firstOrNull { it.id == attachment.id }?.let { existing ->
                    check(attachment.kind == existing.kind && attachment.fileName == existing.fileName && attachment.contentType.equals(existing.contentType, ignoreCase = true) &&
                        attachment.content == null && attachment.contentUri == null) { "Use a new attachment identity when replacing a file." }
                }
            }
        }
    }

    private suspend fun importAttachments(input: SaveFlightServiceRequest, existing: FlightServicePendingEntity?, baseline: FlightServiceRecord?): Map<String, String> {
        val previous = files(existing?.attachmentFilesJson)
        val result = linkedMapOf<String, String>()
        var total = 0L
        input.attachments.orEmpty().forEach { attachment ->
            var key = when {
                attachment.contentUri != null -> attachmentStore.import(attachment.contentUri)
                attachment.content != null -> {
                    check(attachment.content.length <= 34_952_536) { "Service attachments must be at most 25 MB." }
                    attachmentStore.save(Base64.getDecoder().decode(attachment.content))
                }
                else -> previous[attachment.id]
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
                check(retained != null) { "The new service attachment is unavailable. Select it again before saving." }
            }
        }
        check(total <= 25L * 1024 * 1024) { "Service attachments must total at most 25 MB." }
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
        check(bytes.isNotEmpty() && valid) { "Review the service attachment type and size." }
    }
    private fun SaveFlightServiceRequest.withoutBytes() = copy(attachments = attachments?.map { it.copy(content = null, contentUri = null) }).normalized()
    private fun files(value: String?): Map<String, String> = value?.let { json.decodeFromString(it) } ?: emptyMap()
    private suspend fun reclaimFiles() {
        try { attachmentStore.retainOnly((dao.allRows().flatMap { files(it.attachmentFilesJson).values + files(it.attemptAttachmentFilesJson).values } +
            dao.allDrafts().flatMap { files(it.attachmentFilesJson).values }).toSet()) }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (_: Exception) { /* Cleanup cannot undo a completed local transaction. */ }
    }
    private suspend fun checkSession(owner: String, observed: Long) = check(accepting && observed == generation && owner == currentOwner()) {
        "Your session changed. Reopen the flight."
    }
    private suspend fun pendingRow(row: FlightServicePendingEntity) = dao.rows(row.ownerSubject).firstOrNull {
        it.flightId == row.flightId && it.serviceRecordId == row.serviceRecordId && it.changeId == row.changeId && it.status == "pending"
    }
    private fun resetAcknowledgments(owner: String?) { if (owner != acknowledgedOwner) { acknowledgments.clear(); acknowledgedOwner = owner } }
    private fun follow(flightId: String, original: String, recordId: String? = null, baseline: FlightServiceRecord? = null): Pair<String, FlightServiceRecord?> {
        var version = original
        var reviewed = baseline
        val visited = mutableSetOf<String>()
        while (visited.add(version)) {
            val ack = acknowledgments[flightId]?.get(version) ?: break
            if (ack.id == recordId) reviewed = ack.service
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
        dao.rows(owner).firstOrNull { it.flightId == flightId && it.serviceRecordId == recordId }?.let { dao.put(it.copy(status = "pending", error = null)) }
        runCatching { schedule() }
    }
    suspend fun discard(flightId: String, recordId: String) = localWrites.withLock {
        val owner = currentOwner() ?: return@withLock
        check(accepting) { "Your session changed. Reopen Sync Center." }
        dao.rows(owner).firstOrNull { it.flightId == flightId && it.serviceRecordId == recordId }?.let {
            check(it.attemptOperation == null || it.status != "pending") { "This service may already be saved online. Retry synchronization before discarding it." }
            dao.delete(owner, flightId, recordId, it.changeId)
        }
        reclaimFiles()
    }
    suspend fun review(flightId: String): FlightDetail = fetchFlight(flightId).also { check(!it.fromCache) { "Connect to reload the latest flight." } }
    suspend fun formDraft(flightId: String): FlightServiceFormDraftEntity? = localWrites.withLock {
        val owner = currentOwner() ?: return@withLock null
        dao.drafts(owner).firstOrNull { it.flightId == flightId }
    }
    suspend fun attachmentPreview(flightId: String, recordId: String, attachmentId: String, attempted: Boolean = false): File? = localWrites.withLock {
        val owner = currentOwner() ?: return@withLock null
        val row = dao.rows(owner).firstOrNull { it.flightId == flightId && it.serviceRecordId == recordId }
        val key = files(if (attempted) row?.attemptAttachmentFilesJson else row?.attachmentFilesJson)[attachmentId]
            ?: if (!attempted) dao.drafts(owner).firstOrNull { it.flightId == flightId }?.let { files(it.attachmentFilesJson)["$recordId/$attachmentId"] } else null
        key?.let { attachmentStore.previewFile(it) }
    }
    suspend fun pause() {
        localWrites.withLock { accepting = false; generation++ }
        gate.withLock { localWrites.withLock { acknowledgments.clear(); acknowledgedOwner = null } }
    }
    suspend fun resume() = localWrites.withLock { accepting = true; reclaimFiles() }
    suspend fun clear() = gate.withLock { localWrites.withLock { generation++; acknowledgments.clear(); acknowledgedOwner = null; dao.clear(); dao.clearDrafts(); reclaimFiles() } }
}
