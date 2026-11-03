package com.nags.operations.data.repo

import com.nags.operations.data.AircraftTypeCatalogItem
import com.nags.operations.data.ApiException
import com.nags.operations.data.AtaChapterCatalogItem
import com.nags.operations.data.CatalogSyncStatus
import com.nags.operations.data.CustomerCatalogItem
import com.nags.operations.data.ResourceCatalogItem
import com.nags.operations.data.ServiceCatalogItem
import com.nags.operations.data.SharedCatalog
import com.nags.operations.data.TokenStore
import com.nags.operations.data.api.HttpClientFactory
import com.nags.operations.data.api.SharedCatalogsApi
import com.nags.operations.data.db.CatalogDatabase
import com.nags.operations.data.db.dao.SharedCatalogDao
import com.nags.operations.data.db.entities.SharedCatalogSnapshotEntity
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import java.time.Instant

/** Catalog refreshes never read or write flight/work data, and each snapshot commits atomically. */
@OptIn(ExperimentalCoroutinesApi::class)
class SharedCatalogsRepository internal constructor(
    private val fetchCatalog: suspend (SharedCatalog) -> String,
    private val dao: SharedCatalogDao,
    private val ownerFlow: Flow<String?>,
    private val currentOwner: suspend () -> String?,
    private val now: () -> String = { Instant.now().toString() },
    private val json: Json = HttpClientFactory.json,
    private val monotonicNanos: () -> Long = System::nanoTime,
) {
    constructor(api: SharedCatalogsApi, db: CatalogDatabase, tokenStore: TokenStore) : this(
        fetchCatalog = api::catalog,
        dao = db.sharedCatalogDao(),
        ownerFlow = combine(tokenStore.accessTokenFlow, tokenStore.sessionSubjectFlow) { token, subject ->
            if (token.isNullOrBlank()) null else sharedCatalogOwnerKey(HttpClientFactory.baseUrl, subject)
        }.distinctUntilChanged(),
        currentOwner = {
            if (tokenStore.getAccessToken().isNullOrBlank()) null
            else sharedCatalogOwnerKey(HttpClientFactory.baseUrl, tokenStore.getSessionSubject())
        },
    )

    private val refreshMutex = Mutex()
    private val mutationMutex = Mutex()
    @Volatile private var generation = 0L
    private val _isSyncing = MutableStateFlow(false)
    val isSyncing: StateFlow<Boolean> = _isSyncing.asStateFlow()

    private fun observeSnapshots(): Flow<List<SharedCatalogSnapshotEntity>> = ownerFlow.flatMapLatest { owner ->
        if (owner == null) flowOf(emptyList()) else dao.observe(owner)
    }

    fun observeStatus(): Flow<List<CatalogSyncStatus>> = observeSnapshots().map { snapshots ->
        val byCatalog = snapshots.associateBy { it.catalog }
        SharedCatalog.entries.map { kind ->
            val snapshot = byCatalog[kind.storageKey]
            CatalogSyncStatus(kind, snapshot?.itemCount ?: 0, snapshot?.lastAttemptUtc,
                snapshot?.lastSuccessUtc, snapshot?.error, snapshot?.permissionDenied ?: false,
                snapshot?.lastDurationMs)
        }
    }

    fun aircraftTypesFlow(): Flow<List<AircraftTypeCatalogItem>> = catalogFlow(SharedCatalog.AircraftTypes)
    fun customersFlow(): Flow<List<CustomerCatalogItem>> = catalogFlow(SharedCatalog.Customers)
    fun servicesFlow(): Flow<List<ServiceCatalogItem>> = catalogFlow(SharedCatalog.Services)
    fun toolsFlow(): Flow<List<ResourceCatalogItem>> = catalogFlow(SharedCatalog.Tools)
    fun materialsFlow(): Flow<List<ResourceCatalogItem>> = catalogFlow(SharedCatalog.Materials)
    fun generalSupportsFlow(): Flow<List<ResourceCatalogItem>> = catalogFlow(SharedCatalog.GeneralSupports)
    fun ataChaptersFlow(): Flow<List<AtaChapterCatalogItem>> = catalogFlow(SharedCatalog.AtaChapters)

    private inline fun <reified T> catalogFlow(kind: SharedCatalog): Flow<List<T>> = observeSnapshots().map { snapshots ->
        snapshots.firstOrNull { it.catalog == kind.storageKey && !it.permissionDenied }
            ?.let { json.decodeFromString<List<T>>(it.payloadJson) }.orEmpty()
    }

    /** Every catalog has an independent outcome; a failed fetch never replaces a good snapshot. */
    suspend fun refresh() {
        if (!refreshMutex.tryLock()) return
        _isSyncing.value = true
        try {
            val owner = currentOwner() ?: return
            val expectedGeneration = mutationMutex.withLock { generation }
            for (kind in SharedCatalog.entries) {
                if (!isCurrent(owner, expectedGeneration)) return
                val attemptedAt = now()
                val startedNanos = monotonicNanos()
                try {
                    val (payload, count) = normalizedSnapshot(kind, fetchCatalog(kind))
                    val elapsedMs = ((monotonicNanos() - startedNanos).coerceAtLeast(0L)) / 1_000_000L
                    mutationMutex.withLock {
                        if (!isCurrent(owner, expectedGeneration)) return
                        dao.put(SharedCatalogSnapshotEntity(owner, kind.storageKey, payload, count,
                            attemptedAt, now(), lastDurationMs = elapsedMs))
                    }
                } catch (error: CancellationException) {
                    throw error
                } catch (error: Exception) {
                    mutationMutex.withLock {
                        if (generation != expectedGeneration) return
                        if (error is ApiException && error.statusCode == 401) {
                            // The HTTP refresh handler may already have removed invalid tokens.
                            // Do not let that hide the need to clear this account's persisted cache.
                            val activeOwner = currentOwner()
                            if (activeOwner == null || activeOwner == owner) {
                                generation++
                                dao.clearAll()
                            }
                            return
                        }
                        if (!isCurrent(owner, expectedGeneration)) return
                        val denied = error is ApiException && error.statusCode == 403
                        val previous = dao.snapshot(owner, kind.storageKey)
                        val retained = if (denied) SharedCatalogSnapshotEntity(owner, kind.storageKey)
                            else previous ?: SharedCatalogSnapshotEntity(owner, kind.storageKey)
                        dao.put(retained.copy(lastAttemptUtc = attemptedAt,
                            error = if (denied) "Catalog access is not permitted." else "Catalog refresh failed. Retry when connected.",
                            permissionDenied = denied))
                    }
                }
            }
        } finally {
            _isSyncing.value = false
            refreshMutex.unlock()
        }
    }

    /** Also invalidates in-flight responses, including non-cooperative late network completions. */
    suspend fun clearLocal() {
        mutationMutex.withLock {
            generation++
            dao.clearAll()
        }
    }

    private suspend fun isCurrent(owner: String, expectedGeneration: Long): Boolean =
        generation == expectedGeneration && currentOwner() == owner

    private fun normalizedSnapshot(kind: SharedCatalog, payload: String): Pair<String, Int> = when (kind) {
        SharedCatalog.AircraftTypes -> json.decodeFromString<List<AircraftTypeCatalogItem>>(payload)
            .let { json.encodeToString(it) to it.size }
        SharedCatalog.Customers -> json.decodeFromString<List<CustomerCatalogItem>>(payload)
            .let { json.encodeToString(it) to it.size }
        SharedCatalog.Services -> json.decodeFromString<List<ServiceCatalogItem>>(payload)
            .let { json.encodeToString(it) to it.size }
        SharedCatalog.Tools, SharedCatalog.Materials, SharedCatalog.GeneralSupports ->
            json.decodeFromString<List<ResourceCatalogItem>>(payload).let { json.encodeToString(it) to it.size }
        SharedCatalog.AtaChapters -> json.decodeFromString<List<AtaChapterCatalogItem>>(payload)
            .let { json.encodeToString(it) to it.size }
    }
}

/** Copied account IDs across API environments must not share offline reference snapshots. */
internal fun sharedCatalogOwnerKey(baseUrl: String, subject: String?): String? =
    subject?.takeIf(String::isNotBlank)?.let { "${baseUrl.trim().trimEnd('/')}/|$it" }
