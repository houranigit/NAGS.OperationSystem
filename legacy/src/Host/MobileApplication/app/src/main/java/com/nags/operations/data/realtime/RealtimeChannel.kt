package com.nags.operations.data.realtime

import android.util.Log
import com.microsoft.signalr.HubConnection
import com.microsoft.signalr.HubConnectionBuilder
import com.microsoft.signalr.HubConnectionState
import com.nags.operations.data.TokenStore
import com.nags.operations.data.api.HttpClientFactory
import com.nags.operations.data.api.MobileApi
import com.nags.operations.data.network.NetworkMonitor
import com.nags.operations.data.sync.SyncCoordinator
import io.reactivex.rxjava3.core.Single
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import kotlin.coroutines.resume

/**
 * Owns the SignalR connection to the server's `/hubs/mobile-sync` endpoint and
 * routes every incoming `change` envelope into [SyncCoordinator.applyChange].
 *
 * Lifecycle, end-to-end:
 *
 *  1. **Start condition** — a JWT in [TokenStore] AND [NetworkMonitor.isOnline] = true.
 *     The outer coroutine collects the combined `(signedIn, online)` flag with
 *     `collectLatest`, so any transition (logout, offline) cancels the current
 *     connection cleanly.
 *  2. **Connect** — we build a fresh [HubConnection], attach the `change`
 *     handler, start it, and immediately hit the REST catch-up endpoint with
 *     the freshest per-table cursor we have on disk. Catch-up envelopes are
 *     dispatched to the coordinator the same way live events are, so the
 *     apply path is reused.
 *  3. **Stay connected** — events flow in; we just stamp [lastEventAt] and
 *     dispatch.
 *  4. **Reconnect** — `onClosed` fires when the underlying socket dies. We
 *     surface `Reconnecting`, back off (1s, 2s, 4s, 8s, 16s, capped at 30s),
 *     and rebuild. The catch-up call on the next successful connect plugs any
 *     gap caused by the outage.
 *  5. **Stop** — flipping back to "can't sync" cancels the loop, which closes
 *     the hub from inside `awaitDisposal` so we never leak a connection across
 *     logouts.
 *
 * State surfaces ([state] + [lastEventAt]) feed the Sync Center "Live channel"
 * pill so the operator can answer "is the app actually receiving pushes?" at a
 * glance.
 */
class RealtimeChannel(
    private val tokenStore: TokenStore,
    private val networkMonitor: NetworkMonitor,
    private val coordinator: SyncCoordinator,
    private val mobileApi: MobileApi,
    private val appScope: CoroutineScope,
    private val getCursors: suspend () -> Map<String, String?>,
) {
    private val _state = MutableStateFlow<RealtimeChannelState>(RealtimeChannelState.Disconnected)
    val state: StateFlow<RealtimeChannelState> = _state.asStateFlow()

    private val _lastEventAt = MutableStateFlow<Long?>(null)
    val lastEventAt: StateFlow<Long?> = _lastEventAt.asStateFlow()

    @Volatile private var loop: Job? = null

    /** Idempotent — wires up the connect/reconnect/auth lifecycle exactly once per process. */
    fun start() {
        if (loop?.isActive == true) return
        loop = appScope.launch {
            combine(
                tokenStore.accessTokenFlow,
                networkMonitor.isOnline,
            ) { token, online -> (token != null) && online }
                .distinctUntilChanged()
                .collectLatest { canConnect ->
                    if (!canConnect) {
                        // Either the JWT disappeared (logout) or the radio dropped.
                        // collectLatest will give us a fresh sweep when both come back.
                        _state.value = RealtimeChannelState.Disconnected
                        return@collectLatest
                    }
                    runConnectionLoop()
                }
        }
    }

    /** Tear down the channel — used by the explicit sign-out path. */
    suspend fun stop() {
        loop?.cancelAndJoin()
        loop = null
        _state.value = RealtimeChannelState.Disconnected
    }

    /**
     * Build, connect, deliver catch-up, then suspend until the hub closes (or
     * the coroutine is cancelled). One iteration of this function = one
     * connected session.
     */
    private suspend fun runConnectionLoop() {
        var attempt = 0
        while (isStillActive()) {
            _state.value = if (attempt == 0) RealtimeChannelState.Connecting
            else RealtimeChannelState.Reconnecting

            val connection = buildConnection() ?: run {
                // Token vanished between the combine check and here. Bail and let
                // the outer flow re-enter when a token reappears.
                _state.value = RealtimeChannelState.Disconnected
                return
            }

            try {
                connection.on(CHANGE_METHOD, { change ->
                    handleIncoming(change)
                }, MobileSyncChangeDto::class.java)

                startBlocking(connection)
                _state.value = RealtimeChannelState.Connected
                attempt = 0

                // Catch-up envelopes are applied through the same coordinator path
                // as live pushes — never split the apply logic across "live" and
                // "missed", or one of them will rot.
                runCatching { runCatchup() }
                    .onFailure { Log.w(TAG, "Catch-up after (re)connect failed", it) }

                awaitDisposal(connection)
            } catch (e: Exception) {
                Log.w(TAG, "Realtime channel session failed", e)
            } finally {
                runCatching { stopBlocking(connection) }
            }

            if (!isStillActive()) return

            // Lost the connection while still allowed to sync — back off and try again.
            _state.value = RealtimeChannelState.Reconnecting
            attempt++
            delay(backoffMs(attempt))
        }
    }

    private suspend fun buildConnection(): HubConnection? {
        val token = tokenStore.getAccessToken() ?: return null
        return HubConnectionBuilder
            .create(HttpClientFactory.url(HUB_PATH))
            // SignalR Java handles the WebSocket `?access_token=` upgrade + the
            // negotiate `Authorization: Bearer` header from this single provider.
            .withAccessTokenProvider(Single.defer { Single.just(token) })
            .build()
    }

    private fun handleIncoming(change: MobileSyncChangeDto) {
        _lastEventAt.value = System.currentTimeMillis()
        appScope.launch {
            // applyChange is suspending; the SDK callback fires on its own thread
            // pool, so we marshal back onto our app scope to keep DB writes off
            // the SignalR I/O threads.
            coordinator.applyChange(change)
        }
    }

    private suspend fun runCatchup() {
        val cursors = getCursors()
        // We treat the freshest per-table cursor as "if anything moved since
        // the oldest cache, we want to know". Server returns one refresh
        // envelope per table; the coordinator turns each into a full re-sync.
        val since = cursors.values.filterNotNull().maxOrNull()
        val envelopes = mobileApi.syncChanges(tables = null, since = since)
        for (envelope in envelopes) {
            coordinator.applyChange(envelope)
        }
    }

    /**
     * SignalR's `Completable` blocks on its calling thread — wrap in [Dispatchers.IO]
     * so the coroutine can yield while the handshake/upgrade is in flight.
     */
    private suspend fun startBlocking(connection: HubConnection) =
        withContext(Dispatchers.IO) { connection.start().blockingAwait() }

    private suspend fun stopBlocking(connection: HubConnection) =
        withContext(NonCancellable + Dispatchers.IO) { connection.stop().blockingAwait() }

    /**
     * Suspend until the hub goes back to `DISCONNECTED`. Two ways out:
     *  - `onClosed` fires (server kicked us, network blip, etc.) → resume normally.
     *  - The outer coroutine is cancelled → we cancel and let [stopBlocking] clean up.
     */
    private suspend fun awaitDisposal(connection: HubConnection): Throwable? =
        suspendCancellableCoroutine { cont ->
            connection.onClosed { error ->
                if (cont.isActive) cont.resume(error)
            }
            if (connection.connectionState == HubConnectionState.DISCONNECTED) {
                if (cont.isActive) cont.resume(null)
            }
        }

    private fun backoffMs(attempt: Int): Long {
        // 1s, 2s, 4s, 8s, 16s, capped at 30s. Caps the herd if the server bounces.
        val raw = 1_000L shl (attempt - 1).coerceIn(0, 5)
        return raw.coerceAtMost(30_000L)
    }

    /**
     * Cancellation check without grabbing a child scope (which would always
     * report `isActive == true` and defeat the purpose). Reads the live
     * coroutine context — flips false the instant the outer launch is cancelled.
     */
    private suspend fun isStillActive(): Boolean = currentCoroutineContext().isActive

    companion object {
        private const val TAG = "RealtimeChannel"
        private const val HUB_PATH = "hubs/mobile-sync"
        private const val CHANGE_METHOD = "change"
    }
}

/** Coarse-grained state surfaced to the Sync Center "Live channel" pill. */
sealed interface RealtimeChannelState {
    data object Disconnected : RealtimeChannelState
    data object Connecting : RealtimeChannelState
    data object Connected : RealtimeChannelState
    data object Reconnecting : RealtimeChannelState
}
