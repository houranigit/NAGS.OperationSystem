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
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.withContext
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
    private val prepareOffline: suspend (FlightDetail) -> Unit = {},
    private val localAdHocFlights: suspend () -> List<FlightDetail> = { emptyList() },
    private val unmaterializedAdHoc: suspend (String) -> Boolean = { false },
    private val reconcileAdHoc: suspend (Set<String>, Boolean) -> Unit = { _, _ -> },
    private val revokeAdHoc: suspend (String, String?) -> Unit = { _, _ -> },
    private val recordAdHoc: suspend (FlightDetail) -> Unit = {},
    private val confirmAdHocStation: suspend (String) -> Unit = {},
    private val additionalCachedFlight: suspend (String) -> FlightDetail? = { null },
    private val beginAdditionalDetailRead: suspend (String) -> Long = { 0L },
    private val recordAdditionalFetched: suspend (FlightDetail, Long) -> FlightDetail = { detail, _ -> detail },
    private val recordAdditionalAcknowledged: suspend (FlightDetail) -> Unit = {},
    private val prepareAdditionalOffline: suspend () -> Unit = {},
    private val confirmAdditionalStation: suspend (String) -> Unit = {},
    private val revokeAdditional: suspend (String, String?) -> Unit = { _, _ -> },
    private val additionalLogoFlights: suspend () -> List<FlightListItem> = { emptyList() },
) {
    constructor(api: OperationsApi, db: CatalogDatabase, tokenStore: TokenStore, online: StateFlow<Boolean>, logoStore: CustomerLogoStore? = null,
        prepareOffline: suspend (FlightDetail) -> Unit = {},
        localAdHocFlights: suspend () -> List<FlightDetail> = { emptyList() },
        unmaterializedAdHoc: suspend (String) -> Boolean = { false },
        reconcileAdHoc: suspend (Set<String>, Boolean) -> Unit = { _, _ -> },
        revokeAdHoc: suspend (String, String?) -> Unit = { _, _ -> }, recordAdHoc: suspend (FlightDetail) -> Unit = {},
        confirmAdHocStation: suspend (String) -> Unit = {},
        additionalCachedFlight: suspend (String) -> FlightDetail? = { null },
        beginAdditionalDetailRead: suspend (String) -> Long = { 0L },
        recordAdditionalFetched: suspend (FlightDetail, Long) -> FlightDetail = { detail, _ -> detail },
        recordAdditionalAcknowledged: suspend (FlightDetail) -> Unit = {}, prepareAdditionalOffline: suspend () -> Unit = {},
        confirmAdditionalStation: suspend (String) -> Unit = {}, revokeAdditional: suspend (String, String?) -> Unit = { _, _ -> },
        additionalLogoFlights: suspend () -> List<FlightListItem> = { emptyList() }) : this(
        fetchFlights = api::flights,
        fetchFlight = api::flight,
        fetchInvitationOptions = api::invitationOptions,
        sendInvitation = api::invite,
        logoStore = logoStore,
        prepareOffline = prepareOffline, localAdHocFlights = localAdHocFlights,
        unmaterializedAdHoc = unmaterializedAdHoc, reconcileAdHoc = reconcileAdHoc,
        revokeAdHoc = revokeAdHoc, recordAdHoc = recordAdHoc, confirmAdHocStation = confirmAdHocStation,
        additionalCachedFlight = additionalCachedFlight, beginAdditionalDetailRead = beginAdditionalDetailRead,
        recordAdditionalFetched = recordAdditionalFetched, recordAdditionalAcknowledged = recordAdditionalAcknowledged,
        prepareAdditionalOffline = prepareAdditionalOffline, confirmAdditionalStation = confirmAdditionalStation,
        revokeAdditional = revokeAdditional, additionalLogoFlights = additionalLogoFlights,
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
    private val deniedDuringRefresh = mutableSetOf<String>()
    @Volatile private var generation = 0L
    @Volatile private var listRevision = 0L
    private val _isSyncing = MutableStateFlow(false)
    val isSyncing: StateFlow<Boolean> = _isSyncing.asStateFlow()
    private val _cacheChanges = MutableSharedFlow<Unit>(extraBufferCapacity = 1)
    val cacheChanges: Flow<Unit> = _cacheChanges.asSharedFlow()
    fun notifyLocalChanges() { _cacheChanges.tryEmit(Unit) }
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
            return withLocalAdHoc(bounded, category, search)
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
        val local = localAdHocFlights().firstOrNull { it.id == id }
        if (!isOnline()) return local ?: cachedDetailOrNull(session, id) ?: additionalCachedFlight(id) ?: cachedDetail(session, id)
        if (local != null && unmaterializedAdHoc(id)) return local
        return refreshMutex.withLock {
            ensureCurrent(session)
            var reportsChanged = false
            try {
                val additionalRevision = beginAdditionalDetailRead(id)
                var detail = fetchFlight(id)
                if (detail.id != id) throw IOException("The flight response did not match its request.")
                mutationMutex.withLock {
                    ensureCurrent(session)
                    val saved = scopedSnapshotLocked(session)
                    val scope = saved?.second?.scope ?: dao.snapshot(session.owner)?.let(::decodeSnapshot)?.scope
                    if (!detail.isInFlightWindow(Instant.parse(now())) || (scope != null && detail.stationId != scope.stationId)) {
                        throw ApiException(404, "This flight is outside your current flight window.")
                    }
                    // A live bounded read establishes station authority, never orphan offline membership.
                    if (scope == null) {
                        confirmAdHocStation(detail.stationId)
                        confirmAdditionalStation(detail.stationId)
                    }
                    detail = recordAdditionalFetched(detail, additionalRevision)
                    // Only the complete scoped manifest authorizes a normal offline detail.
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
                cachedDetailOrNull(session, id) ?: localAdHocFlights().firstOrNull { it.id == id } ?: additionalCachedFlight(id) ?: throw error
            }
        }
    }

    /** A batch response replaces every operational section together, never one member at a time. */
    suspend fun recordOperationsBatch(detail: FlightDetail) {
        recordAdHoc(detail)
        recordAdditionalAcknowledged(detail)
        val session = requireSession()
        refreshMutex.withLock {
            mutationMutex.withLock {
                ensureCurrent(session)
                dao.detail(session.owner, detail.id)?.let { cached ->
                    dao.putDetails(listOf(cached.copy(payloadJson = json.encodeToString(detail))))
                }
                updateSnapshotReportsLocked(session, detail.id) { it.withReportFlags(detail) }
            }
        }
        _cacheChanges.tryEmit(Unit)
    }

    private suspend fun recordAdHocPartial(session: FlightCacheSession, id: String, change: (FlightDetail) -> FlightDetail) {
        ensureCurrent(session)
        val detail = localAdHocFlights().firstOrNull { it.id == id } ?: return
        ensureCurrent(session)
        recordAdHoc(change(detail).restrictCapabilitiesAfterKnownWork())
    }

    /** Cache-only current evidence for mutually exclusive local submissions; never starts an HTTP read. */
    suspend fun cachedFlight(id: String): FlightDetail? = captureSession()?.let { session ->
        cachedDetailOrNull(session, id) ?: localAdHocFlights().firstOrNull { it.id == id } ?: additionalCachedFlight(id)
    }

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
                            hasCancellationSubmission = cancellations.isNotEmpty(), rowVersion = rowVersion).restrictCapabilitiesAfterKnownWork()
                        dao.putDetails(listOf(cached.copy(payloadJson = json.encodeToString(updated))))
                    }
                }
                updateSnapshotReportsLocked(session, id) { it.copy(hasCancellationSubmission = hasCancellation,
                    latestCancellationAtUtc = confirmedCancellations?.latestCancellationAtUtc()) }
            }
        }
        recordAdHocPartial(session, id) { detail ->
            val cancellations = submissions(detail)
            detail.copy(cancellationSubmissions = cancellations, hasCancellationSubmission = cancellations.isNotEmpty(), rowVersion = rowVersion)
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
                    } + saved.submission, rowVersion = saved.rowVersion).restrictCapabilitiesAfterKnownWork()
                    dao.putDetails(listOf(cached.copy(payloadJson = json.encodeToString(updated))))
                }
                updateSnapshotReportsLocked(session, id) { it.copy(hasArrivalSubmission = true) }
            }
        }
        recordAdHocPartial(session, id) { detail -> detail.copy(arrivalSubmissions = detail.arrivalSubmissions.filterNot { it.id == saved.submission.id } + saved.submission, rowVersion = saved.rowVersion) }
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
                        } + saved.submission, rowVersion = saved.rowVersion).restrictCapabilitiesAfterKnownWork()
                        dao.putDetails(listOf(cached.copy(payloadJson = json.encodeToString(updated))))
                    }
                }
                updateSnapshotReportsLocked(session, id) { it.copy(hasDepartureSubmission = true) }
            }
        }
        recordAdHocPartial(session, id) { detail -> detail.copy(departureSubmissions = detail.departureSubmissions.filterNot { it.id == saved.submission.id } + saved.submission, rowVersion = saved.rowVersion) }
        _cacheChanges.tryEmit(Unit)
    }

    /** Service acknowledgments update only the exact record, retaining all other saved work. */
    suspend fun recordService(id: String, saved: com.nags.operations.data.FlightServiceSaved) {
        updateServiceCache(id, saved.rowVersion, saved.activeWorkCount, saved.completedWorkCount) { records ->
            records.filterNot { it.id == saved.service.id } + saved.service
        }
    }

    suspend fun recordServiceDeleted(id: String, saved: com.nags.operations.data.FlightServiceDeleted) {
        updateServiceCache(id, saved.rowVersion, saved.activeWorkCount, saved.completedWorkCount) { records ->
            records.filterNot { it.id == saved.id }
        }
    }

    private suspend fun updateServiceCache(id: String, rowVersion: String, activeWorkCount: Int?, completedWorkCount: Int?,
        change: (List<com.nags.operations.data.FlightServiceRecord>) -> List<com.nags.operations.data.FlightServiceRecord>) {
        val session = requireSession()
        refreshMutex.withLock {
            mutationMutex.withLock {
                ensureCurrent(session)
                dao.detail(session.owner, id)?.let { cached ->
                    val detail = runCatching { json.decodeFromString<FlightDetail>(cached.payloadJson) }.getOrNull()
                    if (detail != null) {
                        dao.putDetails(listOf(cached.copy(payloadJson = json.encodeToString(
                            detail.copy(services = change(detail.services), rowVersion = rowVersion).restrictCapabilitiesAfterKnownWork()))))
                    }
                }
                // Authoritative counts make a recovered acknowledgment idempotent, including after a crash.
                updateSnapshotReportsLocked(session, id) { it.copy(activeWorkCount = activeWorkCount,
                    completedWorkCount = completedWorkCount) }
            }
        }
        recordAdHocPartial(session, id) { detail -> detail.copy(services = change(detail.services), rowVersion = rowVersion) }
        _cacheChanges.tryEmit(Unit)
    }

    /** Task acknowledgments update only the exact record, retaining all other saved work. */
    suspend fun recordTask(id: String, saved: com.nags.operations.data.FlightTaskSaved) {
        updateTaskCache(id, saved.rowVersion, saved.activeWorkCount, saved.completedWorkCount) { records ->
            records.filterNot { it.id == saved.task.id } + saved.task
        }
    }

    suspend fun recordTaskDeleted(id: String, saved: com.nags.operations.data.FlightTaskDeleted) {
        updateTaskCache(id, saved.rowVersion, saved.activeWorkCount, saved.completedWorkCount) { records ->
            records.filterNot { it.id == saved.id }
        }
    }

    private suspend fun updateTaskCache(id: String, rowVersion: String, activeWorkCount: Int?, completedWorkCount: Int?,
        change: (List<com.nags.operations.data.FlightTaskRecord>) -> List<com.nags.operations.data.FlightTaskRecord>) {
        val session = requireSession()
        refreshMutex.withLock {
            mutationMutex.withLock {
                ensureCurrent(session)
                dao.detail(session.owner, id)?.let { cached ->
                    val detail = runCatching { json.decodeFromString<FlightDetail>(cached.payloadJson) }.getOrNull()
                    if (detail != null) {
                        dao.putDetails(listOf(cached.copy(payloadJson = json.encodeToString(
                            detail.copy(tasks = change(detail.tasks), rowVersion = rowVersion).restrictCapabilitiesAfterKnownWork()))))
                    }
                }
                // Authoritative counts make a recovered acknowledgment idempotent, including after a crash.
                updateSnapshotReportsLocked(session, id) { it.copy(activeWorkCount = activeWorkCount,
                    completedWorkCount = completedWorkCount) }
            }
        }
        recordAdHocPartial(session, id) { detail -> detail.copy(tasks = change(detail.tasks), rowVersion = rowVersion) }
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

    private suspend fun reconcileLogos(items: List<FlightListItem>, read: LogoMetadataRead?, complete: Boolean = false, prepare: Boolean = false) {
        try {
            val visible = if (complete) items + additionalLogoFlights() else items
            logoStore?.reconcileFlights(visible.mapNotNull { item -> item.customerId?.let {
                it to item.customerLogoVersion
            } }.toMap(), read, complete)
            if (prepare) logoStore?.prefetch(visible.mapNotNull { it.customerId }.toSet())
        } catch (cancelled: CancellationException) { throw cancelled }
        catch (error: Exception) { if (prepare) throw error /* Live cards can retain their existing icon fallback. */ }
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
            mutationMutex.withLock { deniedDuringRefresh.clear() }
            var logoRead = logoStore?.beginMetadataRead()
            val attemptedAt = now()
            val started = monotonicNanos()
            val revision = listRevision
            try {
                recordIncompleteAttempt(session, attemptedAt)
                val (downloaded, acceptedLogoRead) = readCompleteList(session, revision, logoRead)
                logoRead = acceptedLogoRead
                val downloadedScope = checkNotNull(downloaded.scope)
                val items = downloaded.items.filter { it.isInFlightWindow(Instant.parse(now())) }.toMutableList()
                val visibleIds = items.mapTo(mutableSetOf()) { it.id }
                reconcileAdHoc(visibleIds, false)
                mutationMutex.withLock {
                    ensureCompatibleScope(session, revision, downloadedScope)
                    revokeMissingFlightsLocked(session, visibleIds)
                }
                val details = mutableListOf<FlightDetailCacheEntity>()
                // A successful sync prepares every eligible flight, including flights never opened.
                // Downloads are sequential so media memory is bounded by the existing per-file limit.
                for (item in items.toList()) {
                    ensureCurrent(session)
                    val detail = try {
                        fetchFlight(item.id).also { detail ->
                            if (detail.id != item.id || detail.stationId != downloadedScope.stationId ||
                                !detail.isInFlightWindow(Instant.parse(now()))) {
                                throw ApiException(404, "This flight is outside your current flight window.")
                            }
                        }
                    } catch (error: ApiException) {
                        if (error.statusCode != 403 && error.statusCode != 404) throw error
                        // Persist known access loss even if a later download fails or is cancelled.
                        handleAccessError(session, error, item.id, invalidatePending = false)
                        items.removeAll { it.id == item.id }
                        continue
                    }
                    ensureCurrent(session)
                    // Missing/stale media and disk failures must not advance the successful-sync time.
                    prepareOffline(detail)
                    ensureCurrent(session)
                    details += FlightDetailCacheEntity(session.owner, item.id, json.encodeToString(detail))
                }
                prepareAdditionalOffline()
                mutationMutex.withLock {
                    ensureCompatibleScope(session, revision, downloadedScope)
                    val previousScope = dao.snapshot(session.owner)?.let(::decodeSnapshot)?.scope
                    if (previousScope != null && previousScope.stationId != downloadedScope.stationId) {
                        logoStore?.clearOwner(session.owner)
                        logoRead = logoStore?.beginMetadataRead()
                    }
                    val retained = items.filter { it.id !in deniedDuringRefresh && it.isInFlightWindow(Instant.parse(now())) }
                    val ids = retained.mapTo(mutableSetOf()) { it.id }
                    val duration = (monotonicNanos() - started).coerceAtLeast(0L) / 1_000_000L
                    reconcileLogos(retained, logoRead, complete = true, prepare = true)
                    dao.replace(FlightSnapshotEntity(session.owner, json.encodeToString(downloaded.copy(items = retained)), retained.size,
                        attemptedAt, now(), lastDurationMs = duration), details.filter { it.flightId in ids })
                    listRevision++
                }
                reconcileAdHoc(items.mapTo(mutableSetOf()) { it.id }, true)
                _cacheChanges.tryEmit(Unit)
            } catch (error: CancellationException) {
                withContext(NonCancellable) { recordIncompleteAttempt(session, attemptedAt) }
                throw error
            } catch (error: Exception) {
                if (!handleAccessError(session, error)) recordIncompleteAttempt(session, attemptedAt)
            }
        } finally {
            _isSyncing.value = false
            refreshMutex.unlock()
        }
    }

    private suspend fun recordIncompleteAttempt(session: FlightCacheSession, attemptedAt: String) = mutationMutex.withLock {
        if (!isCurrent(session)) return@withLock
        scopedSnapshotLocked(session)
        val previous = dao.snapshot(session.owner) ?: emptySnapshot(session.owner)
        dao.putSnapshot(previous.copy(lastAttemptUtc = attemptedAt,
            error = "Offline preparation is incomplete. Saved data is kept; retry when connected."))
    }

    /** A complete manifest revokes missing membership even if later details/media cannot be prepared. */
    private suspend fun revokeMissingFlightsLocked(session: FlightCacheSession, visibleIds: Set<String>) {
        val (snapshot, payload) = scopedSnapshotLocked(session) ?: return
        val retained = payload.items.filter { it.id in visibleIds }
        if (retained.size == payload.items.size) return
        dao.replace(snapshot.copy(payloadJson = json.encodeToString(payload.copy(items = retained)), itemCount = retained.size),
            dao.details(session.owner).filter { it.flightId in visibleIds })
        val customers = retained.mapNotNull { it.customerId }.toSet()
        logoStore?.revokeCustomers(payload.items.mapNotNull { it.customerId }.toSet() - customers)
        listRevision++
        _cacheChanges.tryEmit(Unit)
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
        confirmAdHocStation(incoming.stationId)
        confirmAdditionalStation(incoming.stationId)
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

    /** A denied Discovery detail revokes the same flight without restoring it from an older full-sync manifest. */
    suspend fun recordExternalAccessLoss(id: String, status: Int) {
        val session = captureSession() ?: return
        handleAccessError(session, ApiException(status, "This flight is no longer available."), id, invalidatePending = false)
    }

    /** Cache-only station proof for a v13 offline Save; safe inside the existing Room transaction. */
    suspend fun trustedCachedStation(): String? {
        val owner = currentOwner() ?: return null
        val observed = generation
        val snapshot = dao.snapshot(owner)?.takeUnless { it.permissionDenied } ?: return null
        val station = decodeSnapshot(snapshot)?.scope?.takeIf { it.isValid() }?.stationId
        return station?.takeIf { owner == currentOwner() && observed == generation }
    }

    /** Cross-source station reads share the normal cache's late-response and session generation guards. */
    suspend fun beginExternalScopeRead(): FlightExternalScopeRead? {
        val session = captureSession() ?: return null
        return mutationMutex.withLock {
            ensureCurrent(session)
            FlightExternalScopeRead(session.owner, session.generation, listRevision)
        }
    }

    suspend fun confirmExternalPage(page: FlightPage, read: FlightExternalScopeRead?) {
        val observed = checkNotNull(read) { "Your flight session changed. Reload the page." }
        val scope = page.scopeOrNull() ?: throw IOException("Flight scope metadata is missing.")
        if (page.items.any { it.stationId != scope.stationId || !it.isInFlightWindow(parseFlightInstant(scope.asOfUtc)) })
            throw IOException("A flight is outside its station scope.")
        val changed = mutationMutex.withLock {
            val session = FlightCacheSession(observed.owner, observed.generation)
            ensureCompatibleScope(session, observed.revision, scope)
            acceptScopeLocked(session, scope)
        }
        if (changed) _cacheChanges.tryEmit(Unit)
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

    private suspend fun cachedPageOrNull(session: FlightCacheSession, category: FlightCategory?, page: Int, size: Int, search: String?): FlightPage? {
        val cached = cachedSnapshotPageOrNull(session, category, page, size, search)
        if (cached != null) return withLocalAdHoc(cached, category, search)
        val local = localAdHocFlights().filter { category == null || category == FlightCategory.AdHoc }
        val station = local.firstOrNull()?.stationId ?: return null
        val at = Instant.parse(now())
        return withLocalAdHoc(FlightPage(emptyList(), page.coerceAtLeast(1), size.coerceIn(1, SYNC_PAGE_SIZE),
            fromCache = true, stationId = station, asOfUtc = now(),
            windowStartUtc = at.minusSeconds(12 * 60 * 60).toString(), windowEndUtc = at.plusSeconds(12 * 60 * 60).toString()), category, search)
    }

    private suspend fun withLocalAdHoc(page: FlightPage, category: FlightCategory?, search: String?): FlightPage {
        if (page.page != 1 || category != null && category != FlightCategory.AdHoc) return page
        val term = search?.trim()?.lowercase(Locale.ROOT)?.takeIf(String::isNotEmpty)
        val local = localAdHocFlights().filter { it.stationId == page.stationId }.map { detail ->
            FlightListItem(detail.id, detail.flightNumber, detail.customerIataCode, detail.customerName,
                detail.stationIata, detail.operationTypeName, detail.scheduledArrivalUtc, detail.scheduledDepartureUtc,
                false, detail.aircraftTypeModel, detail.customerId, stationId = detail.stationId,
                isMyAdHocFlight = true, isAdHocFlight = true, stationTimeZoneId = detail.stationTimeZoneId,
                createdByUserId = detail.createdByUserId, createdByDisplayName = detail.createdByDisplayName,
                hasArrivalSubmission = detail.arrivalSubmissions.isNotEmpty(), hasDepartureSubmission = detail.departureSubmissions.isNotEmpty(),
                hasCancellationSubmission = detail.hasCancellationSubmission)
        }.filter { term == null || it.matchesSearch(term) }
        val new = local.filter { row -> page.items.none { it.id == row.id } }
        return page.copy(items = new + page.items, totalCount = page.totalCount + new.size)
    }

    private suspend fun cachedSnapshotPageOrNull(session: FlightCacheSession, category: FlightCategory?, page: Int, size: Int, search: String?): FlightPage? =
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
            ?: throw IllegalStateException("Connect and synchronize flights to prepare this flight for offline viewing.")

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
        if ((error.statusCode == 401 || error.statusCode == 403 || error.statusCode == 404 && flightId != null) &&
            generation == session.generation) {
            val owner = currentOwner()
            if (owner == session.owner || owner == null && error.statusCode == 401) {
                val revokedId = if (error.statusCode == 401) null else flightId
                revokeAdHoc(session.owner, revokedId)
                revokeAdditional(session.owner, revokedId)
            }
        }
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
                if (_isSyncing.value) deniedDuringRefresh += flightId
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

/** Opaque read stamp; only repository-created stamps can confirm a newer station. */
data class FlightExternalScopeRead internal constructor(internal val owner: String, internal val generation: Long, internal val revision: Long)
