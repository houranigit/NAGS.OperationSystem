package com.nags.operations.data.repo

import com.nags.operations.data.*
import com.nags.operations.data.api.HttpClientFactory
import com.nags.operations.data.db.dao.FlightDiscoveryDao
import com.nags.operations.data.db.entities.*
import io.ktor.client.plugins.HttpRequestTimeoutException
import java.io.IOException
import java.time.Instant
import java.util.Locale
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.serialization.Serializable
import kotlinx.serialization.SerializationException
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

/** Independent, bounded Search/History memberships. They never join the Scheduled manifest. */
@OptIn(ExperimentalCoroutinesApi::class)
class DiscoveryFlightsRepository internal constructor(
    private val dao: FlightDiscoveryDao,
    private val owners: Flow<String?>,
    private val currentOwner: suspend () -> String?,
    private val online: () -> Boolean,
    private val fetchPage: suspend (FlightDiscoverySource, String, Int, Int) -> FlightPage,
    private val fetchDetail: suspend (String) -> FlightDetail,
    private val prepareOffline: suspend (FlightDetail) -> Unit,
    private val transaction: suspend (suspend () -> Unit) -> Unit,
    private val beginScopeRead: suspend () -> FlightExternalScopeRead? = { null },
    private val confirmScopeRead: suspend (FlightPage, FlightExternalScopeRead?) -> Unit = { _, _ -> },
    private val localStationAuthority: suspend (String) -> Boolean = { false },
    private val hasPendingWork: suspend (String) -> Boolean = { false },
    private val isUnmaterializedLocalFlight: suspend (String) -> Boolean = { false },
    private val onFlightAccessLost: suspend (String, Int) -> Unit = { _, _ -> },
    private val beginLogoRead: suspend () -> LogoMetadataRead? = { null },
    private val reconcileLogos: suspend (List<FlightListItem>, LogoMetadataRead?) -> Unit = { _, _ -> },
    private val prepareLogos: suspend (Set<String>) -> Unit = {},
    private val now: () -> String = { Instant.now().toString() },
    private val json: Json = HttpClientFactory.json,
    private val fetchOfflinePage: (suspend (Int, Int) -> FlightPage)? = null,
) {
    private val syncGate = Mutex()
    private val readGate = Any()
    @Volatile private var generation = 0L
    @Volatile private var stationRevision = 0L
    @Volatile private var acknowledgmentRevision = 0L
    private val epochs = mutableMapOf<String, Long>()
    private val staging = mutableMapOf<String, StagedPages>()
    private val deniedFlightVersions = mutableMapOf<String, Long>()
    private val manifestGate = Any()
    private val manifests = object : LinkedHashMap<String, ManifestDecode>(16, 0.75f, true) {
        override fun removeEldestEntry(eldest: MutableMap.MutableEntry<String, ManifestDecode>) = size > 64
    }

    /** Initial state/attempt metadata do not fabricate a refresh event. */
    val changes: Flow<Unit> = owners.distinctUntilChanged().flatMapLatest { owner ->
        if (owner == null) emptyFlow() else combine(dao.observeQueries(owner), dao.observeInteractions(owner)) { queries, local ->
            queries.filter { it.payloadJson != null || it.denied }.sortedWith(compareBy({ it.source }, { it.query })).map { row ->
                val snapshot = decode(row)
                listOf(row.source, row.query, snapshot?.scope?.stationId, snapshot?.items?.let { json.encodeToString(it) }, row.denied.toString(), row.accessStatus?.toString())
            } to
                local.sortedBy { it.flightId }.map { listOf(it.flightId, it.payloadJson, it.interactionAtUtc, it.usedSearch.toString(), it.viewRevoked.toString()) }
        }.distinctUntilChanged().drop(1).map { Unit }
    }

    /** Cached membership coverage only. Full-flight sync success remains the media-readiness gate. */
    val offlineSearchStatus: Flow<FlightSyncStatus> = owners.distinctUntilChanged().flatMapLatest { owner ->
        if (owner == null) flowOf(FlightSyncStatus()) else combine(dao.observeQueries(owner), flow {
            while (true) { emit(Instant.parse(now())); delay(60_000L) }
        }) { rows, at ->
            val row = rows.firstOrNull { it.source == Feed.OfflineSearch.key && it.query.isEmpty() }
            val station = dao.station(owner)?.stationId
            val snapshot = row?.takeUnless { it.denied }?.let(::decode)?.takeIf { it.scope.stationId == station }
            if (owner != currentOwner()) FlightSyncStatus() else FlightSyncStatus(
                itemCount = snapshot?.items?.count { it.isInFlightWindow(at) } ?: 0,
                lastAttemptUtc = row?.lastAttemptUtc, lastSuccessUtc = row?.lastSuccessUtc,
                error = row?.error, permissionDenied = row?.denied == true)
        }
    }.distinctUntilChanged().flowOn(Dispatchers.Default)

    suspend fun page(source: FlightDiscoverySource, query: String = "", page: Int = 1, pageSize: Int = 20): FlightPage =
        withContext(Dispatchers.Default) { page(Feed.from(source), query, page, pageSize) }

    private suspend fun page(source: Feed, query: String, page: Int, pageSize: Int): FlightPage {
        val owner = checkNotNull(currentOwner()) { "Sign in to view flights." }
        val term = normalize(source, query)
        val number = page.coerceAtLeast(1); val size = pageSize.coerceIn(1, SYNC_PAGE_SIZE)
        if (!online()) return cachedPage(owner, source, term, number, size)
        val read = beginRead(owner, source, term, newSequence = number == 1)
        try {
            val result = fetchRemotePage(source, term, number, size)
            val scope = validatePage(result, source, term, number, size)
            confirmScopeRead(result, read.external)
            acceptStation(read, scope.stationId)
            reconcileLogos(result.items, read.logos)
            val complete = stage(read, result)
            if (complete != null) publish(read, complete)
            ensureCurrent(read)
            return overlayLocal(owner, source, term, result.copy(items = result.items.filter { it.isInFlightWindow(Instant.parse(now())) }),
                knownIds = complete?.items?.mapTo(mutableSetOf()) { it.id } ?: synchronized(readGate) { staging[read.key]?.ids().orEmpty() })
        } catch (cancelled: CancellationException) { throw cancelled }
        catch (error: Exception) {
            if (!isCurrent(read)) throw error
            if (handleAccessError(owner, error, source)) throw error
            if (!error.isDiscoveryTransient()) throw error
            return cachedPageOrNull(owner, source, term, number, size) ?: throw error
        }
    }

    /** Full readiness covers all regular unassigned flights, cross-device History/Recent and known exact queries. */
    suspend fun refreshKnownAndPrepare() = syncGate.withLock {
        val owner = currentOwner() ?: return@withLock
        for (source in Feed.entries.filter { it != Feed.OfflineSearch || fetchOfflinePage != null }) register(owner, source, "")
        val queries = dao.queries(owner).mapNotNull { row -> Feed.entries.firstOrNull { it.key == row.source }?.let { it to row.query } }
            .distinct().sortedWith(compareBy({ it.first.ordinal }, { it.second }))
        val seen = mutableSetOf<String>()
        val prepared = mutableMapOf<String, String>()
        val deniedFlights = mutableSetOf<String>()
        val unavailableSources = mutableSetOf<Feed>()
        for ((source, term) in queries) {
            if (source in unavailableSources) continue
            // A background recheck joins the current sequence; only a newer foreground page1 may replace it.
            val read = beginRead(owner, source, term, newSequence = false)
            try {
                val snapshot = readComplete(read).let { it.copy(items = it.items.filterNot { item -> item.id in deniedFlights }) }
                // Membership is complete before downloads; revoke only this source's missing rows immediately.
                publish(read, snapshot)
                reconcileLogos(snapshot.items, read.logos)
                prepareLogos(snapshot.items.mapNotNull { it.customerId }.toSet())
                val preparedMembership = if (source == Feed.OfflineSearch) snapshot.items.mapTo(mutableSetOf()) { it.id } else null
                for (item in snapshot.items) {
                    ensureReadScope(read, snapshot.scope.stationId)
                    if (seen.add(item.id)) prepareKnownFlight(read, item.id, snapshot.scope.stationId, deniedFlights, prepared,
                        preparedMembership)
                }
                // Retained own intent is a separate source even before the server History index includes it.
                if (source == Feed.History && term.isEmpty()) for (detail in localDetails(owner, includePendingHidden = true)) {
                    if (!seen.add(detail.id)) continue
                    if (isUnmaterializedLocalFlight(detail.id)) { prepareOffline(detail); prepared[detail.id] = json.encodeToString(detail) }
                    else prepareKnownFlight(read, detail.id, snapshot.scope.stationId, deniedFlights, prepared)
                }
                ensureReadScope(read, snapshot.scope.stationId)
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (error: Exception) {
                if (read.owner != currentOwner() || read.generation != generation) throw error
                if (source == Feed.OfflineSearch && error is ApiException && error.statusCode == 404) {
                    markError(read, "Offline flight search is not available on this server. Retry after the server is updated.")
                    throw IOException("Offline flight search is not available on this server.", error)
                }
                if (!handleAccessError(owner, error, source)) {
                    markError(read, "Offline preparation is incomplete. Retry when connected.")
                    throw error
                } else {
                    if (error is ApiException && error.statusCode == 401) throw error
                    unavailableSources += accessFamily(source)
                }
            }
        }
        if (owner != currentOwner()) throw IOException("Your flight session changed during synchronization.")
        val latest = dao.queries(owner)
        if (latest.any { it.payloadJson == null && !it.denied }) throw IOException("A new flight query is awaiting complete synchronization.")
        val required = (logoFlights().map { it.id } + localDetails(owner).map { it.id }).toSet()
        for (id in required) {
            val expected = prepared[id] ?: throw IOException("New flight data became available during synchronization. Retry.")
            if (dao.detail(owner, id)?.payloadJson != expected) throw IOException("A flight changed during offline preparation. Retry.")
        }
    }

    private suspend fun prepareKnownFlight(read: Read, id: String, station: String, denied: MutableSet<String>, prepared: MutableMap<String, String>, preparedMembership: Set<String>? = null) {
        if (preparedMembership != null && !authorizedForPreparation(read.owner, id, read, preparedMembership)) return
        val version = detailRevision(id)
        val detail = try { fetchDetail(id) }
        catch (error: ApiException) {
            ensureReadScope(read, station)
            if (error.statusCode != 403 && error.statusCode != 404) throw error
            denied += id; revoke(id); onFlightAccessLost(id, error.statusCode); return
        }
        ensureReadScope(read, station)
        if (detail.id != id || detail.stationId != station || !detail.isInFlightWindow(Instant.parse(now()))) {
            denied += id; revoke(id); onFlightAccessLost(id, 404); return
        }
        if (preparedMembership != null && !authorizedForPreparation(read.owner, id, read, preparedMembership)) return
        val resolved = try { resolveFetched(detail, version, persist = false, preparationRead = read.takeIf { preparedMembership != null }, preparedMembership = preparedMembership) }
        catch (error: ApiException) {
            ensureReadScope(read, station)
            if (error.statusCode != 403 && error.statusCode != 404) throw error
            denied += id; revoke(id); onFlightAccessLost(id, error.statusCode); return
        }
        ensureReadScope(read, station)
        try { prepareOffline(resolved.detail) }
        catch (error: ApiException) {
            ensureReadScope(read, station)
            if (error.statusCode != 403 && error.statusCode != 404) throw error
            denied += id; revoke(id); onFlightAccessLost(id, error.statusCode); return
        }
        keepPrepared(read, resolved, preparedMembership)
        prepared[id] = json.encodeToString(resolved.detail)
        ensureReadScope(read, station)
    }

    suspend fun detailRevision(id: String): Long = currentOwner()?.let { owner ->
        maxOf(dao.detail(owner, id)?.revision ?: 0L, synchronized(readGate) { deniedFlightVersions["$owner/$id"] ?: 0L })
    } ?: 0L

    /** Live details are retained only under current complete membership or an actual local-save source. */
    suspend fun recordFetched(detail: FlightDetail, expectedRevision: Long): FlightDetail = resolveFetched(detail, expectedRevision, persist = true).detail

    private suspend fun resolveFetched(detail: FlightDetail, expectedRevision: Long, persist: Boolean, preparationRead: Read? = null, preparedMembership: Set<String>? = null): ResolvedDetail {
        val owner = currentOwner() ?: return ResolvedDetail(detail, expectedRevision)
        var accepted = ResolvedDetail(detail, expectedRevision)
        transaction {
            if (owner != currentOwner() || !eligible(owner, detail)) return@transaction
            val existing = dao.detail(owner, detail.id)
            val denied = synchronized(readGate) { deniedFlightVersions["$owner/${detail.id}"] }
            val revision = maxOf(existing?.revision ?: 0L, denied ?: 0L)
            if (denied != null && revision != expectedRevision) throw ApiException(403, "Flight access changed. Reload the flight.")
            val retained = dao.interaction(owner, detail.id)?.takeIf { it.stationId == detail.stationId }
            val restoreLocal = retained?.viewRevoked == true && hasPendingWork(detail.id)
            if (!authorizedForPreparation(owner, detail.id, preparationRead, preparedMembership) && !restoreLocal) return@transaction
            if (revision != expectedRevision) {
                existing?.let { decodeDetail(it.payloadJson) }?.takeIf { eligible(owner, it) }?.let { accepted = ResolvedDetail(it, revision) }
            } else {
                accepted = ResolvedDetail(detail, revision, restoreLocal)
                if (persist) {
                    if (restoreLocal) dao.putInteraction(checkNotNull(retained).copy(payloadJson = json.encodeToString(detail), viewRevoked = false))
                    synchronized(readGate) { deniedFlightVersions.remove("$owner/${detail.id}") }
                    dao.putDetail(FlightDiscoveryDetailEntity(owner, detail.id, json.encodeToString(detail), revision))
                }
            }
        }
        return accepted
    }

    private suspend fun keepPrepared(read: Read, resolved: ResolvedDetail, preparedMembership: Set<String>? = null) {
        transaction {
            ensureReadScope(read, resolved.detail.stationId)
            if (!eligible(read.owner, resolved.detail)) return@transaction
            val revision = maxOf(dao.detail(read.owner, resolved.detail.id)?.revision ?: 0L,
                synchronized(readGate) { deniedFlightVersions["${read.owner}/${resolved.detail.id}"] ?: 0L })
            if (revision != resolved.revision) throw IOException("The flight changed while preparing its media. Retry synchronization.")
            if (resolved.restoreLocal) dao.interaction(read.owner, resolved.detail.id)?.let {
                dao.putInteraction(it.copy(payloadJson = json.encodeToString(resolved.detail), viewRevoked = false))
            }
            if (!authorizedForPreparation(read.owner, resolved.detail.id, read.takeIf { preparedMembership != null }, preparedMembership)) return@transaction
            synchronized(readGate) { deniedFlightVersions.remove("${read.owner}/${resolved.detail.id}") }
            dao.putDetail(FlightDiscoveryDetailEntity(read.owner, resolved.detail.id, json.encodeToString(resolved.detail), revision))
        }
    }

    suspend fun cachedFlight(id: String): FlightDetail? = withContext(Dispatchers.Default) { cachedFlightOnWorker(id) }

    private suspend fun cachedFlightOnWorker(id: String): FlightDetail? {
        val owner = currentOwner() ?: return null
        var result: FlightDetail? = null
        transaction {
            if (owner != currentOwner() || !authorized(owner, id)) return@transaction
            result = dao.detail(owner, id)?.let { decodeDetail(it.payloadJson) }?.takeIf { eligible(owner, it) }
                ?: dao.interaction(owner, id)?.takeUnless { it.viewRevoked }?.let { decodeDetail(it.payloadJson) }?.takeIf { eligible(owner, it) }
        }
        return result?.copy(fromCache = true)?.takeIf { owner == currentOwner() }
    }

    /** Called inside the normal queue's Room transaction, after real durable input is staged. */
    suspend fun recordLocalSave(detail: FlightDetail) {
        val owner = currentOwner() ?: return
        transaction {
            if (owner != currentOwner()) return@transaction
            if (dao.station(owner) == null && localStationAuthority(detail.stationId)) {
                dao.putStation(FlightDiscoveryStationEntity(owner, detail.stationId))
            }
            check(eligible(owner, detail)) { "Your flight scope changed. Reopen the flight before saving." }
            val previous = dao.interaction(owner, detail.id)
            val usedSearch = previous?.usedSearch == true || detail.isUnassignedSearchFlight
            val revoked = previous?.viewRevoked == true || synchronized(readGate) { "$owner/${detail.id}" in deniedFlightVersions }
            dao.putInteraction(FlightLocalInteractionEntity(owner, detail.id, detail.stationId, json.encodeToString(detail), now(), usedSearch, viewRevoked = revoked))
            if (!revoked) {
                val existing = dao.detail(owner, detail.id)
                dao.putDetail(FlightDiscoveryDetailEntity(owner, detail.id, json.encodeToString(detail), existing?.revision ?: 0L))
            }
        }
    }

    /** Exact write ACKs keep local recency through queue cleanup and defeat older in-flight read responses. */
    suspend fun recordAcknowledged(acknowledged: FlightDetail) {
        val detail = acknowledged.restrictCapabilitiesAfterKnownWork()
        val owner = currentOwner() ?: return
        transaction {
            if (owner != currentOwner() || !eligible(owner, detail)) return@transaction
            val previous = dao.interaction(owner, detail.id)
            dao.putInteraction(previous?.copy(payloadJson = json.encodeToString(detail), acknowledged = true, viewRevoked = false)
                ?: FlightLocalInteractionEntity(owner, detail.id, detail.stationId, json.encodeToString(detail), now(), detail.isUnassignedSearchFlight, acknowledged = true))
            synchronized(readGate) { deniedFlightVersions.remove("$owner/${detail.id}") }
            run {
                val revision = maxOf(acknowledgmentRevision, dao.detail(owner, detail.id)?.revision ?: 0L) + 1
                acknowledgmentRevision = revision
                dao.putDetail(FlightDiscoveryDetailEntity(owner, detail.id, json.encodeToString(detail), revision))
                for (row in dao.queries(owner)) decode(row)?.let { snapshot ->
                    if (snapshot.items.any { it.id == detail.id }) dao.putQuery(row.copy(payloadJson = json.encodeToString(snapshot.copy(
                        items = snapshot.items.map { if (it.id == detail.id) it.withAcknowledged(detail) else it }
                            .filter { it.isInFlightWindow(parseFlightInstant(snapshot.scope.asOfUtc)) }))))
                }
            }
        }
    }

    suspend fun recordArrival(id: String, saved: FlightArrivalSaved) = mutateAcknowledged(id) { detail ->
        detail.copy(rowVersion = saved.rowVersion, arrivalSubmissions = detail.arrivalSubmissions.filterNot { it.id == saved.submission.id } + saved.submission)
    }
    suspend fun recordDeparture(id: String, saved: FlightDepartureSaved) = mutateAcknowledged(id) { detail ->
        detail.copy(rowVersion = saved.rowVersion, departureSubmissions = detail.departureSubmissions.filterNot { it.id == saved.submission.id } + saved.submission)
    }
    suspend fun recordService(id: String, saved: FlightServiceSaved) = mutateAcknowledged(id) { detail ->
        detail.copy(rowVersion = saved.rowVersion, services = detail.services.filterNot { it.id == saved.service.id } + saved.service)
    }
    suspend fun recordServiceDeleted(id: String, saved: FlightServiceDeleted) = mutateAcknowledged(id) { detail ->
        detail.copy(rowVersion = saved.rowVersion, services = detail.services.filterNot { it.id == saved.id })
    }
    suspend fun recordTask(id: String, saved: FlightTaskSaved) = mutateAcknowledged(id) { detail ->
        detail.copy(rowVersion = saved.rowVersion, tasks = detail.tasks.filterNot { it.id == saved.task.id } + saved.task)
    }
    suspend fun recordTaskDeleted(id: String, saved: FlightTaskDeleted) = mutateAcknowledged(id) { detail ->
        detail.copy(rowVersion = saved.rowVersion, tasks = detail.tasks.filterNot { it.id == saved.id })
    }
    suspend fun recordCancellation(id: String, saved: FlightCancellationSaved) = mutateAcknowledged(id) { detail ->
        detail.copy(rowVersion = saved.rowVersion, hasCancellationSubmission = true,
            cancellationSubmissions = detail.cancellationSubmissions.filterNot { it.id == saved.submission.id } + saved.submission)
    }
    suspend fun recordCancellationReverted(id: String, saved: FlightCancellationReverted) = mutateAcknowledged(id) { detail ->
        detail.copy(rowVersion = saved.rowVersion, hasCancellationSubmission = saved.cancellationSubmissions.isNotEmpty(), cancellationSubmissions = saved.cancellationSubmissions)
    }
    private suspend fun mutateAcknowledged(id: String, change: (FlightDetail) -> FlightDetail) {
        val owner = currentOwner() ?: return
        val base = dao.detail(owner, id)?.let { decodeDetail(it.payloadJson) }
            ?: dao.interaction(owner, id)?.let { decodeDetail(it.payloadJson) } ?: return
        if (owner == currentOwner()) recordAcknowledged(change(base))
    }

    /** Branding authorization joins customer membership only; it never changes normal flight membership. */
    suspend fun logoFlights(): List<FlightListItem> {
        val owner = currentOwner() ?: return emptyList()
        val station = dao.station(owner)?.stationId ?: return emptyList()
        val at = Instant.parse(now())
        val rows = dao.queries(owner).filterNot { it.denied }.mapNotNull(::decode)
            .filter { it.scope.stationId == station }.flatMap { it.items }.filter { it.isInFlightWindow(at) }
        return rows.distinctBy { it.id }.takeIf { owner == currentOwner() }.orEmpty()
    }

    /** A confirmed station from any Operations source revokes every older station's read memberships. */
    suspend fun confirmStation(stationId: String) {
        val owner = currentOwner() ?: return
        transaction {
            if (owner != currentOwner()) return@transaction
            acceptStationLocked(owner, stationId)
        }
    }
    suspend fun revoke(id: String? = null) {
        val owner = currentOwner() ?: return
        revokeForOwner(owner, id)
    }
    suspend fun revokeForOwner(owner: String, id: String? = null) {
        val active = currentOwner()
        if (active != owner && active != null) return
        transaction {
            val current = currentOwner()
            if (current != owner && current != null) return@transaction
            if (id == null) {
                generation++
                dao.deleteQueries(owner); dao.deleteDetails(owner); dao.deleteStation(owner)
                for (row in dao.interactions(owner)) dao.putInteraction(row.copy(viewRevoked = true))
            } else {
                val revokedVersion = maxOf(acknowledgmentRevision, dao.detail(owner, id)?.revision ?: 0L) + 1
                acknowledgmentRevision = revokedVersion
                synchronized(readGate) { deniedFlightVersions["$owner/$id"] = revokedVersion }
                for (row in dao.queries(owner)) decode(row)?.let { snapshot ->
                    if (snapshot.items.any { it.id == id }) dao.putQuery(row.copy(payloadJson = json.encodeToString(snapshot.copy(items = snapshot.items.filterNot { it.id == id }))))
                }
                dao.deleteDetail(owner, id)
                dao.interaction(owner, id)?.let { dao.putInteraction(it.copy(viewRevoked = true)) }
            }
        }
    }
    suspend fun clear() {
        generation++; stationRevision++; acknowledgmentRevision = 0L
        synchronized(readGate) { epochs.clear(); staging.clear(); deniedFlightVersions.clear() }
        synchronized(manifestGate) { manifests.clear() }
        transaction { dao.clearQueries(); dao.clearDetails(); dao.clearStations(); dao.clearInteractions() }
    }

    private suspend fun beginRead(owner: String, source: Feed, term: String, newSequence: Boolean): Read {
        val external = beginScopeRead()
        val logos = beginLogoRead()
        val key = "$owner\u0000${source.key}\u0000$term"
        val epoch = synchronized(readGate) {
            if (newSequence || key !in epochs) {
                epochs[key] = (epochs[key] ?: 0L) + 1; staging.remove(key)
            }
            checkNotNull(epochs[key])
        }
        acknowledgmentRevision = maxOf(acknowledgmentRevision, dao.maxDetailRevision(owner))
        val read = Read(owner, source, term, key, generation, stationRevision, epoch, acknowledgmentRevision, external, logos)
        register(owner, source, term)
        return read
    }
    private suspend fun register(owner: String, source: Feed, term: String) {
        transaction {
            if (owner == currentOwner() && dao.query(owner, source.key, term) == null)
                dao.putQuery(FlightDiscoveryQueryEntity(owner, source.key, term))
        }
    }
    private suspend fun readComplete(read: Read): DiscoverySnapshot {
        val items = linkedMapOf<String, FlightListItem>()
        var number = 1; var expected: Long? = null; var scope: FlightScope? = null
        while (true) {
            ensureCurrent(read)
            val page = fetchRemotePage(read.source, read.term, number, SYNC_PAGE_SIZE)
            val incoming = validatePage(page, read.source, read.term, number, SYNC_PAGE_SIZE)
            confirmScopeRead(page, read.external)
            acceptStation(read, incoming.stationId)
            if (expected != null && expected != page.totalCount || scope != null && scope.stationId != incoming.stationId)
                throw IOException("The flight query changed during synchronization.")
            expected = page.totalCount; scope = incoming
            for (item in page.items) if (items.put(item.id, item) != null) throw IOException("Flight pages overlap.")
            if (!page.hasNextPage) break
            if (page.items.isEmpty() || items.size.toLong() >= page.totalCount) throw IOException("Flight pagination is incomplete.")
            number++
        }
        if (items.size.toLong() != expected) throw IOException("Flight pagination is incomplete.")
        return DiscoverySnapshot(1, checkNotNull(scope), items.values.toList())
    }
    private fun stage(read: Read, page: FlightPage): DiscoverySnapshot? = synchronized(readGate) {
        if (epochs[read.key] != read.epoch) throw IOException("A newer flight query replaced this response.")
        val state = staging.getOrPut(read.key) { StagedPages(page.totalCount, checkNotNull(page.scopeOrNull())) }
        if (state.total != page.totalCount || state.scope.stationId != page.stationId ||
            state.pages.values.any { it.pageSize != page.pageSize }) {
            staging.remove(read.key); throw IOException("The flight query changed while paging.")
        }
        state.pages[page.page] = page
        val last = state.pages.values.firstOrNull { !it.hasNextPage }?.page ?: return@synchronized null
        if ((1..last).any { it !in state.pages }) return@synchronized null
        val rows = (1..last).flatMap { state.pages.getValue(it).items }
        if (rows.size.toLong() != state.total || rows.distinctBy { it.id }.size != rows.size)
            throw IOException("Flight pagination is incomplete or overlapping.")
        DiscoverySnapshot(1, state.scope, rows)
    }
    private suspend fun publish(read: Read, downloaded: DiscoverySnapshot) {
        transaction {
            ensureCurrent(read)
            check(dao.station(read.owner)?.stationId == downloaded.scope.stationId) { "Your flight station changed." }
            val known = dao.detailsNewerThan(read.owner, read.acknowledgments).associateBy { it.flightId }
            val retained = downloaded.items.filter { it.isInFlightWindow(Instant.parse(now())) }.map { item ->
                known[item.id]?.let { decodeDetail(it.payloadJson) }?.let { item.withAcknowledged(it) } ?: item
            }.filter { it.isInFlightWindow(parseFlightInstant(downloaded.scope.asOfUtc)) }
            dao.putQuery(FlightDiscoveryQueryEntity(read.owner, read.source.key, read.term,
                json.encodeToString(downloaded.copy(items = retained)), now(), now()))
            if (read.source == Feed.History && read.term.isEmpty()) {
                for (local in dao.interactions(read.owner)) if (!hasPendingWork(local.flightId) &&
                    (dao.detail(read.owner, local.flightId)?.revision ?: 0L) <= read.acknowledgments)
                    dao.deleteInteraction(read.owner, local.flightId)
            }
            pruneDetailsLocked(read.owner)
            ensureCurrent(read)
        }
    }
    private suspend fun acceptStation(read: Read, station: String) {
        transaction {
            ensureCurrent(read)
            val current = dao.station(read.owner)?.stationId
            if (read.stationRevision != stationRevision && current != null && current != station)
                throw IOException("A newer flight station replaced this response.")
            acceptStationLocked(read.owner, station)
        }
    }
    private suspend fun acceptStationLocked(owner: String, station: String) {
        val current = dao.station(owner)?.stationId
        if (current != null && current != station) {
            stationRevision++
            dao.deleteQueries(owner); dao.deleteDetails(owner)
            for (row in dao.interactions(owner)) if (row.stationId != station) dao.putInteraction(row.copy(viewRevoked = true))
        }
        if (current != station) dao.putStation(FlightDiscoveryStationEntity(owner, station))
    }
    private suspend fun authorized(owner: String, id: String): Boolean {
        val station = dao.station(owner)?.stationId ?: return false
        val at = Instant.parse(now())
        val queries = dao.queries(owner)
        return queries.any { row -> !row.denied && manifest(row)?.let { value -> value.snapshot.scope.stationId == station && value.byId[id]?.isInFlightWindow(at) == true } == true } ||
            queries.none { it.source == Feed.History.key && it.denied } && dao.interactions(owner).any {
                it.flightId == id && !it.viewRevoked && it.stationId == station && decodeDetail(it.payloadJson)?.isInFlightWindow(at) == true }
    }
    private suspend fun eligible(owner: String, detail: FlightDetail) = dao.station(owner)?.stationId == detail.stationId && detail.isInFlightWindow(Instant.parse(now()))
    private suspend fun localDetails(owner: String, includePendingHidden: Boolean = false): List<FlightDetail> {
        if (dao.queries(owner).any { it.source == Feed.History.key && it.denied }) return emptyList()
        val station = dao.station(owner)?.stationId ?: return emptyList()
        val values = mutableListOf<FlightDetail>()
        for (row in dao.interactions(owner).filter { it.stationId == station }) {
            if (row.viewRevoked && (!includePendingHidden || !hasPendingWork(row.flightId))) continue
            decodeDetail(row.payloadJson)?.takeIf { it.isInFlightWindow(Instant.parse(now())) }?.let(values::add)
        }
        return values
    }
    private suspend fun pruneDetailsLocked(owner: String) {
        val station = dao.station(owner)?.stationId
        val at = Instant.parse(now())
        val queries = dao.queries(owner)
        val allowed = queries.filterNot { it.denied }.mapNotNull(::decode).filter { it.scope.stationId == station }
            .flatMap { it.items }.filter { it.isInFlightWindow(at) }.mapTo(mutableSetOf()) { it.id }
        val local = dao.interactions(owner)
        if (queries.none { it.source == Feed.History.key && it.denied }) local.filter { !it.viewRevoked && it.stationId == station }.forEach { row ->
            if (decodeDetail(row.payloadJson)?.isInFlightWindow(at) == true) allowed += row.flightId
        }
        for (row in dao.details(owner)) if (row.flightId !in allowed) dao.deleteDetail(owner, row.flightId)
        for (row in local) if (decodeDetail(row.payloadJson)?.isInFlightWindow(at) != true && !hasPendingWork(row.flightId))
            dao.deleteInteraction(owner, row.flightId)
    }
    private suspend fun cachedPage(owner: String, source: Feed, term: String, page: Int, size: Int) =
        cachedPageOrNull(owner, source, term, page, size) ?: throw IllegalStateException("Connect and synchronize this flight query before viewing it offline.")
    private suspend fun cachedPageOrNull(owner: String, source: Feed, term: String, page: Int, size: Int): FlightPage? {
        if (owner != currentOwner()) return null
        val row = dao.query(owner, source.key, term)
        if (row?.denied == true) throw ApiException(row.accessStatus ?: 403, row.error ?: "This flight source is unavailable.")
        val station = dao.station(owner)?.stationId ?: return null
        val exact = row?.let(::manifest)?.takeIf { it.snapshot.scope.stationId == station }
        // Typed Search uses the station index; My Work filters its own complete default membership.
        val catalogRow = if (term.isEmpty()) null else when (source) {
            Feed.Search -> dao.query(owner, Feed.OfflineSearch.key, "")
            Feed.History -> dao.query(owner, Feed.History.key, "")
            Feed.OfflineSearch -> null
        }
        if (source == Feed.History && catalogRow?.denied == true)
            throw ApiException(catalogRow.accessStatus ?: 403, catalogRow.error ?: "This flight source is unavailable.")
        val catalog = catalogRow?.takeUnless { it.denied }?.let(::manifest)?.takeIf { it.snapshot.scope.stationId == station }
        val available = (if (source == Feed.History) listOfNotNull(exact ?: catalog) else listOfNotNull(exact, catalog))
            .sortedByDescending { parseFlightInstant(it.snapshot.scope.asOfUtc) }
        val at = Instant.parse(now())
        val remote = linkedMapOf<String, FlightListItem>()
        available.forEach { value ->
            val rows = if (value !== catalog) value.snapshot.items else if (source == Feed.History)
                value.snapshot.items.filter { it.discoveryMatches(term) } else value.byNumber[term].orEmpty()
            rows.filter { it.isInFlightWindow(at) }.forEach { remote.putIfAbsent(it.id, it) }
        }
        val local = localItems(owner, source, term)
        if (available.isEmpty() && local.isEmpty()) {
            if (catalogRow?.denied == true) throw ApiException(catalogRow.accessStatus ?: 403, catalogRow.error ?: "This flight source is unavailable.")
            return null
        }
        val merged = mergeItems(source, term, remote.values.toList(), local)
        val skip = (page.toLong() - 1L) * size
        val count = if (merged.isEmpty()) 0 else (merged.size - 1) / size + 1
        val saved = available.firstOrNull()?.snapshot
        if (owner != currentOwner() || dao.station(owner)?.stationId != station) return null
        return FlightPage(if (skip >= merged.size) emptyList() else merged.drop(skip.toInt()).take(size), page, size,
            merged.size.toLong(), count, page > 1, page < count, fromCache = true, stationId = station,
            asOfUtc = saved?.scope?.asOfUtc ?: now(), windowStartUtc = saved?.scope?.windowStartUtc ?: at.minus(FLIGHT_WINDOW_HALF).toString(),
            windowEndUtc = saved?.scope?.windowEndUtc ?: at.plus(FLIGHT_WINDOW_HALF).toString())
    }
    private suspend fun overlayLocal(owner: String, source: Feed, term: String, page: FlightPage, knownIds: Set<String>): FlightPage {
        val local = localItems(owner, source, term)
        val additions = local.filter { it.id !in knownIds }
        val items = if (page.page == 1) mergeItems(source, term, page.items, local) else page.items
        return page.copy(items = items, totalCount = page.totalCount + additions.size)
    }
    private suspend fun localItems(owner: String, source: Feed, term: String): List<FlightListItem> {
        val station = dao.station(owner)?.stationId ?: return emptyList()
        val rows = dao.interactions(owner).filter { !it.viewRevoked && it.stationId == station }
        return rows.mapNotNull { row ->
            val detail = decodeDetail(row.payloadJson)?.takeIf { it.isInFlightWindow(Instant.parse(now())) } ?: return@mapNotNull null
            if (source == Feed.Search && (detail.isPerLanding || detail.isAdHocFlight ||
                    if (term.isEmpty()) !row.usedSearch else detail.flightNumber.trim().uppercase(Locale.ROOT) != term)) return@mapNotNull null
            val card = detail.discoveryCard(row.interactionAtUtc)
            card.takeIf { source != Feed.History || term.isEmpty() || it.discoveryMatches(term) }
        }
    }
    private fun mergeItems(source: Feed, term: String, remote: List<FlightListItem>, local: List<FlightListItem>): List<FlightListItem> {
        val byId = remote.associateByTo(linkedMapOf()) { it.id }
        for (item in local) {
            val previous = byId[item.id]
            if (previous == null || instant(item.lastInteractionAtUtc) >= instant(previous.lastInteractionAtUtc)) byId[item.id] = item
        }
        return if (source == Feed.History || term.isEmpty()) byId.values.sortedWith(
            compareByDescending<FlightListItem> { instant(it.lastInteractionAtUtc) }.thenBy { it.id }) else byId.values.toList()
    }
    private suspend fun markError(read: Read, message: String) {
        transaction {
            if (isCurrent(read)) {
                val row = dao.query(read.owner, read.source.key, read.term)
                    ?: FlightDiscoveryQueryEntity(read.owner, read.source.key, read.term)
                dao.putQuery(row.copy(lastAttemptUtc = now(), error = message))
            }
        }
    }
    private suspend fun handleAccessError(owner: String, error: Exception, source: Feed): Boolean {
        if (error !is ApiException) return false
        if (error.statusCode == 401) {
            revokeForOwner(owner)
            return true
        }
        if (error.statusCode == 403 || error.statusCode == 404) {
            transaction {
                if (owner != currentOwner()) return@transaction
                val family = accessFamily(source)
                synchronized(readGate) {
                    for (key in epochs.keys.toList()) if (family.any { key.startsWith("$owner\u0000${it.key}\u0000") }) {
                        epochs[key] = checkNotNull(epochs[key]) + 1
                        staging.remove(key)
                    }
                }
                val rows = dao.queries(owner).filter { row -> family.any { it.key == row.source } }
                for (row in rows) dao.putQuery(row.copy(payloadJson = null, denied = true, lastAttemptUtc = now(),
                    lastSuccessUtc = null, accessStatus = error.statusCode, error = "This flight source is unavailable for this account."))
                pruneDetailsLocked(owner)
            }
            return true
        }
        return false
    }
    private fun decode(row: FlightDiscoveryQueryEntity): DiscoverySnapshot? = manifest(row)?.snapshot
    private fun manifest(row: FlightDiscoveryQueryEntity): DecodedManifest? {
        val raw = row.payloadJson ?: return null
        val key = "${row.ownerSubject}\u0000${row.source}\u0000${row.query}"
        return synchronized(manifestGate) {
            manifests[key]?.takeIf { it.raw == raw }?.let { return@synchronized it.value }
            val decoded = runCatching {
                val value = json.decodeFromString<DiscoverySnapshot>(raw)
                check(value.version == 1 && value.scope.isValid())
                check(value.items.all { it.stationId == value.scope.stationId && it.isInFlightWindow(parseFlightInstant(value.scope.asOfUtc)) })
                DecodedManifest(value, value.items.associateBy { it.id }, value.items.groupBy { it.flightNumber.trim().uppercase(Locale.ROOT) })
            }.getOrNull()
            manifests[key] = ManifestDecode(raw, decoded)
            decoded
        }
    }
    /** The private complete register cannot be replaced by UI paging; denial/session/ACK guards still apply. */
    private suspend fun authorizedForPreparation(owner: String, id: String, read: Read?, membership: Set<String>?): Boolean {
        if (read == null || membership == null) return authorized(owner, id)
        ensureCurrent(read)
        if (read.owner != owner || dao.sourceDenied(owner, Feed.OfflineSearch.key)) return false
        val denied = synchronized(readGate) { deniedFlightVersions["$owner/$id"] }
        return id in membership && (denied == null || denied <= read.acknowledgments)
    }
    private data class DecodedManifest(val snapshot: DiscoverySnapshot, val byId: Map<String, FlightListItem>,
        val byNumber: Map<String, List<FlightListItem>>)
    private data class ManifestDecode(val raw: String, val value: DecodedManifest?)
    private fun decodeDetail(raw: String): FlightDetail? = runCatching { json.decodeFromString<FlightDetail>(raw) }.getOrNull()
    private fun normalize(source: Feed, term: String) = term.trim().let {
        if (source == Feed.Search) it.uppercase(Locale.ROOT) else it.lowercase(Locale.ROOT)
    }
    private fun validatePage(page: FlightPage, source: Feed, term: String, number: Int, size: Int): FlightScope {
        val scope = page.scopeOrNull() ?: throw IOException("Flight scope metadata is missing.")
        if (page.page != number || page.pageSize !in 1..size || page.totalCount < 0 || page.items.size > page.pageSize ||
            page.items.any { it.stationId != scope.stationId || !it.isInFlightWindow(parseFlightInstant(scope.asOfUtc)) ||
                (source == Feed.Search || source == Feed.OfflineSearch) && (it.isPerLanding || it.isAdHocFlight || it.isMyAdHocFlight ||
                    source == Feed.OfflineSearch && it.isMyFlight || source == Feed.Search && term.isNotEmpty() && it.flightNumber.trim().uppercase(Locale.ROOT) != term) }) throw IOException("The flight query is outside its bounded source.")
        return scope
    }
    private suspend fun ensureReadScope(read: Read, station: String) {
        check(read.owner == currentOwner() && read.generation == generation && dao.station(read.owner)?.stationId == station) {
            "Your flight scope changed during offline preparation."
        }
    }
    private suspend fun isCurrent(read: Read) = read.owner == currentOwner() && read.generation == generation &&
        synchronized(readGate) { epochs[read.key] == read.epoch }
    private suspend fun ensureCurrent(read: Read) { check(isCurrent(read)) { "The flight query or account changed. Reload the page." } }
    private data class ResolvedDetail(val detail: FlightDetail, val revision: Long, val restoreLocal: Boolean = false)
    private data class Read(val owner: String, val source: Feed, val term: String, val key: String,
        val generation: Long, val stationRevision: Long, val epoch: Long, val acknowledgments: Long, val external: FlightExternalScopeRead?, val logos: LogoMetadataRead?)
    private data class StagedPages(val total: Long, val scope: FlightScope, val pages: MutableMap<Int, FlightPage> = sortedMapOf()) {
        fun ids() = pages.values.flatMap { it.items }.mapTo(mutableSetOf()) { it.id }
    }
    private enum class Feed(val key: String, val publicSource: FlightDiscoverySource? = null) {
        OfflineSearch("search-offline"), Search("search", FlightDiscoverySource.Search), History("history", FlightDiscoverySource.History);
        companion object { fun from(source: FlightDiscoverySource) = entries.first { it.publicSource == source } }
    }
    private fun accessFamily(source: Feed) = if (source == Feed.History) setOf(source) else setOf(Feed.Search, Feed.OfflineSearch)
    private suspend fun fetchRemotePage(source: Feed, term: String, page: Int, size: Int): FlightPage =
        if (source == Feed.OfflineSearch) checkNotNull(fetchOfflinePage) { "Offline flight search is unavailable." }(page, size)
        else fetchPage(checkNotNull(source.publicSource), term, page, size)

    private companion object { const val SYNC_PAGE_SIZE = 100 }
}

@Serializable
private data class DiscoverySnapshot(val version: Int, val scope: FlightScope, val items: List<FlightListItem>)

private fun Exception.isDiscoveryTransient() = this is IOException || this is HttpRequestTimeoutException || this is SerializationException ||
    this is ApiException && (statusCode >= 500 || statusCode in listOf(408, 429))
private fun instant(raw: String?) = raw?.let { runCatching { parseFlightInstant(it) }.getOrNull() } ?: Instant.MIN
private fun FlightDetail.discoveryCard(interaction: String) = FlightListItem(id, flightNumber, customerIataCode, customerName,
    stationIata, operationTypeName, scheduledArrivalUtc, scheduledDepartureUtc, isPerLanding, aircraftTypeModel, customerId,
    stationId = stationId, stationTimeZoneId = stationTimeZoneId, isAdHocFlight = isAdHocFlight,
    createdByUserId = createdByUserId, createdByDisplayName = createdByDisplayName,
    hasArrivalSubmission = arrivalSubmissions.isNotEmpty(), hasDepartureSubmission = departureSubmissions.isNotEmpty(),
    hasCancellationSubmission = hasCancellationSubmission || cancellationSubmissions.isNotEmpty(), lastInteractionAtUtc = interaction)
private fun FlightListItem.withAcknowledged(detail: FlightDetail) = copy(flightNumber = detail.flightNumber,
    customerId = detail.customerId, customerName = detail.customerName, customerIataCode = detail.customerIataCode,
    scheduledArrivalUtc = detail.scheduledArrivalUtc, scheduledDepartureUtc = detail.scheduledDepartureUtc,
    stationTimeZoneId = detail.stationTimeZoneId, hasArrivalSubmission = detail.arrivalSubmissions.isNotEmpty(),
    hasDepartureSubmission = detail.departureSubmissions.isNotEmpty(),
    hasCancellationSubmission = detail.hasCancellationSubmission || detail.cancellationSubmissions.isNotEmpty(),
    latestCancellationAtUtc = detail.cancellationSubmissions.maxByOrNull { instant(it.canceledAtUtc) }?.canceledAtUtc)
private fun FlightListItem.discoveryMatches(term: String): Boolean {
    val lower = term.lowercase(Locale.ROOT); val compact = lower.replace("-", "").replace(" ", "")
    return flightNumber.lowercase(Locale.ROOT).contains(lower) || customerName.lowercase(Locale.ROOT).contains(lower) ||
        ((customerIataCode.orEmpty() + flightNumber).lowercase(Locale.ROOT).replace("-", "").replace(" ", "").contains(compact)) ||
        id.lowercase(Locale.ROOT).replace("-", "") == compact
}
