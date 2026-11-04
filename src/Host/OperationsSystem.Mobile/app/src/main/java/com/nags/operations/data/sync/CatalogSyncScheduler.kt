package com.nags.operations.data.sync

import com.nags.operations.data.SharedCatalog
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.onEach
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/** Serializes read-only cache refreshes and keeps a periodic fallback for missed broadcasts. */
class CatalogSyncScheduler(
    private val accessTokens: Flow<String?>,
    private val online: StateFlow<Boolean>,
    private val refreshCatalogs: suspend (Set<SharedCatalog>) -> Unit,
    private val clearCatalogs: suspend () -> Unit,
    private val appScope: CoroutineScope,
    private val pollIntervalMs: Long = 5L * 60L * 1_000L,
    private val refreshFlightCache: suspend () -> Unit = {},
) {
    private val requests = Channel<Unit>(Channel.CONFLATED)
    private val lifecycleLock = Any()
    private val pendingTargets = mutableSetOf<SyncTarget>()
    @Volatile private var enabled = false
    private var loop: Job? = null

    fun start() = synchronized(lifecycleLock) {
        if (loop?.isActive == true) return@synchronized
        enabled = true
        loop = appScope.launch {
            // Queue a recheck when the credential rotates, without cancelling an HTTP request
            // that may itself be publishing the new token from Ktor's refresh handler.
            combine(accessTokens, online) { token, connected -> CatalogSyncSession(token, connected) }
                .distinctUntilChanged()
                .onEach { session ->
                    if (session.token != null && session.online) refreshNow()
                }
                .map { session -> CatalogSyncAvailability(session.token != null, session.online) }
                .distinctUntilChanged()
                .collectLatest { session ->
                    if (!session.signedIn) {
                        // Finish the local purge even if an immediate re-login publishes a token.
                        // collectLatest waits for this before starting that session's refresh.
                        withContext(NonCancellable) { clearCatalogs() }
                        return@collectLatest
                    }
                    if (!session.online) return@collectLatest

                    coroutineScope {
                        launch {
                            for (request in requests) {
                                val targets = synchronized(lifecycleLock) {
                                    pendingTargets.toSet().also { pendingTargets.clear() }
                                }
                                val catalogs = targets.filterIsInstance<SyncTarget.Catalog>()
                                    .mapTo(mutableSetOf()) { it.catalog }
                                if (catalogs.isNotEmpty()) runRefresh { refreshCatalogs(catalogs) }
                                if (SyncTarget.Flights in targets) runRefresh { refreshFlightCache() }
                            }
                        }
                        while (true) {
                            delay(pollIntervalMs)
                            refreshNow()
                        }
                    }
                }
        }
    }

    /** Foreground/manual/session/reconnect refreshes recheck every read-only dataset. */
    fun refreshNow() = enqueue(SyncTarget.all)

    fun refreshCatalog(catalog: SharedCatalog) = enqueue(setOf(SyncTarget.Catalog(catalog)))

    fun refreshFlights() = enqueue(setOf(SyncTarget.Flights))

    /** Live hints merge into the pending batch without losing distinct datasets. */
    private fun enqueue(targets: Collection<SyncTarget>) {
        synchronized(lifecycleLock) {
            if (enabled) {
                pendingTargets.addAll(targets)
                requests.trySend(Unit)
            }
        }
    }

    private suspend fun runRefresh(refresh: suspend () -> Unit) {
        try {
            refresh()
        } catch (error: CancellationException) {
            throw error
        } catch (_: Exception) {
            // Each repository retains errors and successful snapshots. One failed dataset must
            // not stop the other pending datasets or later manual/reconnect retries.
        }
    }

    /** Wait for authenticated HTTP work to stop before logout or a different login is published. */
    suspend fun stop() {
        val job = synchronized(lifecycleLock) {
            enabled = false
            pendingTargets.clear()
            loop.also { loop = null }
        }
        job?.cancelAndJoin()
        while (requests.tryReceive().isSuccess) { }
    }
}

private data class CatalogSyncSession(val token: String?, val online: Boolean)
private data class CatalogSyncAvailability(val signedIn: Boolean, val online: Boolean)
