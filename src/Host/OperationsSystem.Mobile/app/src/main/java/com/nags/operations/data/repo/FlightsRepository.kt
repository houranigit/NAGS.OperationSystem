package com.nags.operations.data.repo

import com.nags.operations.data.ApiException
import com.nags.operations.data.AssignedEmployee
import com.nags.operations.data.FlightCategory
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.FlightDepartureSaved
import com.nags.operations.data.departureEditable
import com.nags.operations.data.FlightListItem
import com.nags.operations.data.FlightPage
import com.nags.operations.data.FlightScope
import com.nags.operations.data.FlightSyncStatus
import com.nags.operations.data.TokenStore
import com.nags.operations.data.api.HttpClientFactory
import com.nags.operations.data.api.OperationsApi
import com.nags.operations.data.db.CatalogDatabase
import com.nags.operations.data.db.dao.FlightCacheDao
import com.nags.operations.data.db.entities.FlightDetailCacheEntity
import com.nags.operations.data.db.entities.FlightSnapshotEntity
import com.nags.operations.data.isInFlightWindow
import com.nags.operations.data.matches
import com.nags.operations.data.parseFlightInstant
import com.nags.operations.data.scopeOrNull
import io.ktor.client.plugins.HttpRequestTimeoutException
import java.io.IOException
import java.time.Instant
import java.util.Locale
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.onStart
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.serialization.Serializable
import kotlinx.serialization.SerializationException
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

/** Scoped read-only snapshots; employee invitations always use the live API. */
@OptIn(ExperimentalCoroutinesApi::class)
class FlightsRepository internal constructor(
    private val fetchFlights: suspend (Int, Int, String?, FlightCategory?) -> FlightPage,
    private val fetchFlight: suspend (String) -> FlightDetail,
    private val fetchInvitationOptions: suspend (String) -> List<AssignedEmployee>,
    private val sendInvitation: suspend (String, List<String>, String) -> Unit,
    private val dao: FlightCacheDao,
    private val ownerFlow: Flow<String?>,
    private val currentOwner: suspend () -> String?,
    private val isOnline: () -> Boolean,
    private val now: () -> String = { Instant.now().toString() },
    private val json: Json = HttpClientFactory.json,
    private val monotonicNanos: () -> Long = System::nanoTime,
    private val logoStore: CustomerLogoStore? = null,
) {
    constructor(api: OperationsApi, db: CatalogDatabase, tokenStore: TokenStore, online: StateFlow<Boolean>, logoStore: CustomerLogoStore? = null) : this(
        fetchFlights = api::flights,
        fetchFlight = api::flight,
        fetchInvitationOptions = api::invitationOptions,
        sendInvitation = api::invite,
        logoStore = logoStore,
        dao = db.flightCacheDao(),
        ownerFlow = combine(tokenStore.accessTokenFlow, tokenStore.sessionSubjectFlow) { token, subject ->
            if (token.isNullOrBlank()) null else sharedCatalogOwnerKey(HttpClientFactory.baseUrl, subject)
        }.distinctUntilChanged(),
        currentOwner = {
            if (tokenStore.getAccessToken().isNullOrBlank()) null
            else sharedCatalogOwnerKey(HttpClientFactory.baseUrl, tokenStore.getSessionSubject())
        },
        isOnline = { online.value },
    )

    private val refreshMutex = Mutex()
    private val mutationMutex = Mutex()
    @Volatile private var generation = 0L
    @Volatile private var listRevision = 0L
    private val _isSyncing = MutableStateFlow(false)
    val isSyncing: StateFlow<Boolean> = _isSyncing.asStateFlow()
    private val _cacheChanges = MutableSharedFlow<Unit>(extraBufferCapacity = 1)
    val cacheChanges: Flow<Unit> = _cacheChanges.asSharedFlow()
    val syncStatus: Flow<FlightSyncStatus> = ownerFlow.flatMapLatest { owner ->
        if (owner == null) flowOf(null) else dao.observe(owner)
    }.map { snapshot ->
        val payload = snapshot?.let(::decodeSnapshot)
        if (snapshot == null || payload == null) FlightSyncStatus()
        else FlightSyncStatus(payload.items.count { it.isInFlightWindow(Instant.parse(now())) },
            snapshot.lastAttemptUtc, snapshot.lastSuccessUtc, snapshot.error,
            snapshot.permissionDenied, snapshot.lastDurationMs)
    }.distinctUntilChanged()

    /** Notification links use only the account's locally synced My Flights tab, without network reads. */
    val syncedMyFlightIds: Flow<Set<String>> = ownerFlow.flatMapLatest { owner ->
        if (owner == null) flowOf(emptySet())
        else dao.observe(owner).map { snapshot ->
            snapshot?.takeIf { it.ownerSubject == owner && it.lastSuccessUtc != null && !it.permissionDenied }
                ?.let(::decodeSnapshot)?.items
                ?.filter { it.matches(FlightCategory.MyFlights) && it.isInFlightWindow(Instant.parse(now())) }
                ?.mapTo(mutableSetOf()) { it.id.lowercase(Locale.ROOT) }.orEmpty()
        }.onStart { emit(emptySet()) }
    }.distinctUntilChanged()

    /** The complete eligible union is used by synchronization, never the portal register. */
    suspend fun flights(page: Int = 1, pageSize: Int = 20, search: String? = null): FlightPage =
        flightsPage(null, page, pageSize, search)

    suspend fun flights(category: FlightCategory, page: Int = 1, pageSize: Int = 20, search: String? = null): FlightPage =
        flightsPage(category, page, pageSize, search)

    private suspend fun flightsPage(category: FlightCategory?, page: Int, pageSize: Int, search: String?): FlightPage {
        val session = requireSession()
        var logoRead = logoStore?.beginMetadataRead()
        if (!isOnline()) return cachedPage(session, category, page, pageSize, search)
        val revision = listRevision
        try {
            val result = fetchFlights(page, pageSize, search, category)
            val scope = requireScope(result, category)
            val accepted = mutationMutex.withLock {
                ensureCurrent(session)
                if (revision != listRevision) return@withLock false
                if (acceptScopeLocked(session, scope)) logoRead = logoStore?.beginMetadataRead()
                true
            }
            if (!accepted) return cachedPage(session, category, page, pageSize, search)
            val bounded = result.copy(items = result.items.filter { it.isInFlightWindow(Instant.parse(now())) })
            reconcileLogos(bounded.items, logoRead)
            return bounded
        } catch (error: CancellationException) {
            throw error
        } catch (error: Exception) {
            handleAccessError(session, error)
            if (!error.isTransient()) throw error
            return cachedPageOrNull(session, category, page, pageSize, search) ?: throw error
        }
    }

    suspend fun flight(id: String): FlightDetail {
        val session = requireSession()
        if (!isOnline()) return cachedDetail(session, id)
        return refreshMutex.withLock {
            ensureCurrent(session)
            var reportsChanged = false
            try {
                val detail = fetchFlight(id)
                mutationMutex.withLock {
                    ensureCurrent(session)
                    val saved = scopedSnapshotLocked(session)
                    val scope = saved?.second?.scope ?: dao.snapshot(session.owner)?.let(::decodeSnapshot)?.scope
                    if (!detail.isInFlightWindow(Instant.parse(now())) || (scope != null && detail.stationId != scope.stationId)) {
                        throw ApiException(404, "This flight is outside your current flight window.")
                    }
                    // Only the complete scoped manifest authorizes an offline detail.
                    if (saved?.second?.items?.any { it.id == id } == true) {
                        dao.putDetails(listOf(FlightDetailCacheEntity(session.owner, id, json.encodeToString(detail))))
                        reportsChanged = updateSnapshotReportsLocked(session, id) { it.withReportFlags(detail) }
                    }
                }
                if (reportsChanged) _cacheChanges.tryEmit(Unit)
                detail
            } catch (error: CancellationException) {
                throw error
            } catch (error: Exception) {
                handleAccessError(session, error, id)
                if (!error.isTransient()) throw error
                cachedDetailOrNull(session, id) ?: throw error
            }
        }
    }

    /** Cache-only current evidence for mutually exclusive local submissions; never starts an HTTP read. */
    suspend fun cachedFlight(id: String): FlightDetail? = captureSession()?.let { cachedDetailOrNull(it, id) }

    suspend fun recordCancellation(id: String, saved: com.nags.operations.data.FlightCancellationSaved) {
        updateCancellationCache(id, saved.rowVersion, hasCancellation = true) { detail ->
            detail.cancellationSubmissions.filterNot { it.id == saved.submission.id } + saved.submission
        }
    }

    suspend fun recordCancellationReverted(id: String, saved: com.nags.operations.data.FlightCancellationReverted) {
        updateCancellationCache(id, saved.rowVersion, saved.cancellationSubmissions.isNotEmpty(), saved.cancellationSubmissions) {
            saved.cancellationSubmissions
        }
    }

    private suspend fun updateCancellationCache(id: String, rowVersion: String, hasCancellation: Boolean,
        confirmedSubmissions: List<com.nags.operations.data.FlightCancellationSubmission>? = null,
        submissions: (FlightDetail) -> List<com.nags.operations.data.FlightCancellationSubmission>) {
        val session = requireSession()
        refreshMutex.withLock {
            mutationMutex.withLock {
                ensureCurrent(session)
                var confirmedCancellations = confirmedSubmissions
                dao.detail(session.owner, id)?.let { cached ->
                    val detail = runCatching { json.decodeFromString<FlightDetail>(cached.payloadJson) }.getOrNull()
                    if (detail != null) {
                        val cancellations = submissions(detail)
                        confirmedCancellations = cancellations
                        val updated = detail.copy(cancellationSubmissions = cancellations,
                            hasCancellationSubmission = cancellations.isNotEmpty(), rowVersion = rowVersion)
                        dao.putDetails(listOf(cached.copy(payloadJson = json.encodeToString(updated))))
                    }
                }
                updateSnapshotReportsLocked(session, id) { it.copy(hasCancellationSubmission = hasCancellation,
                    latestCancellationAtUtc = confirmedCancellations?.latestCancellationAtUtc()) }
            }
        }
        _cacheChanges.tryEmit(Unit)
    }

    suspend fun recordArrival(id: String, saved: com.nags.operations.data.FlightArrivalSaved) {
        val session = requireSession()
        refreshMutex.withLock {
            mutationMutex.withLock {
                ensureCurrent(session)
                val cached = dao.detail(session.owner, id)
                val detail = cached?.let { runCatching { json.decodeFromString<FlightDetail>(it.payloadJson) }.getOrNull() }
                if (cached != null && detail != null) {
                    val updated = detail.copy(arrivalSubmissions = detail.arrivalSubmissions.filterNot {
                        it.id == saved.submission.id
                    } + saved.submission, rowVersion = saved.rowVersion)
                    dao.putDetails(listOf(cached.copy(payloadJson = json.encodeToString(updated))))
                }
                updateSnapshotReportsLocked(session, id) { it.copy(hasArrivalSubmission = true) }
            }
        }
        _cacheChanges.tryEmit(Unit)
    }

    /** Acknowledged departure updates the replaceable cache; pending intent lives in its own repository. */
    suspend fun recordDeparture(id: String, saved: FlightDepartureSaved) {
        val session = requireSession()
        refreshMutex.withLock {
            mutationMutex.withLock {
                ensureCurrent(session)
                dao.detail(session.owner, id)?.let { cached ->
                    val detail = runCatching { json.decodeFromString<FlightDetail>(cached.payloadJson) }.getOrNull()
                    if (detail != null) {
                        val updated = detail.copy(departureSubmissions = detail.departureSubmissions.filterNot {
                            it.id == saved.submission.id
                        } + saved.submission, rowVersion = saved.rowVersion)
                        dao.putDetails(listOf(cached.copy(payloadJson = json.encodeToString(updated))))
                    }
                }
                updateSnapshotReportsLocked(session, id) { it.copy(hasDepartureSubmission = true) }
            }
        }
        _cacheChanges.tryEmit(Unit)
    }

    /** Only existing scoped rows receive acknowledged metadata; their planning, membership and versions stay intact. */
    private suspend fun updateSnapshotReportsLocked(session: FlightCacheSession, id: String,
        change: (FlightListItem) -> FlightListItem): Boolean {
        val (snapshot, payload) = scopedSnapshotLocked(session) ?: return false
        val original = payload.items.firstOrNull { it.id == id } ?: return false
        val updated = change(original)
        if (updated == original) return false
        val items = payload.items.map { if (it.id == id) updated else it }
        dao.putSnapshot(snapshot.copy(payloadJson = json.encodeToString(payload.copy(items = items))))
        val presenceChanged = (original.hasArrivalSubmission == true) != (updated.hasArrivalSubmission == true) ||
            (original.hasDepartureSubmission == true) != (updated.hasDepartureSubmission == true) ||
            (original.hasCancellationSubmission == true) != (updated.hasCancellationSubmission == true)
        val reportSummaryChanged = presenceChanged || original.latestCancellationAtUtc != updated.latestCancellationAtUtc
        // A list response started before a report change must not undo its summary. Filling only
        // missing false metadata does not invalidate unrelated planning reads.
        if (reportSummaryChanged) listRevision++
        return reportSummaryChanged
    }

    fun customerLogo(customerId: String): Flow<ByteArray?> = logoStore?.observe(customerId) ?: flowOf(null)

    private suspend fun reconcileLogos(items: List<FlightListItem>, read: LogoMetadataRead?, complete: Boolean = false) {
        try {
            logoStore?.reconcileFlights(items.mapNotNull { item -> item.customerId?.let {
                it to item.customerLogoVersion
            } }.toMap(), read, complete)
        } catch (cancelled: CancellationException) { throw cancelled }
        catch (_: Exception) { /* A logo file failure doesn't invalidate flight planning data. */ }
    }

    suspend fun invitationOptions(id: String): List<AssignedEmployee> {
        requireOnlineInvitations()
        return fetchInvitationOptions(id)
    }

    suspend fun invite(id: String, staffMemberIds: List<String>, rowVersion: String) {
        requireOnlineInvitations()
        sendInvitation(id, staffMemberIds, rowVersion)
    }

    /** Removes expired rows and details even when the device is offline. */
    suspend fun pruneExpired() {
        val session = captureSession() ?: return
        mutationMutex.withLock {
            ensureCurrent(session)
            scopedSnapshotLocked(session)
        }
    }

    /** Replaces only a complete scoped list; partial downloads never become an offline snapshot. */
    suspend fun refreshCache() {
        if (!isOnline()) return
        refreshMutex.lock()
        _isSyncing.value = true
        try {
            if (!isOnline()) return
            val session = captureSession() ?: return
            var logoRead = logoStore?.beginMetadataRead()
            val attemptedAt = now()
            val started = monotonicNanos()
            val revision = listRevision
            try {
                val (downloaded, acceptedLogoRead) = readCompleteList(session, revision, logoRead)
                logoRead = acceptedLogoRead
                val downloadedScope = checkNotNull(downloaded.scope)
                val items = downloaded.items.filter { it.isInFlightWindow(Instant.parse(now())) }.toMutableList()
                val visibleIds = items.mapTo(mutableSetOf()) { it.id }
                val previousDetails = mutationMutex.withLock {
                    ensureCompatibleScope(session, revision, downloadedScope)
                    dao.details(session.owner).filter { it.flightId in visibleIds }
                }
                val details = mutableListOf<FlightDetailCacheEntity>()
                for (previous in previousDetails) {
                    ensureCurrent(session)
                    try {
                        val detail = fetchFlight(previous.flightId)
                        if (detail.stationId != downloadedScope.stationId || !detail.isInFlightWindow(Instant.parse(now()))) {
                            throw ApiException(404, "This flight is outside your current flight window.")
                        }
                        details += FlightDetailCacheEntity(session.owner, previous.flightId, json.encodeToString(detail))
                    } catch (error: ApiException) {
                        if (error.statusCode != 403 && error.statusCode != 404) throw error
                        // Persist known access loss even if a later detail fetch fails or is cancelled.
                        handleAccessError(session, error, previous.flightId, invalidatePending = false)
                        items.removeAll { it.id == previous.flightId }
                    }
                }
                mutationMutex.withLock {
                    ensureCompatibleScope(session, revision, downloadedScope)
                    val previousScope = dao.snapshot(session.owner)?.let(::decodeSnapshot)?.scope
                    if (previousScope != null && previousScope.stationId != downloadedScope.stationId) {
                        logoStore?.clearOwner(session.owner)
                        logoRead = logoStore?.beginMetadataRead()
                    }
                    val retained = items.filter { it.isInFlightWindow(Instant.parse(now())) }
                    val ids = retained.mapTo(mutableSetOf()) { it.id }
                    val duration = (monotonicNanos() - started).coerceAtLeast(0L) / 1_000_000L
                    dao.replace(FlightSnapshotEntity(session.owner, json.encodeToString(downloaded.copy(items = retained)), retained.size,
                        attemptedAt, now(), lastDurationMs = duration), details.filter { it.flightId in ids })
                    listRevision++
                    reconcileLogos(retained, logoRead, complete = true)
                }
                _cacheChanges.tryEmit(Unit)
            } catch (error: CancellationException) {
                throw error
            } catch (error: Exception) {
                val handled = handleAccessError(session, error)
                if (!handled) mutationMutex.withLock {
                    if (!isCurrent(session)) return
                    scopedSnapshotLocked(session)
                    val previous = dao.snapshot(session.owner) ?: emptySnapshot(session.owner)
                    dao.putSnapshot(previous.copy(lastAttemptUtc = attemptedAt,
                        error = "Could not refresh flights. Saved data is kept; retry when connected."))
                }
            }
        } finally {
            _isSyncing.value = false
            refreshMutex.unlock()
        }
    }

    /** Waiters and late responses cannot restore a previous session's flight data. */
    suspend fun clearLocal() {
        mutationMutex.withLock {
            generation++
            dao.clearAll()
            listRevision++
        }
        logoStore?.clearAll()
        _cacheChanges.tryEmit(Unit)
    }

    private suspend fun readCompleteList(
        session: FlightCacheSession,
        revision: Long,
        initialLogoRead: LogoMetadataRead?,
    ): Pair<ScopedFlightSnapshot, LogoMetadataRead?> {
        var logoRead = initialLogoRead
        val items = linkedMapOf<String, FlightListItem>()
        var pageNumber = 1
        var expectedTotal: Long? = null
        var scope: FlightScope? = null
        while (true) {
            ensureCurrent(session)
            val page = fetchFlights(pageNumber, SYNC_PAGE_SIZE, null, null)
            val incoming = requireScope(page, null)
            val stationChanged = mutationMutex.withLock {
                ensureCompatibleScope(session, revision, incoming)
                acceptScopeLocked(session, incoming).also { changed ->
                    if (changed) logoRead = logoStore?.beginMetadataRead()
                }
            }
            // New station metadata revokes old rows even if the remaining download fails.
            if (stationChanged) _cacheChanges.tryEmit(Unit)
            if (page.page != pageNumber || page.pageSize !in 1..SYNC_PAGE_SIZE || page.totalCount < 0 ||
                (expectedTotal != null && page.totalCount != expectedTotal) ||
                (scope != null && scope.stationId != incoming.stationId)) {
                throw IOException("Flight list changed during sync; retry.")
            }
            scope = incoming
            expectedTotal = page.totalCount
            for (item in page.items) {
                if (items.put(item.id, item) != null) throw IOException("Flight pages overlap; retry.")
            }
            if (!page.hasNextPage) break
            if (page.items.isEmpty() || items.size.toLong() >= page.totalCount) {
                throw IOException("Flight pagination is incomplete; retry.")
            }
            pageNumber++
        }
        if (items.size.toLong() != expectedTotal) throw IOException("Flight pagination is incomplete; retry.")
        return ScopedFlightSnapshot(CACHE_SCOPE_VERSION, scope, items.values.toList()) to logoRead
    }

    /** Caller holds mutationMutex. Scope metadata can revoke old data without saving partial rows. */
    private suspend fun acceptScopeLocked(session: FlightCacheSession, incoming: FlightScope): Boolean {
        val previous = dao.snapshot(session.owner)
        val saved = previous?.let(::decodeSnapshot)
        if (saved?.scope?.stationId != null && saved.scope.stationId != incoming.stationId) {
            dao.replace(emptySnapshot(session.owner, incoming), emptyList())
            logoStore?.clearOwner(session.owner)
            listRevision++
            return true
        }
        if (previous?.lastSuccessUtc == null) {
            if (saved?.scope == null) listRevision++
            dao.putSnapshot(emptySnapshot(session.owner, incoming))
        }
        return false
    }

    /** Caller holds mutationMutex; a newer confirmed station wins over a pending full response. */
    private suspend fun ensureCompatibleScope(session: FlightCacheSession, revision: Long, incoming: FlightScope) {
        ensureCurrent(session)
        val current = dao.snapshot(session.owner)?.let(::decodeSnapshot)?.scope
        if (revision != listRevision && current != null && current.stationId != incoming.stationId) {
            throw IOException("Flight station changed during sync; retry.")
        }
    }

    private fun requireScope(page: FlightPage, category: FlightCategory?): FlightScope {
        val scope = page.scopeOrNull() ?: throw IOException("The mobile flight scope is missing or invalid.")
        val at = parseFlightInstant(scope.asOfUtc)
        if (page.items.any { it.stationId != scope.stationId || !it.matches(category) || !it.isInFlightWindow(at) }) {
            throw IOException("A flight is outside the mobile flight scope.")
        }
        return scope
    }

    private suspend fun cachedPage(session: FlightCacheSession, category: FlightCategory?, page: Int, size: Int, search: String?): FlightPage =
        cachedPageOrNull(session, category, page, size, search)
            ?: throw IllegalStateException("Connect to download the flight list before viewing it offline.")

    private suspend fun cachedPageOrNull(session: FlightCacheSession, category: FlightCategory?, page: Int, size: Int, search: String?): FlightPage? =
        mutationMutex.withLock {
            ensureCurrent(session)
            val (_, payload) = scopedSnapshotLocked(session) ?: return@withLock null
            val scope = payload.scope ?: return@withLock null
            val term = search?.trim()?.lowercase(Locale.ROOT)?.takeIf(String::isNotEmpty)
            val filtered = payload.items.filter { it.matches(category) && (term == null || it.matchesSearch(term)) }
            val normalizedPage = page.coerceAtLeast(1)
            val pageSize = size.coerceIn(1, SYNC_PAGE_SIZE)
            val totalPages = if (filtered.isEmpty()) 0 else ((filtered.size - 1) / pageSize) + 1
            val skip = (normalizedPage.toLong() - 1L) * pageSize
            val pageItems = if (skip >= filtered.size) emptyList() else filtered.drop(skip.toInt()).take(pageSize)
            // Older snapshots lacked report flags. Reuse only already-cached, eligible detail evidence.
            val missingIds = pageItems.filter { it.hasArrivalSubmission == null || it.hasDepartureSubmission == null ||
                it.hasCancellationSubmission == null }.mapTo(mutableSetOf()) { it.id }
            val knownDetails = if (missingIds.isEmpty()) emptyMap() else dao.details(session.owner)
                .filter { it.flightId in missingIds }.mapNotNull { entity ->
                    runCatching { json.decodeFromString<FlightDetail>(entity.payloadJson) }.getOrNull()
                        ?.takeIf { it.id == entity.flightId && it.stationId == scope.stationId && it.isInFlightWindow(Instant.parse(now())) }
                        ?.let { it.id to it }
                }.toMap()
            FlightPage(pageItems.map { item -> knownDetails[item.id]?.let { item.withMissingReportFlags(it) } ?: item },
                normalizedPage, pageSize, filtered.size.toLong(), totalPages,
                normalizedPage > 1, normalizedPage < totalPages, fromCache = true,
                stationId = scope.stationId, asOfUtc = scope.asOfUtc,
                windowStartUtc = scope.windowStartUtc, windowEndUtc = scope.windowEndUtc)
        }

    private suspend fun cachedDetail(session: FlightCacheSession, id: String): FlightDetail =
        cachedDetailOrNull(session, id)
            ?: throw IllegalStateException("Connect and open this flight once to save its details for offline viewing.")

    private suspend fun cachedDetailOrNull(session: FlightCacheSession, id: String): FlightDetail? = mutationMutex.withLock {
        ensureCurrent(session)
        val (_, payload) = scopedSnapshotLocked(session) ?: return@withLock null
        if (payload.items.none { it.id == id }) return@withLock null
        dao.detail(session.owner, id)?.let { entity ->
            runCatching { json.decodeFromString<FlightDetail>(entity.payloadJson) }.getOrNull()
                ?.takeIf { it.stationId == payload.scope?.stationId && it.isInFlightWindow(Instant.parse(now())) }
                ?.copy(fromCache = true)
        }
    }

    /** Caller holds mutationMutex. Expired cache data cannot be served or renew logo membership. */
    private suspend fun scopedSnapshotLocked(session: FlightCacheSession): Pair<FlightSnapshotEntity, ScopedFlightSnapshot>? {
        val snapshot = dao.snapshot(session.owner)?.takeIf { it.lastSuccessUtc != null && !it.permissionDenied } ?: return null
        val payload = decodeSnapshot(snapshot) ?: return null
        val retained = payload.items.filter { it.isInFlightWindow(Instant.parse(now())) }
        if (retained.size == payload.items.size) return snapshot to payload
        val ids = retained.mapTo(mutableSetOf()) { it.id }
        val updatedPayload = payload.copy(items = retained)
        val updated = snapshot.copy(payloadJson = json.encodeToString(updatedPayload), itemCount = retained.size)
        dao.replace(updated, dao.details(session.owner).filter { it.flightId in ids })
        val customers = retained.mapNotNull { it.customerId }.toSet()
        logoStore?.revokeCustomers(payload.items.mapNotNull { it.customerId }.toSet() - customers)
        listRevision++
        _cacheChanges.tryEmit(Unit)
        return updated to updatedPayload
    }

    private fun decodeSnapshot(snapshot: FlightSnapshotEntity): ScopedFlightSnapshot? = runCatching {
        val payload = json.decodeFromString<ScopedFlightSnapshot>(snapshot.payloadJson)
        check(payload.version == CACHE_SCOPE_VERSION)
        if (snapshot.lastSuccessUtc != null) {
            val scope = checkNotNull(payload.scope).also { check(it.isValid()) }
            check(payload.items.all { it.stationId == scope.stationId && it.matches(null) })
        } else check(payload.items.isEmpty())
        payload
    }.getOrNull()

    private fun emptySnapshot(owner: String, scope: FlightScope? = null): FlightSnapshotEntity =
        FlightSnapshotEntity(owner, payloadJson = json.encodeToString(ScopedFlightSnapshot(CACHE_SCOPE_VERSION, scope)))

    private suspend fun handleAccessError(
        session: FlightCacheSession,
        error: Exception,
        flightId: String? = null,
        invalidatePending: Boolean = true,
    ): Boolean {
        if (error !is ApiException) return false
        if (error.statusCode == 401) {
            val changed = mutationMutex.withLock {
                if (generation != session.generation) return@withLock false
                val activeOwner = currentOwner()
                if (activeOwner != null && activeOwner != session.owner) return@withLock false
                generation++
                val hadCache = dao.hasAnyCache()
                dao.clearAll()
                logoStore?.clearOwner(session.owner)
                listRevision++
                hadCache
            }
            if (changed) _cacheChanges.tryEmit(Unit)
            return true
        }
        if (error.statusCode == 403 && flightId == null) {
            val changed = mutationMutex.withLock {
                if (!isCurrent(session)) return@withLock false
                generation++
                val previous = dao.snapshot(session.owner)
                val hadCache = previous?.lastSuccessUtc != null || dao.details(session.owner).isNotEmpty()
                dao.replace(emptySnapshot(session.owner).copy(lastAttemptUtc = now(),
                    error = "Flight access is not permitted.", permissionDenied = true), emptyList())
                logoStore?.clearOwner(session.owner)
                listRevision++
                hadCache
            }
            if (changed) _cacheChanges.tryEmit(Unit)
            return true
        }
        if (flightId != null && (error.statusCode == 403 || error.statusCode == 404)) {
            val changed = mutationMutex.withLock {
                if (!isCurrent(session)) return@withLock false
                if (invalidatePending) generation++
                val previous = dao.snapshot(session.owner) ?: emptySnapshot(session.owner)
                val payload = decodeSnapshot(previous) ?: ScopedFlightSnapshot(CACHE_SCOPE_VERSION)
                val originalDetails = dao.details(session.owner)
                val retained = payload.items.filter { it.id != flightId }
                val retainedDetails = originalDetails.filter { it.flightId != flightId }
                val hadCache = retained.size != payload.items.size || retainedDetails.size != originalDetails.size
                if (hadCache) {
                    dao.replace(previous.copy(payloadJson = json.encodeToString(payload.copy(items = retained)), itemCount = retained.size), retainedDetails)
                    listRevision++
                    if (previous.lastSuccessUtc != null && invalidatePending) {
                        val stillVisible = retained.mapNotNull { it.customerId }.toSet()
                        val lostCustomers = payload.items.filter { it.id == flightId }.mapNotNull { it.customerId }.toSet() - stillVisible
                        logoStore?.revokeCustomers(lostCustomers)
                    }
                }
                hadCache
            }
            if (changed) _cacheChanges.tryEmit(Unit)
            return true
        }
        return false
    }

    private fun requireOnlineInvitations() {
        check(isOnline()) { "Connect to invite employees. Invitations cannot be saved offline." }
    }

    private suspend fun captureSession(): FlightCacheSession? = mutationMutex.withLock {
        val owner = currentOwner() ?: return@withLock null
        val previous = dao.snapshot(owner)
        // The former cache was the broad portal register. Never interpret it as the mobile scope.
        if (previous == null || decodeSnapshot(previous) == null) {
            generation++
            dao.replace(emptySnapshot(owner), emptyList())
            logoStore?.clearOwner(owner)
            listRevision++
        }
        FlightCacheSession(owner, generation)
    }
    private suspend fun requireSession(): FlightCacheSession = captureSession()
        ?: throw IllegalStateException("Sign in to view flights.")
    private suspend fun isCurrent(session: FlightCacheSession): Boolean =
        generation == session.generation && currentOwner() == session.owner
    private suspend fun ensureCurrent(session: FlightCacheSession) {
        check(isCurrent(session)) { "Flight access changed. Refresh the flight list." }
    }

    private companion object {
        const val SYNC_PAGE_SIZE = 100
        const val CACHE_SCOPE_VERSION = 1
    }
}

@Serializable
private data class ScopedFlightSnapshot(
    val version: Int,
    val scope: FlightScope? = null,
    val items: List<FlightListItem> = emptyList(),
)

private data class FlightCacheSession(val owner: String, val generation: Long)

private fun Exception.isTransient(): Boolean = this is IOException || this is HttpRequestTimeoutException ||
    this is SerializationException || (this is ApiException && (statusCode in 500..599 || statusCode == 408 || statusCode == 429))

/** Matches the current server's flight-number/customer/compact-IATA/flight-ID search. */
private fun FlightListItem.matchesSearch(term: String): Boolean {
    val compactTerm = term.replace("-", "").replace(" ", "")
    val idTerm = compactTerm.length == 32 && compactTerm.all { it in '0'..'9' || it in 'a'..'f' }
    val iata = customerIataCode?.lowercase(Locale.ROOT)
    return (idTerm && id.replace("-", "").lowercase(Locale.ROOT) == compactTerm) ||
        flightNumber.lowercase(Locale.ROOT).contains(term) || customerName.lowercase(Locale.ROOT).contains(term) ||
        (iata != null && (iata + flightNumber.lowercase(Locale.ROOT)).replace("-", "").replace(" ", "").contains(compactTerm))
}

/** Fresh detail metadata is authoritative for all three presence flags. */
private fun FlightListItem.withReportFlags(detail: FlightDetail) = copy(
    hasArrivalSubmission = detail.arrivalSubmissions.isNotEmpty(),
    hasDepartureSubmission = detail.departureSubmissions.isNotEmpty(),
    hasCancellationSubmission = detail.hasCancellationSubmission || detail.cancellationSubmissions.isNotEmpty(),
    latestCancellationAtUtc = detail.cancellationSubmissions.latestCancellationAtUtc(),
)

/** A known server false must not be replaced by an older cached detail's positive report. */
private fun FlightListItem.withMissingReportFlags(detail: FlightDetail) = copy(
    hasArrivalSubmission = hasArrivalSubmission ?: detail.arrivalSubmissions.isNotEmpty(),
    hasDepartureSubmission = hasDepartureSubmission ?: detail.departureSubmissions.isNotEmpty(),
    hasCancellationSubmission = hasCancellationSubmission ?: (detail.hasCancellationSubmission || detail.cancellationSubmissions.isNotEmpty()),
    latestCancellationAtUtc = latestCancellationAtUtc ?: if (hasCancellationSubmission == null)
        detail.cancellationSubmissions.latestCancellationAtUtc() else null,
)

private fun List<com.nags.operations.data.FlightCancellationSubmission>.latestCancellationAtUtc(): String? =
    mapNotNull { submission -> runCatching {
        parseFlightInstant(submission.canceledAtUtc) to submission.canceledAtUtc
    }.getOrNull() }.maxByOrNull { it.first }?.second
