package com.nags.operations.data.repo

import com.nags.operations.data.*
import com.nags.operations.data.api.HttpClientFactory
import java.io.File
import java.io.IOException
import java.security.MessageDigest
import java.time.Instant
import java.util.Locale
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import kotlinx.serialization.Serializable
import kotlinx.serialization.SerializationException
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

/** Immutable post discovery. These snapshots never assign employees or authorize operational writes. */
class SharedFlightsRepository internal constructor(
    private val store: SharedFlightsStore,
    private val currentOwner: suspend () -> String?,
    private val currentStation: suspend () -> String?,
    private val online: () -> Boolean,
    private val fetchPage: suspend (FlightShareDirection, String, Int, Int) -> FlightSharePage,
    private val publishPost: suspend (String, String, PublishFlightShareRequest) -> FlightSharePost,
    private val beginScopeRead: suspend () -> FlightExternalScopeRead? = { null },
    private val confirmScopeRead: suspend (FlightPage, FlightExternalScopeRead?) -> Unit = { _, _ -> },
    private val cachedFlight: suspend (String) -> FlightDetail? = { null },
    private val now: () -> String = { Instant.now().toString() },
    private val json: Json = HttpClientFactory.json,
) {
    private val mutation = Mutex()
    private val sync = Mutex()
    private var generation = 0L
    private val epochs = mutableMapOf<String, Long>()
    private val stages = mutableMapOf<String, Stage>()
    private val evidence = mutableMapOf<String, Evidence>()
    private var evidenceRevision = 0L
    private var readSequence = 0L
    private val lastSavedSequences = mutableMapOf<String, Long>()
    private val events = MutableSharedFlow<Unit>(extraBufferCapacity = 1)
    val changes: Flow<Unit> = events

    suspend fun page(direction: FlightShareDirection = FlightShareDirection.All, search: String = "", page: Int = 1, pageSize: Int = 20): FlightSharePage =
        withContext(Dispatchers.IO) {
            require(page > 0 && pageSize in 1..100) { "Invalid share page." }
            val term = search.trim()
            if (!online()) return@withContext cachedPage(direction, term, page, pageSize)
                ?: throw IOException("Connect and synchronize Shared Flights before reading them offline.")
            val read = begin(direction, term, page == 1)
            try {
                val external = beginScopeRead()
                val result = fetchPage(direction, term, page, pageSize)
                val scope = validate(result, page, pageSize)
                ensureCurrent(read)
                confirmScopeRead(result.flightPage(), external)
                acceptStation(read, scope.stationId)
                acceptFreshEvidence(read, result.items)
                if (term.isEmpty()) stage(read, result, scope)
                ensureCurrent(read)
                val rows = visible(read.owner, result.items)
                ensureCurrent(read)
                result.copy(items = rows, ownerSessionKey = sessionKey(read.owner, read.generation))
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (error: Exception) {
                accessFailure(read, error)
                if (!error.isShareTransient()) throw error
                ensureCurrent(read)
                cachedPage(direction, term, page, pageSize) ?: throw error
            }
        }

    /** One complete combined manifest; flight/media preparation remains in the existing full sync. */
    suspend fun refreshAll() = withContext(Dispatchers.IO) {
        sync.withLock {
            val direction = FlightShareDirection.All
            val read = begin(direction, "", true, channel = "sync")
            try {
                val posts = mutableListOf<FlightSharePost>()
                var first: FlightSharePage? = null
                var number = 1
                do {
                    val external = beginScopeRead()
                    val page = fetchPage(direction, "", number, 100)
                    val scope = validate(page, number, 100)
                    ensureCurrent(read)
                    confirmScopeRead(page.flightPage(), external)
                    acceptStation(read, scope.stationId)
                    if (first == null) first = page
                    else if (first.stationId != page.stationId || first.totalCount != page.totalCount)
                        throw IOException("Shared Flights changed while synchronizing. Retry.")
                    acceptFreshEvidence(read, page.items)
                    posts += page.items
                    if (posts.map { it.id }.distinct().size != posts.size)
                        throw IOException("Shared Flights changed while synchronizing. Retry.")
                    number++
                } while (page.hasNextPage)
                val header = checkNotNull(first)
                if (posts.size.toLong() != header.totalCount) throw IOException("Shared Flights synchronization is incomplete.")
                save(read, Snapshot(scope = header.flightPage().scopeOrNull()!!, items = posts))
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (error: Exception) { accessFailure(read, error); throw error }
        }
    }

    suspend fun cachedPage(direction: FlightShareDirection = FlightShareDirection.All, search: String = "", page: Int = 1, pageSize: Int = 20): FlightSharePage? =
        withContext(Dispatchers.IO) {
            require(page > 0 && pageSize in 1..100)
            val owner = currentOwner() ?: return@withContext null
            val observed = mutation.withLock { generation }
            val station = currentStation() ?: return@withContext null
            val saved = mutation.withLock { snapshotFor(owner, direction) } ?: return@withContext null
            if (saved.scope.stationId != station) return@withContext null
            val term = search.trim().lowercase(Locale.ROOT)
            val rows = visible(owner, saved.items).filter { term.isEmpty() || it.flight.shareMatches(term) }
            if (owner != currentOwner() || observed != mutation.withLock { generation } || station != currentStation())
                return@withContext null
            val totalPages = if (rows.isEmpty()) 0 else (rows.size - 1) / pageSize + 1
            val offset = ((page.toLong() - 1) * pageSize).coerceAtMost(rows.size.toLong()).toInt()
            FlightSharePage(rows.drop(offset).take(pageSize), page, pageSize, rows.size.toLong(), totalPages,
                page > 1, page < totalPages, station, saved.scope.asOfUtc, saved.scope.windowStartUtc,
                saved.scope.windowEndUtc, fromCache = true, ownerSessionKey = sessionKey(owner, observed))
        }

    suspend fun publish(flight: FlightDetail, shareId: String, message: String?, staffMemberIds: List<String>): FlightSharePost =
        withContext(Dispatchers.IO) {
            check(online()) { "Connect to share this flight." }
            check(flight.canShare && flight.isInFlightWindow(Instant.parse(now()))) { "This flight cannot be shared." }
            val text = message?.trim()?.takeIf(String::isNotEmpty)
            require((text?.length ?: 0) <= 2000) { "The message must contain at most 2,000 characters." }
            val owner = currentOwner() ?: throw IOException("Sign in to share this flight.")
            val observed = mutation.withLock { generation }
            check(currentStation() == flight.stationId) { "Your station changed. Reload the flight." }
            val input = PublishFlightShareRequest(text, staffMemberIds.distinct().sorted())
            val post = publishPost(flight.id, shareId, input)
            if (owner != currentOwner() || observed != mutation.withLock { generation } || currentStation() != flight.stationId)
                throw IOException("Your flight session changed. Reload the page.")
            if (post.id != shareId || post.flightId != flight.id || post.flight.stationId != flight.stationId)
                throw IOException("The shared flight response did not match its request.")
            mutation.withLock {
                if (owner != currentOwner() || observed != generation || currentStation() != flight.stationId) throw IOException("Your flight session changed.")
                for (direction in listOf(FlightShareDirection.All, FlightShareDirection.Sent)) {
                    val saved = snapshotFor(owner, direction)
                    if (saved != null && saved.scope.stationId == flight.stationId) {
                        val updated = saved.copy(items = (saved.items.filterNot { it.id == post.id } + post)
                            .sortedWith(compareByDescending<FlightSharePost> { parseFlightInstant(it.createdAtUtc) }.thenBy { it.id }))
                        store.write(owner, direction, json.encodeToString(updated))
                    }
                }
                epochs.keys.toList().forEach { epochs[it] = epochs.getValue(it) + 1 }
                stages.clear()
            }
            events.tryEmit(Unit)
            if (owner != currentOwner() || observed != mutation.withLock { generation } || currentStation() != flight.stationId)
                throw IOException("Your flight session changed. Reload the page.")
            post
        }

    /** Live details/complete acknowledgments update post cards without adding a new discovery membership. */
    suspend fun recordAcknowledged(detail: FlightDetail) = withContext(Dispatchers.IO) {
        val owner = currentOwner() ?: return@withContext
        val observed = mutation.withLock { generation }
        if (currentStation() != detail.stationId) return@withContext
        recordEvidence(owner, detail.id, detail.shareCard(), observed)
    }

    suspend fun recordPartialAcknowledged(flightId: String) {
        val owner = currentOwner() ?: return
        val observed = mutation.withLock { generation }
        val detail = cachedFlight(flightId)
        if (owner != currentOwner()) return
        if (detail != null && currentStation() == detail.stationId) recordEvidence(owner, flightId, detail.shareCard(), observed)
        else recordEvidence(owner, flightId, null, observed)
    }

    suspend fun clear() = withContext(Dispatchers.IO) {
        mutation.withLock { generation++; stages.clear(); epochs.clear(); evidence.clear(); lastSavedSequences.clear(); store.clearAll() }
        events.tryEmit(Unit)
    }

    private suspend fun recordEvidence(owner: String, id: String, card: FlightListItem?, observed: Long? = null) = withContext(Dispatchers.IO) {
        mutation.withLock {
            if (owner != currentOwner() || observed != null && observed != generation) return@withLock
            evidence["$owner\u0000$id"] = Evidence(++evidenceRevision, Long.MAX_VALUE, card)
            for (direction in FlightShareDirection.entries) {
                val saved = decode(store.read(owner, direction)) ?: continue
                if (saved.items.none { it.flightId == id }) continue
                val updated = saved.copy(items = saved.items.mapNotNull { post ->
                    if (post.flightId != id) post else card?.takeIf { it.stationId == saved.scope.stationId }?.let { post.copy(flight = it.copy(customerLogoVersion = post.flight.customerLogoVersion)) }
                })
                store.write(owner, direction, json.encodeToString(updated))
            }
        }
        events.tryEmit(Unit)
    }

    private fun sessionKey(owner: String, observed: Long) = "$owner\u0000$observed"

    suspend fun isCurrentScope(stationId: String, ownerSessionKey: String?): Boolean {
        val owner = currentOwner() ?: return false
        return ownerSessionKey == mutation.withLock { sessionKey(owner, generation) } && currentStation() == stationId
    }

    suspend fun reconcileVisible(items: List<FlightSharePost>, ownerSessionKey: String?): List<FlightSharePost> {
        val owner = currentOwner() ?: return emptyList()
        if (ownerSessionKey != mutation.withLock { sessionKey(owner, generation) }) return emptyList()
        val result = visible(owner, items)
        return result.takeIf { owner == currentOwner() && ownerSessionKey == mutation.withLock { sessionKey(owner, generation) } }.orEmpty()
    }

    suspend fun confirmStation(station: String) = withContext(Dispatchers.IO) {
        val owner = currentOwner() ?: return@withContext
        var changed = false
        mutation.withLock {
            for (direction in FlightShareDirection.entries) {
                val snapshot = decode(store.read(owner, direction))
                if (snapshot != null && snapshot.scope.stationId != station) {
                    store.remove(owner, direction); changed = true
                }
            }
            if (changed) { generation++; stages.clear(); evidence.clear() }
        }
        if (changed) events.tryEmit(Unit)
    }

    suspend fun revokeForOwner(owner: String, flightId: String?) = withContext(Dispatchers.IO) {
        if (flightId != null) { recordEvidence(owner, flightId, null); return@withContext }
        mutation.withLock {
            store.clearOwner(owner)
            if (owner == currentOwner()) { generation++; stages.clear(); evidence.clear() }
        }
        events.tryEmit(Unit)
    }

    private suspend fun acceptFreshEvidence(read: Read, items: List<FlightSharePost>) = mutation.withLock {
        if (read.owner != currentOwner() || !current(read)) throw IOException("Your flight session changed.")
        for (post in items) {
            val key = "${read.owner}\u0000${post.flightId}"
            val old = evidence[key]
            val laterAcknowledgment = old != null && old.readSequence == Long.MAX_VALUE && old.revision > read.evidenceRevision
            val newerNetworkRead = old != null && old.readSequence != Long.MAX_VALUE && old.readSequence > read.sequence
            if (!laterAcknowledgment && !newerNetworkRead)
                evidence[key] = Evidence(++evidenceRevision, read.sequence, post.flight)
        }
    }

    private suspend fun begin(direction: FlightShareDirection, term: String, reset: Boolean, channel: String = "query"): Read {
        val owner = currentOwner() ?: throw IOException("Sign in to view Shared Flights.")
        return mutation.withLock {
            val key = "$owner\u0000${direction.key}\u0000$term\u0000$channel"
            if (reset) { epochs[key] = (epochs[key] ?: 0) + 1; stages.remove(key) }
            Read(owner, generation, key, epochs[key] ?: 0, direction, evidenceRevision, ++readSequence)
        }
    }
    private suspend fun ensureCurrent(read: Read) {
        if (read.owner != currentOwner() || !mutation.withLock { current(read) })
            throw IOException("Your Shared Flights session changed. Reload the page.")
    }
    private fun current(read: Read) = read.generation == generation && read.epoch == (epochs[read.key] ?: 0)
    private suspend fun acceptStation(read: Read, station: String) = mutation.withLock {
        if (read.owner != currentOwner() || !current(read)) throw IOException("Your flight session changed.")
        for (direction in FlightShareDirection.entries) {
            val saved = decode(store.read(read.owner, direction))
            if (saved != null && saved.scope.stationId != station) store.remove(read.owner, direction)
        }
    }
    private suspend fun stage(read: Read, page: FlightSharePage, scope: FlightScope) {
        val complete = mutation.withLock {
            if (!current(read)) return@withLock null
            val stage = stages.getOrPut(read.key) { Stage(scope, page.totalCount, page.pageSize) }
            if (stage.scope.stationId != scope.stationId || stage.total != page.totalCount || stage.size != page.pageSize)
                throw IOException("Shared Flights changed while loading. Refresh the page.")
            stage.pages[page.page] = page.items
            val count = if (page.totalCount == 0L) 1 else ((page.totalCount - 1) / page.pageSize + 1).toInt()
            if (!(1..count).all { it in stage.pages }) return@withLock null
            val items = (1..count).flatMap { stage.pages.getValue(it) }
            if (items.size.toLong() != page.totalCount || items.map { it.id }.distinct().size != items.size)
                throw IOException("Shared Flights changed while loading. Refresh the page.")
            stages.remove(read.key)
            Snapshot(scope = stage.scope, items = items)
        }
        if (complete != null) save(read, complete)
    }
    private suspend fun save(read: Read, snapshot: Snapshot) {
        ensureCurrent(read)
        val changed = mutation.withLock {
            if (read.owner != currentOwner() || !current(read)) throw IOException("Your flight session changed.")
            if (currentStation() != snapshot.scope.stationId) throw IOException("Your station changed. Refresh Shared Flights.")
            val key = "${read.owner}\u0000${read.direction.key}"
            if ((lastSavedSequences[key] ?: 0) > read.sequence) return@withLock false
            // Reconcile under the commit lock: a newer acknowledgment must survive a complete-feed save.
            val items = reconcileRows(read.owner, snapshot.items, snapshot.scope.stationId, Instant.parse(now()), evidence)
            store.write(read.owner, read.direction, json.encodeToString(snapshot.copy(items = items)))
            lastSavedSequences[key] = read.sequence
            true
        }
        if (changed) events.tryEmit(Unit)
    }
    private suspend fun visible(owner: String, items: List<FlightSharePost>): List<FlightSharePost> {
        val known = mutation.withLock { evidence.toMap() }
        val station = currentStation() ?: return emptyList()
        val at = Instant.parse(now())
        return reconcileRows(owner, items, station, at, known)
    }
    private fun reconcileRows(owner: String, items: List<FlightSharePost>, station: String, at: Instant,
        known: Map<String, Evidence>): List<FlightSharePost> = items.mapNotNull { post ->
        val key = "$owner\u0000${post.flightId}"
        val card = if (known.containsKey(key)) known[key]?.card else post.flight
        card?.takeIf { it.stationId == station && !it.isPerLanding && !it.isAdHocFlight && !it.isMyAdHocFlight && it.isInFlightWindow(at) }
            ?.let { post.copy(flight = it.copy(customerLogoVersion = post.flight.customerLogoVersion)) }
    }
    private fun validate(page: FlightSharePage, number: Int, size: Int): FlightScope {
        val scope = page.flightPage().scopeOrNull() ?: throw IOException("Shared Flights scope is missing.")
        val count = if (page.totalCount <= 0) 0 else ((page.totalCount - 1) / size + 1).coerceAtMost(Int.MAX_VALUE.toLong()).toInt()
        if (page.page != number || page.pageSize != size || page.totalCount < 0 || page.totalPages != count ||
            page.hasPreviousPage != (number > 1) || page.hasNextPage != (number < count) || page.items.size > size ||
            page.items.map { it.id }.distinct().size != page.items.size || page.items.any {
                it.flightId != it.flight.id || it.flight.stationId != scope.stationId ||
                    it.flight.isPerLanding || it.flight.isAdHocFlight || it.flight.isMyAdHocFlight ||
                    !it.flight.isInFlightWindow(parseFlightInstant(scope.asOfUtc)) || it.isStationWide != it.recipients.isEmpty()
            }) throw IOException("Shared Flights page is incomplete or outside its station.")
        return scope
    }
    /** Only two complete legacy manifests can bridge an offline upgrade to the combined feed. */
    private fun snapshotFor(owner: String, direction: FlightShareDirection): Snapshot? {
        val raw = store.read(owner, direction)
        if (raw != null || direction != FlightShareDirection.All) return decode(raw)
        val received = decode(store.read(owner, FlightShareDirection.Received)) ?: return null
        val sent = decode(store.read(owner, FlightShareDirection.Sent)) ?: return null
        if (received.scope.stationId != sent.scope.stationId) return null
        val scope = if (parseFlightInstant(received.scope.asOfUtc) >= parseFlightInstant(sent.scope.asOfUtc)) received.scope else sent.scope
        val items = (received.items + sent.items).distinctBy { it.id }
            .sortedWith(compareByDescending<FlightSharePost> { parseFlightInstant(it.createdAtUtc) }.thenBy { it.id })
        return Snapshot(scope = scope, items = items)
    }

    private fun decode(raw: String?): Snapshot? = raw?.let { runCatching {
        json.decodeFromString<Snapshot>(it).takeIf { row -> row.version == 1 && row.scope.isValid() &&
            row.items.map { post -> post.id }.distinct().size == row.items.size && row.items.all { post ->
                post.flightId == post.flight.id && post.flight.stationId == row.scope.stationId }
        }
    }.getOrNull() }
    private suspend fun accessFailure(read: Read, error: Exception) {
        if (error !is ApiException || error.statusCode !in setOf(401, 403)) return
        mutation.withLock {
            if (read.owner == currentOwner() && current(read)) {
                generation++; stages.clear(); evidence.clear(); store.clearOwner(read.owner)
            }
        }
        events.tryEmit(Unit)
    }
    private data class Evidence(val revision: Long, val readSequence: Long, val card: FlightListItem?)
    private data class Read(val owner: String, val generation: Long, val key: String, val epoch: Long, val direction: FlightShareDirection,
        val evidenceRevision: Long, val sequence: Long)
    private data class Stage(val scope: FlightScope, val total: Long, val size: Int, val pages: MutableMap<Int, List<FlightSharePost>> = mutableMapOf())
    @Serializable private data class Snapshot(val version: Int = 1, val scope: FlightScope, val items: List<FlightSharePost>)
}

internal interface SharedFlightsStore {
    fun read(owner: String, direction: FlightShareDirection): String?
    fun write(owner: String, direction: FlightShareDirection, value: String)
    fun remove(owner: String, direction: FlightShareDirection)
    fun clearOwner(owner: String)
    fun clearAll()
}

/** Replaceable private snapshots are committed by atomic rename, never by appending incomplete pages. */
internal class DiskSharedFlightsStore(private val directory: File) : SharedFlightsStore {
    private fun prefix(owner: String) = MessageDigest.getInstance("SHA-256").digest(owner.toByteArray()).joinToString("") { "%02x".format(it) }
    private fun file(owner: String, direction: FlightShareDirection) = File(directory, "${prefix(owner)}-${direction.key}.json")
    override fun read(owner: String, direction: FlightShareDirection) = file(owner, direction).takeIf { it.isFile }?.readText()
    override fun write(owner: String, direction: FlightShareDirection, value: String) {
        check(directory.isDirectory || directory.mkdirs()) { "Shared Flights cache could not be opened." }
        val target = file(owner, direction)
        val temporary = File(directory, "${target.name}.pending")
        temporary.writeText(value)
        if (!temporary.renameTo(target)) { temporary.delete(); throw IOException("Shared Flights cache could not be saved.") }
    }
    override fun remove(owner: String, direction: FlightShareDirection) { file(owner, direction).delete() }
    override fun clearOwner(owner: String) { directory.listFiles()?.filter { it.name.startsWith(prefix(owner) + "-") }?.forEach { it.delete() } }
    override fun clearAll() { directory.listFiles()?.forEach { it.delete() } }
}

private fun Exception.isShareTransient() = this is IOException || this is SerializationException ||
    this is io.ktor.client.plugins.HttpRequestTimeoutException || this is ApiException && (statusCode in 500..599 || statusCode in setOf(408, 429))
private fun FlightListItem.shareMatches(term: String): Boolean {
    val compact = term.replace("-", "").replace(" ", "")
    return flightNumber.lowercase(Locale.ROOT).replace("-", "").replace(" ", "").contains(compact) || customerName.lowercase(Locale.ROOT).contains(term)
}
private fun FlightDetail.shareCard() = FlightListItem(id, flightNumber, customerIataCode, customerName, stationIata,
    operationTypeName, scheduledArrivalUtc, scheduledDepartureUtc, isPerLanding, aircraftTypeModel, customerId,
    stationId = stationId, isMyFlight = assignedEmployees.any { it.staffMemberId.equals(currentStaffMemberId, true) },
    stationTimeZoneId = stationTimeZoneId, isAdHocFlight = isAdHocFlight, createdByUserId = createdByUserId,
    createdByDisplayName = createdByDisplayName).withReturnToRampReports(this)
