package com.nags.operations.data.repo

import androidx.room.withTransaction
import com.nags.operations.data.*
import com.nags.operations.data.db.CatalogDatabase
import com.nags.operations.data.db.dao.*
import com.nags.operations.data.db.entities.*
import io.ktor.client.plugins.HttpRequestTimeoutException
import java.io.IOException
import java.util.Base64
import java.util.UUID
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.withContext
import kotlin.coroutines.AbstractCoroutineContextElement
import kotlin.coroutines.CoroutineContext
import kotlinx.serialization.SerializationException
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

/** Shared transaction boundary; the Room adapter uses the existing operational database. */
interface FlightOperationsBatchStore {
    val batches: FlightOperationsBatchDao
    val arrivals: FlightArrivalDao
    val departures: FlightDepartureDao
    val services: FlightServiceDao
    val tasks: FlightTaskDao
    suspend fun transaction(block: suspend () -> Unit)
}

class RoomFlightOperationsBatchStore(private val db: CatalogDatabase) : FlightOperationsBatchStore {
    override val batches get() = db.flightOperationsBatchDao()
    override val arrivals get() = db.flightArrivalDao()
    override val departures get() = db.flightDepartureDao()
    override val services get() = db.flightServiceDao()
    override val tasks get() = db.flightTaskDao()
    override suspend fun transaction(block: suspend () -> Unit) = db.withTransaction { block() }
}

/** The graph's operations mutex serializes publication, uploads, and account transitions. */
class FlightOperationsBatchesRepository(
    private val store: FlightOperationsBatchStore,
    private val currentOwner: suspend () -> String?,
    private val online: () -> Boolean,
    private val fetch: suspend (String) -> FlightDetail,
    private val send: suspend (String, SaveFlightOperationsBatchRequest, String) -> FlightDetail,
    private val accepted: suspend (FlightDetail, FlightOperationsBatchInput) -> Unit,
    private val hydrateArrival: (FlightArrivalPendingEntity) -> SaveFlightArrivalRequest,
    private val hydrateDeparture: suspend (FlightDeparturePendingEntity) -> SaveFlightDepartureRequest,
    private val hydrateService: suspend (FlightServicePendingEntity) -> SaveFlightServiceRequest?,
    private val hydrateTask: suspend (FlightTaskPendingEntity) -> SaveFlightTaskRequest?,
    private val acknowledgedVersion: suspend (String, String) -> String,
    private val deferCleanup: (Boolean) -> Unit,
    private val cleanup: suspend () -> Unit,
    private val schedule: () -> Unit,
    private val json: Json,
    private val onAcknowledged: suspend (String, String, String) -> Unit = { _, _, _ -> },
    private val mayPublishFlight: suspend (String) -> Boolean = { true },
) {
    private class Saving(val repository: FlightOperationsBatchesRepository) : AbstractCoroutineContextElement(Key) {
        companion object Key : CoroutineContext.Key<Saving>
    }
    private class Reviewing(val flight: FlightDetail, val target: FlightOperationReviewTarget) : AbstractCoroutineContextElement(Key) {
        companion object Key : CoroutineContext.Key<Reviewing>
    }
    private val dao get() = store.batches
    private fun decode(value: String) = json.decodeFromString<FlightOperationsBatchInput>(value)

    private suspend fun snapshot(owner: String) = FlightOperationsBatchInput(
        services = store.services.rows(owner), tasks = store.tasks.rows(owner),
    )
    private suspend fun snapshots(owner: String): Map<String, FlightOperationsBatchInput> {
        val work = snapshot(owner)
        val arrivals = store.arrivals.rows(owner)
        val departures = store.departures.rows(owner)
        val ids = (arrivals.map { it.flightId } + departures.map { it.flightId } + work.services.map { it.flightId } + work.tasks.map { it.flightId }).toSet()
        return ids.associateWith { id -> FlightOperationsBatchInput(arrivals.firstOrNull { it.flightId == id },
            departures.firstOrNull { it.flightId == id }, work.services.filter { it.flightId == id }, work.tasks.filter { it.flightId == id }) }
    }

    /** No observer can see one section committed without the rest of this Save. */
    suspend fun save(saveSections: suspend () -> Unit) {
        if (currentCoroutineContext()[Saving]?.repository === this) { saveSections(); return }
        withContext(Saving(this)) { saveWithinContext(saveSections) }
    }

    /** Planning intent and its never-sent dependent work commit before any file cleanup or scheduling. */
    suspend fun savePlanning(stageIdentity: suspend () -> Unit, saveSections: suspend () -> Unit,
        afterStaging: suspend () -> Unit) {
        check(currentCoroutineContext()[Saving] == null) { "Planning must own the outer flight Save." }
        withContext(Saving(this)) { saveWithinContext({ stageIdentity(); saveSections() }, afterStaging) }
    }

    private suspend fun saveWithinContext(saveSections: suspend () -> Unit, afterStaging: suspend () -> Unit = {}) {
        val owner = checkNotNull(currentOwner()) { "Sign in to save this flight." }
        val reviewing = currentCoroutineContext()[Reviewing]
        deferCleanup(true)
        var primaryFailure: Throwable? = null
        try {
            store.transaction {
                val before = snapshots(owner)
                saveSections()
                check(owner == currentOwner()) { "Your session changed. Reopen the flight." }
                if (reviewing == null) updateOrdinaryReviewSources(owner, before)
                val after = snapshots(owner)
                val changed = (before.keys + after.keys).filter { before[it] != after[it] }
                check(changed.size <= 1) { "Save one flight at a time." }
                for (id in changed) {
                    val old = before[id] ?: FlightOperationsBatchInput()
                    val next = after[id] ?: FlightOperationsBatchInput()
                    val prior = dao.rows(owner).firstOrNull { it.flightId == id }
                    val retained = projectionStatuses(prior?.let { decode(it.inputJson) } ?: FlightOperationsBatchInput(), old)
                    val preserveAttempt = prior?.attemptInputJson != null && !prior.attemptRejected
                    val frozen = prior?.attemptInputJson?.takeIf { preserveAttempt }?.let(::decode)
                    fun <T> merge(previous: List<T>, was: List<T>, now: List<T>, key: (T) -> String): List<T> {
                        val changedKeys = (was.map(key) + now.map(key)).filter { k -> was.firstOrNull { key(it) == k } != now.firstOrNull { key(it) == k } }.toSet()
                        return previous.filterNot { key(it) in changedKeys } + now.filter { key(it) in changedKeys }
                    }
                    var desired = FlightOperationsBatchInput(
                        if (old.arrival != next.arrival) next.arrival else retained.arrival,
                        if (old.departure != next.departure) next.departure else retained.departure,
                        merge(retained.services, old.services, next.services) { it.serviceRecordId } +
                            old.services.filter { removed -> next.services.none { it.serviceRecordId == removed.serviceRecordId } &&
                                frozen?.contains("service", removed.serviceRecordId) == true }.map { it.copy(operation = "delete", inputJson = null, attachmentFilesJson = "{}") },
                        merge(retained.tasks, old.tasks, next.tasks) { it.taskRecordId } +
                            old.tasks.filter { removed -> next.tasks.none { it.taskRecordId == removed.taskRecordId } &&
                                frozen?.contains("task", removed.taskRecordId) == true }.map { it.copy(operation = "delete", inputJson = null, attachmentFilesJson = "{}") },
                    )
                    check(!desired.isEmpty() || !preserveAttempt) { "Retry synchronization before removing a Save whose outcome is unknown." }
                    if (desired.isEmpty()) { dao.delete(owner, id); continue }
                    val versions = desired.versions().map { acknowledgedVersion(id, it) }.distinct()
                    check(versions.size == 1 || reviewing?.flight?.id == id) { "The edited sections were reviewed against different flight versions. Reload and review them before saving together." }
                    val version = versions.singleOrNull() ?: checkNotNull(reviewing).flight.rowVersion
                    val needsComparison = prior?.needsComparison == true || desired.versions().any { it != version } ||
                        desired.arrival?.needsArrivalComparison == true || desired.departure?.needsDepartureComparison == true ||
                        desired.services.any { it.needsServiceComparison } || desired.tasks.any { it.needsTaskComparison }
                    if (versions.size == 1) desired = atVersion(desired, version)
                    // Import/validate every selected file, including the aggregate limits, before the transaction publishes.
                    request(desired)
                    val held = versions.size != 1 || !allReviewed(desired)
                    dao.put(FlightOperationsBatchEntity(owner, id, prior?.changeId ?: UUID.randomUUID().toString(),
                        json.encodeToString(desired), version, status = if (held && !preserveAttempt) "conflict" else "pending",
                        error = if (held) "Review or discard every remaining local section before synchronizing this Save." else null,
                        attemptInputJson = prior?.attemptInputJson?.takeIf { preserveAttempt },
                        attemptRowVersion = prior?.attemptRowVersion?.takeIf { preserveAttempt }, needsComparison = needsComparison))
                    putProjections(desired)
                }
                afterStaging()
            }
        } catch (failure: Throwable) {
            primaryFailure = failure
            throw failure
        } finally {
            deferCleanup(false)
            // Rollback preserves old references; successful commit owns all new references before cleanup.
            try { cleanupSafely() }
            catch (cancelled: CancellationException) { if (primaryFailure == null) throw cancelled }
        }
        runCatching { schedule() }
    }

    private fun allReviewed(input: FlightOperationsBatchInput) = listOfNotNull(input.arrival?.status, input.departure?.status)
        .plus(input.services.map { it.status }).plus(input.tasks.map { it.status }).all { it == "pending" }

    private fun projectionStatuses(input: FlightOperationsBatchInput, current: FlightOperationsBatchInput) = input.copy(
        arrival = input.arrival?.let { row -> current.arrival?.takeIf { it.changeId == row.changeId }?.let { row.copy(status = it.status, error = it.error) } ?: row },
        departure = input.departure?.let { row -> current.departure?.takeIf { it.changeId == row.changeId }?.let { row.copy(status = it.status, error = it.error) } ?: row },
        services = input.services.map { row -> current.services.firstOrNull { it.serviceRecordId == row.serviceRecordId && it.changeId == row.changeId }?.let { row.copy(status = it.status, error = it.error) } ?: row },
        tasks = input.tasks.map { row -> current.tasks.firstOrNull { it.taskRecordId == row.taskRecordId && it.changeId == row.changeId }?.let { row.copy(status = it.status, error = it.error) } ?: row })

    /** Loading and copying Review values never call this: only the explicit editor Save replaces local intent. */
    suspend fun saveReviewed(flight: FlightDetail, target: FlightOperationReviewTarget, saveSections: suspend () -> Unit) {
        withContext(Reviewing(flight, target)) {
            save {
                val owner = checkNotNull(currentOwner()) { "Sign in to review this flight." }
                val input = snapshots(owner)[flight.id] ?: error("This local input is no longer available. Reopen the flight.")
                requireReviewTarget(input, target)
                requireResolved(owner, flight.id, input)
                // Actual queues normally prohibit switching target. This explicit, rollback-safe review is the only exception.
                when (target.kind) {
                    "arrival" -> input.arrival?.let { store.arrivals.delete(owner, flight.id, it.changeId) }
                    "departure" -> input.departure?.let { store.departures.delete(owner, flight.id, it.changeId) }
                }
                saveSections()
                persistReviewSource(owner, flight, target, input)
            }
        }
    }

    private suspend fun persistReviewSource(owner: String, flight: FlightDetail, target: FlightOperationReviewTarget, original: FlightOperationsBatchInput) {
        when (target.kind) {
            "arrival" -> {
                val before = checkNotNull(original.arrival)
                val after = store.arrivals.rows(owner).firstOrNull { it.flightId == flight.id } ?: return
                val saved = json.decodeFromString<SaveFlightArrivalRequest>(after.inputJson)
                val source = if (target.useLocalSource) json.decodeFromString<SaveFlightArrivalRequest>(before.reviewSourceInputJson ?: before.inputJson)
                    else SaveFlightArrivalRequest("", "", "")
                val online = (saved.targetSubmissionId?.let { id -> flight.arrivalSubmissions.firstOrNull { it.id.equals(id, true) } }
                    ?: flight.visibleArrival())?.input()?.copy(targetSubmissionId = saved.targetSubmissionId)
                    ?: SaveFlightArrivalRequest("", "", "", targetSubmissionId = saved.targetSubmissionId)
                val staged = target.stagedInputJson?.let { json.decodeFromString<SaveFlightArrivalRequest>(it) }
                    ?: if (target.useLocalSource) mergeArrivalReview(online, source) else online
                store.arrivals.put(after.copy(reviewSourceInputJson = json.encodeToString(arrivalReviewSourceAfterSave(online, source, saved, staged))))
            }
            "departure" -> {
                val before = checkNotNull(original.departure)
                val after = store.departures.rows(owner).firstOrNull { it.flightId == flight.id } ?: return
                val saved = json.decodeFromString<SaveFlightDepartureRequest>(after.inputJson)
                val source = if (target.useLocalSource) json.decodeFromString<SaveFlightDepartureRequest>(before.reviewSourceInputJson ?: before.inputJson)
                    else SaveFlightDepartureRequest("")
                val recorded = saved.targetSubmissionId?.let { id -> flight.departureSubmissions.firstOrNull { it.id.equals(id, true) } } ?: flight.visibleDeparture()
                val online = SaveFlightDepartureRequest(recorded?.atd.orEmpty(), remarks = recorded?.remarks, targetSubmissionId = saved.targetSubmissionId)
                val staged = target.stagedInputJson?.let { json.decodeFromString<SaveFlightDepartureRequest>(it) }
                    ?: if (target.useLocalSource) mergeDepartureReview(online, source) else online
                val retained = departureReviewSourceAfterSave(online, source, saved, staged).copy(signatureContent = null,
                    signatureSignedAtUtc = if (after.signatureFile == null) null else saved.signatureSignedAtUtc)
                store.departures.put(after.copy(reviewSourceInputJson = json.encodeToString(retained)))
            }
            "service" -> {
                val before = original.services.single { it.serviceRecordId == target.recordId }
                val after = store.services.rows(owner).firstOrNull { it.flightId == flight.id && it.serviceRecordId == target.recordId } ?: return
                if (after.inputJson == null || before.inputJson == null) return
                val source = if (target.useLocalSource) json.decodeFromString<SaveFlightServiceRequest>(before.reviewSourceInputJson ?: before.inputJson)
                    else SaveFlightServiceRequest("", "")
                val saved = json.decodeFromString<SaveFlightServiceRequest>(after.inputJson)
                val online = flight.services.firstOrNull { it.id.equals(target.recordId, true) }?.input() ?: SaveFlightServiceRequest("", "")
                val staged = target.stagedInputJson?.let { json.decodeFromString<SaveFlightServiceRequest>(it) }
                    ?: if (target.useLocalSource) mergeServiceReview(online, source) else online
                store.services.put(after.copy(reviewSourceInputJson = json.encodeToString(serviceReviewSourceAfterSave(online, source, saved, staged))))
            }
            "task" -> {
                val before = original.tasks.single { it.taskRecordId == target.recordId }
                val after = store.tasks.rows(owner).firstOrNull { it.flightId == flight.id && it.taskRecordId == target.recordId } ?: return
                if (after.inputJson == null || before.inputJson == null) return
                val source = if (target.useLocalSource) json.decodeFromString<SaveFlightTaskRequest>(before.reviewSourceInputJson ?: before.inputJson)
                    else SaveFlightTaskRequest(-1, "")
                val saved = json.decodeFromString<SaveFlightTaskRequest>(after.inputJson)
                val online = flight.tasks.firstOrNull { it.id.equals(target.recordId, true) }?.input() ?: SaveFlightTaskRequest(saved.taskType, "")
                val staged = target.stagedInputJson?.let { json.decodeFromString<SaveFlightTaskRequest>(it) }
                    ?: if (target.useLocalSource) mergeTaskReview(online, source) else online
                store.tasks.put(after.copy(reviewSourceInputJson = json.encodeToString(taskReviewSourceAfterSave(online, source, saved, staged))))
            }
        }
    }

    /** An ordinary edit of already-reviewed pending work adds only its new editor deltas to provenance. */
    private suspend fun updateOrdinaryReviewSources(owner: String, before: Map<String, FlightOperationsBatchInput>) {
        for ((_, original) in before) {
            original.arrival?.takeIf { it.reviewSourceInputJson != null }?.let { old ->
                store.arrivals.rows(owner).firstOrNull { it.flightId == old.flightId }?.let { row ->
                    if (row.inputJson == old.inputJson) return@let
                    val staged = json.decodeFromString<SaveFlightArrivalRequest>(old.inputJson)
                    val source = json.decodeFromString<SaveFlightArrivalRequest>(checkNotNull(old.reviewSourceInputJson))
                    val saved = json.decodeFromString<SaveFlightArrivalRequest>(row.inputJson)
                    store.arrivals.put(row.copy(reviewSourceInputJson = json.encodeToString(arrivalReviewSourceAfterSave(staged, source, saved, staged))))
                }
            }
            original.departure?.takeIf { it.reviewSourceInputJson != null }?.let { old ->
                store.departures.rows(owner).firstOrNull { it.flightId == old.flightId }?.let { row ->
                    if (row.inputJson == old.inputJson && row.signatureFile == old.signatureFile) return@let
                    val staged = json.decodeFromString<SaveFlightDepartureRequest>(old.inputJson)
                    val source = json.decodeFromString<SaveFlightDepartureRequest>(checkNotNull(old.reviewSourceInputJson))
                    val saved = json.decodeFromString<SaveFlightDepartureRequest>(row.inputJson)
                    val retained = departureReviewSourceAfterSave(staged, source, saved, staged).copy(signatureContent = null,
                        signatureSignedAtUtc = if (row.signatureFile == null) null else saved.signatureSignedAtUtc)
                    store.departures.put(row.copy(reviewSourceInputJson = json.encodeToString(retained)))
                }
            }
            for (old in original.services.filter { it.reviewSourceInputJson != null && it.inputJson != null }) {
                val row = store.services.rows(owner).firstOrNull { it.flightId == old.flightId && it.serviceRecordId == old.serviceRecordId } ?: continue
                if (row.inputJson == null) continue
                if (row.inputJson == old.inputJson && row.attachmentFilesJson == old.attachmentFilesJson) continue
                val staged = json.decodeFromString<SaveFlightServiceRequest>(checkNotNull(old.inputJson))
                val source = json.decodeFromString<SaveFlightServiceRequest>(checkNotNull(old.reviewSourceInputJson))
                val saved = json.decodeFromString<SaveFlightServiceRequest>(row.inputJson)
                store.services.put(row.copy(reviewSourceInputJson = json.encodeToString(serviceReviewSourceAfterSave(staged, source, saved, staged))))
            }
            for (old in original.tasks.filter { it.reviewSourceInputJson != null && it.inputJson != null }) {
                val row = store.tasks.rows(owner).firstOrNull { it.flightId == old.flightId && it.taskRecordId == old.taskRecordId } ?: continue
                if (row.inputJson == null) continue
                if (row.inputJson == old.inputJson && row.attachmentFilesJson == old.attachmentFilesJson) continue
                val staged = json.decodeFromString<SaveFlightTaskRequest>(checkNotNull(old.inputJson))
                val source = json.decodeFromString<SaveFlightTaskRequest>(checkNotNull(old.reviewSourceInputJson))
                val saved = json.decodeFromString<SaveFlightTaskRequest>(row.inputJson)
                store.tasks.put(row.copy(reviewSourceInputJson = json.encodeToString(taskReviewSourceAfterSave(staged, source, saved, staged))))
            }
        }
    }

    /** Review-card discard is member-scoped; Sync Center's existing discard remains whole-Save scoped. */
    suspend fun discardReviewed(flightId: String, target: FlightOperationReviewTarget) {
        val owner = checkNotNull(currentOwner()) { "Sign in to review this flight." }
        store.transaction {
            val current = snapshots(owner)[flightId] ?: error("This local input is no longer available. Reopen the flight.")
            requireReviewTarget(current, target)
            requireResolved(owner, flightId, current)
            val removed = member(current, target)
            removeProjections(owner, removed)
            dao.rows(owner).firstOrNull { it.flightId == flightId }?.let { group ->
                val retained = withoutMember(projectionStatuses(decode(group.inputJson), current), target)
                if (retained.isEmpty()) dao.delete(owner, flightId)
                else {
                    val versions = retained.versions().distinct()
                    val held = !allReviewed(retained) || versions.size != 1
                    dao.put(group.copy(inputJson = json.encodeToString(retained), rowVersion = versions.singleOrNull() ?: group.rowVersion,
                        status = if (held) "conflict" else "pending", error = if (held) "Review or discard every remaining local section before synchronizing this Save." else null,
                        attemptInputJson = null, attemptRowVersion = null, attemptRejected = false))
                }
            }
        }
        cleanupSafely()
        runCatching { schedule() }
    }

    private fun member(input: FlightOperationsBatchInput, target: FlightOperationReviewTarget) = when (target.kind) {
        "arrival" -> FlightOperationsBatchInput(arrival = input.arrival)
        "departure" -> FlightOperationsBatchInput(departure = input.departure)
        "service" -> FlightOperationsBatchInput(services = input.services.filter { it.serviceRecordId == target.recordId })
        "task" -> FlightOperationsBatchInput(tasks = input.tasks.filter { it.taskRecordId == target.recordId })
        else -> error("Review section is invalid.")
    }
    private fun withoutMember(input: FlightOperationsBatchInput, target: FlightOperationReviewTarget) = input.copy(
        arrival = if (target.kind == "arrival") null else input.arrival, departure = if (target.kind == "departure") null else input.departure,
        services = input.services.filterNot { target.kind == "service" && it.serviceRecordId == target.recordId },
        tasks = input.tasks.filterNot { target.kind == "task" && it.taskRecordId == target.recordId })
    private fun requireReviewTarget(input: FlightOperationsBatchInput, target: FlightOperationReviewTarget) {
        val selected = member(input, target)
        val changes = listOfNotNull(selected.arrival?.changeId, selected.departure?.changeId) + selected.services.map { it.changeId } + selected.tasks.map { it.changeId }
        check(changes.singleOrNull() == target.changeId) { "This saved input changed. Reopen Review before continuing." }
    }
    private suspend fun requireResolved(owner: String, id: String, input: FlightOperationsBatchInput) {
        val group = dao.rows(owner).firstOrNull { it.flightId == id }
        check(group?.attemptInputJson == null || group.attemptRejected) { "This Save may already be recorded online. Open Sync Center and retry synchronization before resolving it." }
        check(!legacyPending(owner, id, input)) { "An earlier request has an unknown outcome. Open Sync Center and retry it before resolving this input." }
    }

    suspend fun contains(kind: String, flightId: String, recordId: String? = null): Boolean {
        val owner = currentOwner() ?: return false
        return dao.rows(owner).firstOrNull { it.flightId == flightId }?.let {
            decode(it.inputJson).contains(kind, recordId) || it.attemptInputJson?.let { raw -> decode(raw).contains(kind, recordId) } == true
        } == true
    }

    /** A frozen whole Save also holds unrelated upgrade-era fresh rows until its outcome is known. */
    suspend fun blocksFreshDrain(flightId: String): Boolean {
        val owner = currentOwner() ?: return false
        return dao.rows(owner).any { it.flightId == flightId }
    }

    /** DB10 had no Save-group identity. Conservatively group its unsent intent by flight before any fresh drain. */
    suspend fun adoptLegacyPending() {
        val owner = currentOwner() ?: return
        for ((id, current) in snapshots(owner)) {
            store.transaction {
                val prior = dao.rows(owner).firstOrNull { it.flightId == id }
                // Never change a frozen group's membership/version. Flight-wide blocking holds fresh siblings.
                if (prior?.attemptInputJson != null) return@transaction
                val retained = prior?.let { decode(it.inputJson) } ?: FlightOperationsBatchInput()
                val extra = FlightOperationsBatchInput(
                    current.arrival?.takeIf { it.attemptInputJson == null && !retained.contains("arrival") },
                    current.departure?.takeIf { it.attemptInputJson == null && !retained.contains("departure") },
                    current.services.filter { it.attemptOperation == null && !retained.contains("service", it.serviceRecordId) },
                    current.tasks.filter { it.attemptOperation == null && !retained.contains("task", it.taskRecordId) })
                if (extra.isEmpty()) return@transaction
                var desired = retained.copy(arrival = retained.arrival ?: extra.arrival, departure = retained.departure ?: extra.departure,
                    services = retained.services + extra.services, tasks = retained.tasks + extra.tasks)
                var state = prior?.status?.takeIf { it != "pending" } ?: listOfNotNull(extra.arrival?.status, extra.departure?.status)
                    .plus(extra.services.map { it.status }).plus(extra.tasks.map { it.status }).firstOrNull { it != "pending" } ?: "pending"
                var message = prior?.error ?: if (state != "pending") "Reload and review every saved section before synchronizing this flight." else null
                val versions = desired.versions().map { acknowledgedVersion(id, it) }.distinct()
                val version = versions.singleOrNull() ?: prior?.rowVersion ?: desired.versions().first()
                var compare = prior?.needsComparison == true
                if (versions.size != 1) {
                    state = "conflict"
                    message = "The saved sections were reviewed against different flight versions. Reload and review them before saving together."
                } else {
                    compare = compare || desired.versions().any { it != version } || desired.arrival?.needsArrivalComparison == true ||
                        desired.departure?.needsDepartureComparison == true || desired.services.any { it.needsServiceComparison } || desired.tasks.any { it.needsTaskComparison }
                    desired = atVersion(desired, version)
                }
                try { request(desired) }
                catch (cancelled: CancellationException) { throw cancelled }
                catch (failure: Exception) { state = "failed"; message = failure.userMessage() }
                if (state != "pending" && state != "waiting-creation") desired = status(desired, state, message)
                check(owner == currentOwner()) { "Your session changed. Reopen the flight." }
                dao.put(FlightOperationsBatchEntity(owner, id, prior?.changeId ?: UUID.randomUUID().toString(),
                    json.encodeToString(desired), version, status = state, error = message, needsComparison = compare))
                // Only adopted unsent projections are changed; legacy frozen rows remain eligible for exact replay.
                if (state != "waiting-creation") putAdoptedProjections(desired, state, message)
            }
        }
    }

    suspend fun fileReferences(kind: String): Set<String> = dao.allRows().flatMap { row ->
        listOfNotNull(row.inputJson, row.attemptInputJson).flatMap { raw ->
            val input = decode(raw)
            fun files(raw: String?) = raw?.let { json.decodeFromString<Map<String, String>>(it).values.toList() }.orEmpty()
            when (kind) {
                "service" -> input.services.flatMap { files(it.attachmentFilesJson) + files(it.attemptAttachmentFilesJson) }
                "task" -> input.tasks.flatMap { files(it.attachmentFilesJson) + files(it.attemptAttachmentFilesJson) }
                "departure" -> listOfNotNull(input.departure?.signatureFile, input.departure?.attemptSignatureFile)
                else -> emptyList()
            }
        }
    }.toSet()

    private suspend fun request(input: FlightOperationsBatchInput): SaveFlightOperationsBatchRequest {
        val value = SaveFlightOperationsBatchRequest(input.arrival?.let(hydrateArrival), input.departure?.let { hydrateDeparture(it) },
            input.services.map { FlightServiceBatchChange(it.serviceRecordId, hydrateService(it)) },
            input.tasks.map { FlightTaskBatchChange(it.taskRecordId, hydrateTask(it)) })
        val content = value.serviceChanges.flatMap { it.input?.attachments.orEmpty() }.mapNotNull { it.content } +
            value.taskChanges.flatMap { it.input?.attachments.orEmpty() }.mapNotNull { it.content } + listOfNotNull(value.departure?.signatureContent)
        val bytes = content.sumOf { Base64.getDecoder().decode(it).size.toLong() }
        check(bytes <= FlightOperationsBatchLimits.MAX_INLINE_BYTES) { "Files and signature in one Save must total at most 100 MB." }
        check(json.encodeToString(value).toByteArray(Charsets.UTF_8).size.toLong() <= FlightOperationsBatchLimits.MAX_JSON_BYTES) {
            "This flight Save is too large. Reduce the selected files before saving."
        }
        return value
    }

    /** Only exact acknowledgments of our legacy requests may advance a waiting new batch. */
    suspend fun onPriorWriteAcknowledged(id: String, before: String, after: String) {
        val owner = currentOwner() ?: return
        dao.rows(owner).firstOrNull { it.flightId == id && it.attemptInputJson == null && it.rowVersion == before }?.let {
            val input = decode(it.inputJson)
            val advanced = input.copy(arrival = input.arrival?.let { row -> if (row.rowVersion == before) row.copy(rowVersion = after) else row },
                departure = input.departure?.let { row -> if (row.rowVersion == before) row.copy(rowVersion = after) else row },
                services = input.services.map { row -> if (row.rowVersion == before) row.copy(rowVersion = after) else row },
                tasks = input.tasks.map { row -> if (row.rowVersion == before) row.copy(rowVersion = after) else row })
            dao.put(it.copy(rowVersion = after, inputJson = json.encodeToString(advanced), needsComparison = true))
        }
    }
    /** Only local, never-transmitted work can depend on an unmaterialized/planning flight. */
    suspend fun holdForPlanning(id: String): String? {
        val owner = checkNotNull(currentOwner())
        return dao.rows(owner).firstOrNull { it.flightId == id }?.let { row ->
            check(row.attemptInputJson == null && !legacyPending(owner, id, decode(row.inputJson))) {
                "Synchronize the previous flight Save before changing Flight details."
            }
            dao.put(row.copy(status = "waiting-creation"))
            row.rowVersion
        }
    }

    /** An exact planning ACK may advance only the dependent requests that have never been sent. */
    suspend fun releaseAfterPlanning(id: String, before: String, after: String) {
        val owner = checkNotNull(currentOwner())
        dao.rows(owner).firstOrNull { it.flightId == id }?.let { row ->
            check(row.attemptInputJson == null && row.rowVersion == before &&
                !legacyPending(owner, id, decode(row.inputJson))) { "Review the saved flight changes before synchronizing." }
            val advanced = atVersion(decode(row.inputJson), after)
            putProjections(advanced)
            dao.put(row.copy(inputJson = json.encodeToString(advanced), rowVersion = after,
                status = if (row.status == "waiting-creation") "pending" else row.status, needsComparison = true))
        }
    }

    /** A recovered creation receipt does not prove that its current planning/work is still the reviewed empty flight. */
    suspend fun holdPlanningWorkForReview(id: String, message: String) {
        val owner = checkNotNull(currentOwner())
        dao.rows(owner).firstOrNull { it.flightId == id }?.let { row ->
            val input = decode(row.inputJson)
            check(row.attemptInputJson == null && !legacyPending(owner, id, input)) { "A transmitted flight Save cannot be retargeted." }
            val retained = status(input, "conflict", message)
            putProjections(retained)
            dao.put(row.copy(inputJson = json.encodeToString(retained), status = "conflict", error = message, needsComparison = true))
        }
    }

    /** Used inside the same planning transaction; normal outer cleanup retains rollback ownership. */
    suspend fun removeUnsentPlanningWork(id: String) {
        val owner = checkNotNull(currentOwner())
        dao.rows(owner).firstOrNull { it.flightId == id }?.let { row ->
            val input = decode(row.inputJson)
            check(row.attemptInputJson == null && !legacyPending(owner, id, input)) {
                "This Save may already be recorded online. Retry synchronization before discarding it."
            }
            removeProjections(owner, input)
            dao.delete(owner, id)
        }
        store.services.drafts(owner).firstOrNull { it.flightId == id }?.let { store.services.deleteDraft(owner, id) }
    }

    private fun atVersion(input: FlightOperationsBatchInput, version: String) = input.copy(
        arrival = input.arrival?.copy(rowVersion = version), departure = input.departure?.copy(rowVersion = version),
        services = input.services.map { it.copy(rowVersion = version) }, tasks = input.tasks.map { it.copy(rowVersion = version) })

    suspend fun legacyArrivalAccepted(id: String, saved: FlightArrivalSaved) = updateDesired(id) { input ->
        input.copy(arrival = input.arrival?.copy(reviewedArrivalJson = json.encodeToString(saved.submission), attemptInputJson = null))
    }
    suspend fun legacyDepartureAccepted(id: String, saved: FlightDepartureSaved) = updateDesired(id) { input ->
        input.copy(departure = input.departure?.let { row ->
            val sameSignature = row.signatureFile == row.attemptSignatureFile
            row.copy(reviewedDepartureJson = json.encodeToString(listOf(saved.submission)),
                inputJson = if (sameSignature) json.encodeToString(json.decodeFromString<SaveFlightDepartureRequest>(row.inputJson).copy(signatureSignedAtUtc = null)) else row.inputJson,
                signatureFile = if (sameSignature) null else row.signatureFile,
                attemptInputJson = null, attemptRowVersion = null, attemptSignatureFile = null)
        })
    }
    suspend fun legacyServiceAccepted(id: String, saved: FlightServiceSaved) = updateDesired(id) { input ->
        input.copy(services = input.services.map { if (it.serviceRecordId == saved.service.id) it.copy(
            reviewedServiceJson = json.encodeToString(saved.service), attemptOperation = null, attemptInputJson = null,
            attachmentFilesJson = json.encodeToString(json.decodeFromString<Map<String, String>>(it.attachmentFilesJson).filterKeys { key -> saved.service.attachments.none { file -> file.id == key } }),
            attemptRowVersion = null, attemptAttachmentFilesJson = null) else it })
    }
    suspend fun legacyTaskAccepted(id: String, saved: FlightTaskSaved) = updateDesired(id) { input ->
        input.copy(tasks = input.tasks.map { if (it.taskRecordId == saved.task.id) it.copy(
            reviewedTaskJson = json.encodeToString(saved.task), attemptOperation = null, attemptInputJson = null,
            attachmentFilesJson = json.encodeToString(json.decodeFromString<Map<String, String>>(it.attachmentFilesJson).filterKeys { key -> saved.task.attachments.none { file -> file.id == key } }),
            attemptRowVersion = null, attemptAttachmentFilesJson = null) else it })
    }
    suspend fun legacyServiceDeleted(id: String, recordId: String) = updateDesired(id) { input ->
        input.copy(services = input.services.map { if (it.serviceRecordId == recordId) it.copy(reviewedServiceJson = null,
            attemptOperation = null, attemptInputJson = null, attemptRowVersion = null, attemptAttachmentFilesJson = null) else it })
    }
    suspend fun legacyTaskDeleted(id: String, recordId: String) = updateDesired(id) { input ->
        input.copy(tasks = input.tasks.map { if (it.taskRecordId == recordId) it.copy(reviewedTaskJson = null,
            attemptOperation = null, attemptInputJson = null, attemptRowVersion = null, attemptAttachmentFilesJson = null) else it })
    }
    /** Called only after an actual legacy HTTP attempt receives a definitive rejection, before any ACK. */
    suspend fun legacyRejected(kind: String, id: String, recordId: String? = null) {
        val owner = currentOwner() ?: return
        fun arrival(row: FlightArrivalPendingEntity) = row.copy(attemptInputJson = null)
        fun departure(row: FlightDeparturePendingEntity) = row.copy(attemptInputJson = null, attemptRowVersion = null, attemptSignatureFile = null)
        fun service(row: FlightServicePendingEntity) = row.copy(attemptOperation = null, attemptInputJson = null, attemptRowVersion = null, attemptAttachmentFilesJson = null)
        fun task(row: FlightTaskPendingEntity) = row.copy(attemptOperation = null, attemptInputJson = null, attemptRowVersion = null, attemptAttachmentFilesJson = null)
        store.transaction {
            dao.rows(owner).firstOrNull { it.flightId == id && it.attemptInputJson == null }?.let { group ->
                val input = decode(group.inputJson)
                val cleared = input.copy(arrival = input.arrival?.let { if (kind == "arrival") arrival(it) else it },
                    departure = input.departure?.let { if (kind == "departure") departure(it) else it },
                    services = input.services.map { if (kind == "service" && it.serviceRecordId == recordId) service(it) else it },
                    tasks = input.tasks.map { if (kind == "task" && it.taskRecordId == recordId) task(it) else it })
                dao.put(group.copy(inputJson = json.encodeToString(cleared)))
            }
            when (kind) {
                "arrival" -> store.arrivals.rows(owner).firstOrNull { it.flightId == id }?.let { store.arrivals.put(arrival(it)) }
                "departure" -> store.departures.rows(owner).firstOrNull { it.flightId == id }?.let { store.departures.put(departure(it)) }
                "service" -> store.services.rows(owner).firstOrNull { it.flightId == id && it.serviceRecordId == recordId }?.let { store.services.put(service(it)) }
                "task" -> store.tasks.rows(owner).firstOrNull { it.flightId == id && it.taskRecordId == recordId }?.let { store.tasks.put(task(it)) }
            }
        }
    }
    private suspend fun updateDesired(id: String, update: (FlightOperationsBatchInput) -> FlightOperationsBatchInput) {
        val owner = currentOwner() ?: return
        dao.rows(owner).firstOrNull { it.flightId == id && it.attemptInputJson == null }?.let {
            dao.put(it.copy(inputJson = json.encodeToString(update(decode(it.inputJson)))))
        }
    }

    private suspend fun legacyPending(owner: String, id: String, input: FlightOperationsBatchInput): Boolean {
        val current = snapshots(owner)[id] ?: return false
        return current.arrival?.attemptInputJson != null || current.departure?.attemptInputJson != null ||
            current.services.any { it.attemptOperation != null } || current.tasks.any { it.attemptOperation != null }
    }

    private fun verifyReviewed(input: FlightOperationsBatchInput, live: FlightDetail) {
        input.arrival?.let { row ->
            val request = hydrateArrival(row)
            val current = if (request.targetSubmissionId != null) live.arrivalSubmissions.firstOrNull { it.id == request.targetSubmissionId }
                else live.arrivalSubmissions.firstOrNull { it.submittedByEmployee.staffMemberId.equals(live.currentStaffMemberId, true) }
                    ?: live.arrivalSubmissions.singleOrNull()
            val reviewed = row.reviewedArrivalJson?.let { json.decodeFromString<FlightArrivalSubmission?>(it) }
            check(current?.input()?.normalized() == reviewed?.input()?.normalized()) { "Arrival changed. Reload and review the flight before saving again." }
        }
        input.departure?.let { row ->
            val request = json.decodeFromString<SaveFlightDepartureRequest>(row.inputJson)
            val current = if (request.targetSubmissionId != null) live.departureSubmissions.firstOrNull { it.id == request.targetSubmissionId }
                else live.departureSubmissions.firstOrNull { it.submittedByEmployee.staffMemberId.equals(live.currentStaffMemberId, true) }
                    ?: live.departureSubmissions.singleOrNull()
            val reviewed = json.decodeFromString<List<FlightDepartureSubmission>>(row.reviewedDepartureJson).firstOrNull {
                if (request.targetSubmissionId != null) it.id == request.targetSubmissionId else it.submittedByEmployee.staffMemberId.equals(row.staffMemberId, true)
            }
            check(current == reviewed) { "Departure changed. Reload and review the flight before saving again." }
        }
        input.services.forEach { row ->
            val reviewed = row.reviewedServiceJson?.let { json.decodeFromString<FlightServiceRecord>(it) }
            check(live.services.firstOrNull { it.id == row.serviceRecordId }?.input() == reviewed?.input()) { "A service changed. Reload and review the flight before saving again." }
        }
        input.tasks.forEach { row ->
            val reviewed = row.reviewedTaskJson?.let { json.decodeFromString<FlightTaskRecord>(it) }
            check(live.tasks.firstOrNull { it.id == row.taskRecordId }?.input() == reviewed?.input()) { "A task changed. Reload and review the flight before saving again." }
        }
    }

    suspend fun syncPending(): Boolean {
        val owner = currentOwner() ?: return false
        if (!online()) return dao.rows(owner).any { it.status == "pending" }
        var retry = false
        for (snapshot in dao.rows(owner).filter { it.status == "pending" }) {
            if (owner != currentOwner()) return false
            if (!mayPublishFlight(snapshot.flightId)) {
                check(snapshot.attemptInputJson == null) { "A transmitted flight Save cannot wait for creation." }
                dao.put(snapshot.copy(status = "waiting-creation"))
                continue
            }
            var acknowledged = false
            try {
                val desired = decode(snapshot.inputJson)
                if (snapshot.attemptInputJson == null && legacyPending(owner, snapshot.flightId, desired)) { retry = true; continue }
                if (snapshot.attemptInputJson == null && !allReviewed(desired)) {
                    dao.put(snapshot.copy(status = "conflict", error = "Review or discard every remaining local section before synchronizing this Save."))
                    continue
                }
                if (snapshot.attemptInputJson == null && desired.versions().distinct() != listOf(snapshot.rowVersion))
                    throw ApiException(409, "The saved sections were reviewed against different flight versions. Reload and review them before saving together.")
                var preparedRequest: SaveFlightOperationsBatchRequest? = null
                val row = if (snapshot.attemptInputJson != null) snapshot.copy(attemptRejected = false).also { dao.put(it) } else {
                    if (snapshot.needsComparison) {
                        val live = fetch(snapshot.flightId)
                        if (live.fromCache) throw IOException("Connect to reload the latest flight before synchronizing.")
                        verifyReviewed(desired, live)
                    }
                    // A first-send preparation failure is definitely local. Do not create unknown-outcome
                    // proof until every byte is available, then send these exact prepared bytes.
                    preparedRequest = request(desired)
                    snapshot.copy(attemptInputJson = snapshot.inputJson, attemptRowVersion = snapshot.rowVersion, attemptRejected = false).also { dao.put(it) }
                }
                val attempt = decode(checkNotNull(row.attemptInputJson))
                val result = send(row.flightId, preparedRequest ?: request(attempt), checkNotNull(row.attemptRowVersion))
                acknowledged = true
                if (owner != currentOwner()) return false
                accepted(result, attempt)
                onAcknowledged(row.flightId, checkNotNull(row.attemptRowVersion), result.rowVersion)
                store.transaction {
                    val latest = dao.rows(owner).firstOrNull { it.flightId == row.flightId && it.changeId == row.changeId } ?: return@transaction
                    if (sameDesired(decode(latest.inputJson), attempt)) {
                        removeProjections(owner, attempt)
                        dao.delete(owner, row.flightId)
                    } else {
                        val advanced = advance(decode(latest.inputJson), attempt, result)
                        putProjections(advanced)
                        val held = !allReviewed(advanced)
                        dao.put(latest.copy(inputJson = json.encodeToString(advanced), rowVersion = result.rowVersion,
                            attemptInputJson = null, attemptRowVersion = null, status = if (held) "conflict" else "pending",
                            error = if (held) "Review or discard every remaining local section before synchronizing this Save." else null,
                            needsComparison = advanced.arrival?.needsArrivalComparison == true || advanced.departure?.needsDepartureComparison == true ||
                                advanced.services.any { it.needsServiceComparison } || advanced.tasks.any { it.needsTaskComparison }, attemptRejected = false))
                    }
                }
                cleanupSafely()
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (error: Exception) {
                if (owner != currentOwner()) return false
                val transient = acknowledged || error is IOException || error is HttpRequestTimeoutException || error is SerializationException ||
                    error is ApiException && (error.statusCode >= 500 || error.statusCode in listOf(401, 408, 429))
                val status = if (transient) "pending" else if (error is ApiException && error.statusCode in listOf(409, 412)) "conflict" else "failed"
                val rejected = !acknowledged && error is ApiException && error.statusCode in 400..499 && error.statusCode !in listOf(401, 408, 429)
                val message = if (transient) "Saved on this device. Waiting to sync; reconnect or sign in again." else error.userMessage()
                store.transaction {
                    dao.rows(owner).firstOrNull { it.flightId == snapshot.flightId && it.changeId == snapshot.changeId }?.let {
                        val retained = status(decode(it.inputJson), status, message)
                        dao.put(it.copy(inputJson = json.encodeToString(retained), status = status, error = message, attemptRejected = it.attemptRejected || rejected))
                        putProjections(retained)
                    }
                }
                retry = retry || transient
            }
        }
        return retry || dao.rows(owner).any { it.status == "pending" }
    }

    private fun status(input: FlightOperationsBatchInput, status: String, error: String?) = input.copy(
        arrival = input.arrival?.copy(status = status, error = error), departure = input.departure?.copy(status = status, error = error),
        services = input.services.map { it.copy(status = status, error = error) }, tasks = input.tasks.map { it.copy(status = status, error = error) })

    private fun sameDesired(left: FlightOperationsBatchInput, right: FlightOperationsBatchInput): Boolean {
        fun files(raw: String) = json.decodeFromString<Map<String, String>>(raw)
        fun services(input: FlightOperationsBatchInput) = input.services.sortedBy { it.serviceRecordId }.map { listOf(it.serviceRecordId, it.operation, it.inputJson, files(it.attachmentFilesJson)) }
        fun tasks(input: FlightOperationsBatchInput) = input.tasks.sortedBy { it.taskRecordId }.map { listOf(it.taskRecordId, it.operation, it.inputJson, files(it.attachmentFilesJson)) }
        return left.arrival?.inputJson == right.arrival?.inputJson && left.departure?.inputJson == right.departure?.inputJson &&
            left.departure?.signatureFile == right.departure?.signatureFile && services(left) == services(right) && tasks(left) == tasks(right)
    }

    private suspend fun putProjections(input: FlightOperationsBatchInput) {
        input.arrival?.let { store.arrivals.put(it) }; input.departure?.let { store.departures.put(it) }
        input.services.forEach { store.services.put(it) }; input.tasks.forEach { store.tasks.put(it) }
    }
    private suspend fun putAdoptedProjections(input: FlightOperationsBatchInput, state: String, message: String?) {
        val owner = checkNotNull(currentOwner())
        input.arrival?.let { row ->
            if (store.arrivals.rows(owner).firstOrNull { it.flightId == row.flightId }?.attemptInputJson == null)
                store.arrivals.put(row.copy(status = state, error = message))
        }
        input.departure?.let { row ->
            if (store.departures.rows(owner).firstOrNull { it.flightId == row.flightId }?.attemptInputJson == null)
                store.departures.put(row.copy(status = state, error = message))
        }
        val services = store.services.rows(owner).associateBy { it.flightId to it.serviceRecordId }
        input.services.forEach { if (services[it.flightId to it.serviceRecordId]?.attemptOperation == null) store.services.put(it.copy(status = state, error = message)) }
        val tasks = store.tasks.rows(owner).associateBy { it.flightId to it.taskRecordId }
        input.tasks.forEach { if (tasks[it.flightId to it.taskRecordId]?.attemptOperation == null) store.tasks.put(it.copy(status = state, error = message)) }
    }
    private suspend fun removeProjections(owner: String, input: FlightOperationsBatchInput) {
        input.arrival?.let { store.arrivals.delete(owner, it.flightId, it.changeId) }
        input.departure?.let { store.departures.delete(owner, it.flightId, it.changeId) }
        input.services.forEach { store.services.delete(owner, it.flightId, it.serviceRecordId, it.changeId) }
        input.tasks.forEach { store.tasks.delete(owner, it.flightId, it.taskRecordId, it.changeId) }
    }

    private fun advance(input: FlightOperationsBatchInput, attempt: FlightOperationsBatchInput, result: FlightDetail): FlightOperationsBatchInput {
        fun files(raw: String) = json.decodeFromString<Map<String, String>>(raw)
        return input.copy(
            arrival = input.arrival?.let { row -> if (attempt.arrival == null) row.copy(rowVersion = result.rowVersion, needsArrivalComparison = true)
            else row.copy(rowVersion = result.rowVersion, reviewedArrivalJson = json.encodeToString(result.arrivalSubmissions.firstOrNull {
                val target = hydrateArrival(checkNotNull(input.arrival)).targetSubmissionId
                if (target != null) it.id == target else it.submittedByEmployee.staffMemberId.equals(result.currentStaffMemberId, true)
            }), attemptInputJson = null, needsArrivalComparison = false, status = "pending", error = null) },
            departure = input.departure?.let { row ->
                if (attempt.departure == null) return@let row.copy(rowVersion = result.rowVersion, needsDepartureComparison = true)
                val sameFile = row.signatureFile == attempt.departure.signatureFile
                row.copy(rowVersion = result.rowVersion, reviewedDepartureJson = json.encodeToString(result.departureSubmissions),
                    inputJson = if (sameFile) json.encodeToString(json.decodeFromString<SaveFlightDepartureRequest>(row.inputJson).copy(signatureSignedAtUtc = null)) else row.inputJson,
                    signatureFile = if (sameFile) null else row.signatureFile, attemptInputJson = null, attemptRowVersion = null, attemptSignatureFile = null, needsDepartureComparison = false, status = "pending", error = null)
            },
            services = input.services.map { row -> if (!attempt.contains("service", row.serviceRecordId)) row.copy(rowVersion = result.rowVersion, needsServiceComparison = true)
            else row.copy(rowVersion = result.rowVersion,
                reviewedServiceJson = result.services.firstOrNull { it.id == row.serviceRecordId }?.let { json.encodeToString(it) },
                attachmentFilesJson = json.encodeToString(files(row.attachmentFilesJson).filterKeys { key -> result.services.firstOrNull { it.id == row.serviceRecordId }?.attachments?.none { it.id == key } != false }),
                attemptOperation = null, attemptInputJson = null, attemptRowVersion = null, attemptAttachmentFilesJson = null, needsServiceComparison = false, status = "pending", error = null) },
            tasks = input.tasks.map { row -> if (!attempt.contains("task", row.taskRecordId)) row.copy(rowVersion = result.rowVersion, needsTaskComparison = true)
            else row.copy(rowVersion = result.rowVersion,
                reviewedTaskJson = result.tasks.firstOrNull { it.id == row.taskRecordId }?.let { json.encodeToString(it) },
                attachmentFilesJson = json.encodeToString(files(row.attachmentFilesJson).filterKeys { key -> result.tasks.firstOrNull { it.id == row.taskRecordId }?.attachments?.none { it.id == key } != false }),
                attemptOperation = null, attemptInputJson = null, attemptRowVersion = null, attemptAttachmentFilesJson = null, needsTaskComparison = false, status = "pending", error = null) })
    }

    suspend fun retry(kind: String, id: String, recordId: String? = null): Boolean {
        val owner = currentOwner() ?: return false
        val row = dao.rows(owner).firstOrNull { it.flightId == id && (decode(it.inputJson).contains(kind, recordId) || it.attemptInputJson?.let { raw -> decode(raw).contains(kind, recordId) } == true) } ?: return false
        if ((row.attemptInputJson == null || row.attemptRejected) && !allReviewed(decode(row.inputJson))) {
            dao.put(row.copy(status = "conflict", error = "Review or discard every remaining local section before synchronizing this Save.",
                attemptInputJson = null, attemptRowVersion = null, attemptRejected = false))
            return true
        }
        store.transaction { dao.put(row.copy(status = "pending", error = null, attemptRejected = false)); putProjections(status(decode(row.inputJson), "pending", null)) }
        runCatching { schedule() }; return true
    }
    suspend fun discard(kind: String, id: String, recordId: String? = null): Boolean {
        val owner = currentOwner() ?: return false
        val row = dao.rows(owner).firstOrNull { it.flightId == id && (decode(it.inputJson).contains(kind, recordId) || it.attemptInputJson?.let { raw -> decode(raw).contains(kind, recordId) } == true) } ?: return false
        check(row.attemptInputJson == null || row.attemptRejected) { "This Save may already be recorded online. Retry synchronization before discarding it." }
        val input = decode(row.inputJson)
        check(!legacyPending(owner, id, input)) { "This Save includes an earlier request with an unknown outcome. Retry synchronization before discarding it." }
        store.transaction {
            removeProjections(owner, input)
            dao.delete(owner, id)
        }
        cleanupSafely(); return true
    }
    private suspend fun cleanupSafely() {
        try { cleanup() }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (_: Exception) { /* Optional garbage collection cannot undo a durable Save or hide its validation error. */ }
    }
    suspend fun clear() { dao.clear() }
}
