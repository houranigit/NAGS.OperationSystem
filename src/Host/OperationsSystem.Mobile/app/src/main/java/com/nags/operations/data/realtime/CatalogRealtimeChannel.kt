package com.nags.operations.data.realtime

import android.util.Log
import com.microsoft.signalr.HubConnection
import com.microsoft.signalr.HubConnectionBuilder
import com.microsoft.signalr.HubConnectionState
import com.nags.operations.data.sync.SyncTarget
import com.nags.operations.data.api.HttpClientFactory
import io.reactivex.rxjava3.core.Completable
import io.reactivex.rxjava3.core.Single
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException

/** Invalidation hints for read-only flights and catalogs; retired mutation messages stay ignored. */
class CatalogRealtimeChannel(
    private val accessTokens: Flow<String?>,
    private val online: StateFlow<Boolean>,
    private val requestRefresh: (SyncTarget) -> Unit,
    private val requestFullRefresh: () -> Unit,
    private val appScope: CoroutineScope,
) {
    private val lifecycleLock = Any()
    private var loop: Job? = null
    @Volatile private var enabled = false
    private val diagnostics = CatalogRealtimeDiagnostics()
    val state: StateFlow<RealtimeChannelState> = diagnostics.state
    val lastEventAt: StateFlow<Long?> = diagnostics.lastEventAt

    fun start() = synchronized(lifecycleLock) {
        if (loop?.isActive == true) return@synchronized
        enabled = true
        loop = appScope.launch {
            combine(accessTokens, online) { token, connected -> token to connected }
                .distinctUntilChanged()
                .collectLatest { (token, connected) ->
                    // Going offline retains the last observed event for diagnostics. Ending the
                    // authenticated session removes that account's timestamp before a new login.
                    diagnostics.disconnect(clearLastEvent = token == null)
                    if (token == null || !connected) return@collectLatest
                    connectWhileActive(token)
                }
        }
    }

    suspend fun stop() {
        val job = synchronized(lifecycleLock) {
            enabled = false
            diagnostics.disconnect(clearLastEvent = true)
            loop.also { loop = null }
        }
        job?.cancelAndJoin()
    }

    private suspend fun connectWhileActive(token: String) {
        var attempt = 0
        while (enabled && currentCoroutineContext().isActive) {
            val attemptId = synchronized(lifecycleLock) {
                if (!enabled) return
                diagnostics.beginAttempt(reconnecting = attempt > 0)
            }
            val connection = HubConnectionBuilder
                .create(HttpClientFactory.url("hubs/mobile-sync"))
                .withAccessTokenProvider(Single.defer { Single.just(token) })
                .build()
            try {
                connection.on("change", { change ->
                    if (enabled) {
                        diagnostics.acceptChange(attemptId, change.table, System.currentTimeMillis())
                            ?.let(requestRefresh)
                    }
                }, CatalogChangeEnvelope::class.java)
                connection.start().awaitCompletion()
                attempt = 0
                // The retained hub has no replay feed. Reload server snapshots on every connect
                // so disconnect gaps do not require the retired Operations catch-up endpoint.
                if (enabled && diagnostics.connected(attemptId)) requestFullRefresh()
                awaitClosure(connection)
                diagnostics.reconnecting(attemptId)
            } catch (error: CancellationException) {
                diagnostics.disconnect(attemptId)
                throw error
            } catch (_: Exception) {
                diagnostics.reconnecting(attemptId)
                Log.w(TAG, "Catalog live channel unavailable; retrying")
            } finally {
                withContext(NonCancellable) {
                    withTimeoutOrNull(5_000L) {
                        runCatching { connection.stop().awaitCompletion() }
                    }
                }
            }
            attempt++
            delay((1_000L shl (attempt - 1).coerceIn(0, 5)).coerceAtMost(30_000L))
        }
    }

    private suspend fun awaitClosure(connection: HubConnection): Unit =
        suspendCancellableCoroutine { continuation ->
            val completed = AtomicBoolean(false)
            fun finish() {
                if (completed.compareAndSet(false, true) && continuation.isActive) continuation.resume(Unit)
            }
            connection.onClosed { finish() }
            if (connection.connectionState == HubConnectionState.DISCONNECTED) finish()
        }

    private companion object {
        const val TAG = "CatalogRealtime"
    }
}

/** Coarse-grained connection status used by the Sync Center live-channel indicator. */
sealed interface RealtimeChannelState {
    data object Disconnected : RealtimeChannelState
    data object Connecting : RealtimeChannelState
    data object Connected : RealtimeChannelState
    data object Reconnecting : RealtimeChannelState
}

/** Rejects callbacks from a closed connection or an ended account session. */
internal class CatalogRealtimeDiagnostics {
    private val lock = Any()
    private var generation = 0L
    private val _state = MutableStateFlow<RealtimeChannelState>(RealtimeChannelState.Disconnected)
    val state: StateFlow<RealtimeChannelState> = _state.asStateFlow()
    private val _lastEventAt = MutableStateFlow<Long?>(null)
    val lastEventAt: StateFlow<Long?> = _lastEventAt.asStateFlow()

    fun beginAttempt(reconnecting: Boolean): Long = synchronized(lock) {
        generation++
        _state.value = if (reconnecting) RealtimeChannelState.Reconnecting else RealtimeChannelState.Connecting
        generation
    }

    fun connected(attemptId: Long): Boolean = synchronized(lock) {
        if (attemptId != generation) return false
        _state.value = RealtimeChannelState.Connected
        true
    }

    fun reconnecting(attemptId: Long) = synchronized(lock) {
        if (attemptId == generation) _state.value = RealtimeChannelState.Reconnecting
    }

    fun disconnect(attemptId: Long? = null, clearLastEvent: Boolean = false) = synchronized(lock) {
        if (attemptId != null && attemptId != generation) return
        generation++
        _state.value = RealtimeChannelState.Disconnected
        if (clearLastEvent) _lastEventAt.value = null
    }

    fun acceptChange(attemptId: Long, table: String?, receivedAt: Long): SyncTarget? = synchronized(lock) {
        if (attemptId != generation || _state.value != RealtimeChannelState.Connected) {
            return null
        }
        val target = SyncTarget.fromTable(table) ?: return null
        _lastEventAt.value = receivedAt
        target
    }
}

/** Gson/SignalR wire shape; extra envelope fields are intentionally ignored. */
private data class CatalogChangeEnvelope(val table: String? = null)

private suspend fun Completable.awaitCompletion(): Unit = suspendCancellableCoroutine { continuation ->
    val subscription = subscribe(
        { if (continuation.isActive) continuation.resume(Unit) },
        { error -> if (continuation.isActive) continuation.resumeWithException(error) },
    )
    continuation.invokeOnCancellation { subscription.dispose() }
}
