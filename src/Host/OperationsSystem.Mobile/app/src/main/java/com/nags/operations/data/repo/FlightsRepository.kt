package com.nags.operations.data.repo

import com.nags.operations.data.ApiException
import com.nags.operations.data.AssignedEmployee
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.FlightListItem
import com.nags.operations.data.FlightPage
import com.nags.operations.data.FlightSyncStatus
import com.nags.operations.data.TokenStore
import com.nags.operations.data.api.HttpClientFactory
import com.nags.operations.data.api.OperationsApi
import com.nags.operations.data.db.CatalogDatabase
import com.nags.operations.data.db.dao.FlightCacheDao
import com.nags.operations.data.db.entities.FlightDetailCacheEntity
import com.nags.operations.data.db.entities.FlightSnapshotEntity
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
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.serialization.SerializationException
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

/** Scoped read-only snapshots; employee invitations always use the live API. */
@OptIn(ExperimentalCoroutinesApi::class)
class FlightsRepository internal constructor(
    private val fetchFlights: suspend (Int, Int, String?) -> FlightPage,
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
    private val fetchCustomerLogo: suspend (String) -> ByteArray? = { null },
) {
    constructor(api: OperationsApi, db: CatalogDatabase, tokenStore: TokenStore, online: StateFlow<Boolean>) : this(
        fetchFlights = api::flights,
        fetchFlight = api::flight,
        fetchInvitationOptions = api::invitationOptions,
        sendInvitation = api::invite,
        fetchCustomerLogo = api::customerLogo,
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
        FlightSyncStatus(snapshot?.itemCount ?: 0, snapshot?.lastAttemptUtc, snapshot?.lastSuccessUtc,
            snapshot?.error, snapshot?.permissionDenied ?: false, snapshot?.lastDurationMs)
    }.distinctUntilChanged()

    suspend fun flights(page: Int = 1, pageSize: Int = 20, search: String? = null): FlightPage {
        val session = requireSession()
        if (!isOnline()) return cachedPage(session, page, pageSize, search)
        val revision = listRevision
        try {
            val result = fetchFlights(page, pageSize, search)
            ensureCurrent(session)
            return if (revision == listRevision) result else cachedPage(session, page, pageSize, search)
        } catch (error: CancellationException) {
            throw error
        } catch (error: Exception) {
            handleAccessError(session, error)
            if (!error.isTransient()) throw error
            return cachedPageOrNull(session, page, pageSize, search) ?: throw error
        }
    }

    suspend fun flight(id: String): FlightDetail {
        val session = requireSession()
        if (!isOnline()) return cachedDetail(session, id)
        return refreshMutex.withLock {
            ensureCurrent(session)
            try {
                val detail = fetchFlight(id)
                mutationMutex.withLock {
                    ensureCurrent(session)
                    dao.putDetails(listOf(FlightDetailCacheEntity(session.owner, id, json.encodeToString(detail))))
                }
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

    /** Logo absence never changes a flight's visibility or persisted planning data. */
    suspend fun customerLogo(customerId: String): ByteArray? {
        val session = requireSession()
        if (!isOnline()) return null
        ensureCurrent(session)
        val bytes = fetchCustomerLogo(customerId)
        ensureCurrent(session)
        return bytes
    }

    suspend fun invitationOptions(id: String): List<AssignedEmployee> {
        requireOnlineInvitations()
        return fetchInvitationOptions(id)
    }

    suspend fun invite(id: String, staffMemberIds: List<String>, rowVersion: String) {
        requireOnlineInvitations()
        sendInvitation(id, staffMemberIds, rowVersion)
    }

    /** Replaces only a complete scoped list; partial downloads never become an offline snapshot. */
    suspend fun refreshCache() {
        if (!isOnline()) return
        refreshMutex.lock()
        _isSyncing.value = true
        try {
            if (!isOnline()) return
            val session = captureSession() ?: return
            val attemptedAt = now()
            val started = monotonicNanos()
            try {
                val items = readCompleteList(session).toMutableList()
                val visibleIds = items.mapTo(mutableSetOf()) { it.id }
                val previousDetails = mutationMutex.withLock {
                    ensureCurrent(session)
                    dao.details(session.owner).filter { it.flightId in visibleIds }
                }
                val details = mutableListOf<FlightDetailCacheEntity>()
                for (previous in previousDetails) {
                    ensureCurrent(session)
                    try {
                        val detail = fetchFlight(previous.flightId)
                        details += FlightDetailCacheEntity(session.owner, previous.flightId, json.encodeToString(detail))
                    } catch (error: ApiException) {
                        if (error.statusCode != 403 && error.statusCode != 404) throw error
                        // Persist known access loss even if a later detail fetch fails or is cancelled.
                        handleAccessError(session, error, previous.flightId, invalidatePending = false)
                        items.removeAll { it.id == previous.flightId }
                    }
                }
                mutationMutex.withLock {
                    ensureCurrent(session)
                    val duration = (monotonicNanos() - started).coerceAtLeast(0L) / 1_000_000L
                    dao.replace(FlightSnapshotEntity(session.owner, json.encodeToString(items), items.size,
                        attemptedAt, now(), lastDurationMs = duration), details)
                    listRevision++
                }
                _cacheChanges.tryEmit(Unit)
            } catch (error: CancellationException) {
                throw error
            } catch (error: Exception) {
                val handled = handleAccessError(session, error)
                if (!handled) mutationMutex.withLock {
                    if (!isCurrent(session)) return
                    val previous = dao.snapshot(session.owner) ?: FlightSnapshotEntity(session.owner)
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
        // An explicit session purge also clears a live-only UI that never persisted a snapshot.
        _cacheChanges.tryEmit(Unit)
    }

    private suspend fun readCompleteList(session: FlightCacheSession): List<FlightListItem> {
        val items = linkedMapOf<String, FlightListItem>()
        var pageNumber = 1
        var expectedTotal: Long? = null
        while (true) {
            ensureCurrent(session)
            val page = fetchFlights(pageNumber, SYNC_PAGE_SIZE, null)
            if (page.page != pageNumber || page.pageSize !in 1..SYNC_PAGE_SIZE || page.totalCount < 0 ||
                (expectedTotal != null && page.totalCount != expectedTotal)) {
                throw IOException("Flight list changed during sync; retry.")
            }
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
        return items.values.toList()
    }

    private suspend fun cachedPage(session: FlightCacheSession, page: Int, size: Int, search: String?): FlightPage =
        cachedPageOrNull(session, page, size, search)
            ?: throw IllegalStateException("Connect to download the flight list before viewing it offline.")

    private suspend fun cachedPageOrNull(session: FlightCacheSession, page: Int, size: Int, search: String?): FlightPage? =
        mutationMutex.withLock {
            ensureCurrent(session)
            val snapshot = dao.snapshot(session.owner)?.takeIf { it.lastSuccessUtc != null && !it.permissionDenied }
                ?: return@withLock null
            val term = search?.trim()?.lowercase(Locale.ROOT)?.takeIf(String::isNotEmpty)
            val filtered = json.decodeFromString<List<FlightListItem>>(snapshot.payloadJson)
                .filter { term == null || it.matchesSearch(term) }
            val normalizedPage = page.coerceAtLeast(1)
            val pageSize = size.coerceIn(1, SYNC_PAGE_SIZE)
            val totalPages = if (filtered.isEmpty()) 0 else ((filtered.size - 1) / pageSize) + 1
            val skip = (normalizedPage.toLong() - 1L) * pageSize
            FlightPage(if (skip >= filtered.size) emptyList() else filtered.drop(skip.toInt()).take(pageSize),
                normalizedPage, pageSize, filtered.size.toLong(), totalPages,
                normalizedPage > 1, normalizedPage < totalPages, fromCache = true)
        }

    private suspend fun cachedDetail(session: FlightCacheSession, id: String): FlightDetail =
        cachedDetailOrNull(session, id)
            ?: throw IllegalStateException("Connect and open this flight once to save its details for offline viewing.")

    private suspend fun cachedDetailOrNull(session: FlightCacheSession, id: String): FlightDetail? = mutationMutex.withLock {
        ensureCurrent(session)
        dao.detail(session.owner, id)?.let { json.decodeFromString<FlightDetail>(it.payloadJson).copy(fromCache = true) }
    }

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
                dao.replace(FlightSnapshotEntity(session.owner, lastAttemptUtc = now(),
                    error = "Flight access is not permitted.", permissionDenied = true), emptyList())
                listRevision++
                hadCache
            }
            if (changed) _cacheChanges.tryEmit(Unit)
            return true
        }
        if (flightId != null && (error.statusCode == 403 || error.statusCode == 404)) {
            val changed = mutationMutex.withLock {
                if (!isCurrent(session)) return@withLock false
                // Full sync already holds the detail-fetch lock and may continue revalidating.
                if (invalidatePending) generation++
                val previous = dao.snapshot(session.owner) ?: FlightSnapshotEntity(session.owner)
                val originalItems = json.decodeFromString<List<FlightListItem>>(previous.payloadJson)
                val originalDetails = dao.details(session.owner)
                val retained = originalItems.filter { it.id != flightId }
                val retainedDetails = originalDetails.filter { it.flightId != flightId }
                val hadCache = retained.size != originalItems.size || retainedDetails.size != originalDetails.size
                if (hadCache) {
                    dao.replace(previous.copy(payloadJson = json.encodeToString(retained), itemCount = retained.size), retainedDetails)
                    listRevision++
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
        currentOwner()?.let { FlightCacheSession(it, generation) }
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
    }
}

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
        (iata != null && ((iata + "-" + flightNumber.lowercase(Locale.ROOT)).contains(term) ||
            (iata + flightNumber.lowercase(Locale.ROOT)).contains(compactTerm)))
}
