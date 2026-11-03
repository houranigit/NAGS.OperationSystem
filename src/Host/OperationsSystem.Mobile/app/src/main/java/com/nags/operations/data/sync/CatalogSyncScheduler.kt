package com.nags.operations.data.sync

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

/** Serializes catalog refresh hints and keeps a periodic fallback for changes without broadcasts. */
class CatalogSyncScheduler(
    private val accessTokens: Flow<String?>,
    private val online: StateFlow<Boolean>,
    private val refreshCatalogs: suspend () -> Unit,
    private val clearCatalogs: suspend () -> Unit,
    private val appScope: CoroutineScope,
    private val pollIntervalMs: Long = 5L * 60L * 1_000L,
) {
    private val requests = Channel<Unit>(Channel.CONFLATED)
    private val lifecycleLock = Any()
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
                    if (session.token != null && session.online) requests.trySend(Unit)
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
                                try {
                                    refreshCatalogs()
                                } catch (error: CancellationException) {
                                    throw error
                                } catch (_: Exception) {
                                    // The repository retains per-catalog errors and successful rows.
                                    // A failed refresh must not stop later reconnect/manual retries.
                                }
                            }
                        }
                        while (true) {
                            delay(pollIntervalMs)
                            requests.trySend(Unit)
                        }
                    }
                }
        }
    }

    /** A burst of foreground/manual/live hints produces at most one pending refresh. */
    fun refreshNow() {
        synchronized(lifecycleLock) {
            if (enabled) requests.trySend(Unit)
        }
    }

    /** Wait for authenticated HTTP work to stop before logout or a different login is published. */
    suspend fun stop() {
        val job = synchronized(lifecycleLock) {
            enabled = false
            loop.also { loop = null }
        }
        job?.cancelAndJoin()
        while (requests.tryReceive().isSuccess) { }
    }
}

private data class CatalogSyncSession(val token: String?, val online: Boolean)
private data class CatalogSyncAvailability(val signedIn: Boolean, val online: Boolean)
