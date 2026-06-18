package com.nags.operations.data.sync

import com.nags.operations.data.TokenStore
import com.nags.operations.data.network.NetworkMonitor
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.firstOrNull
import kotlinx.coroutines.launch

/**
 * Drives "near real-time" freshness while the app is in the foreground. Every
 * trigger funnels into [SyncCoordinator.refreshAll]:
 *
 *  • **Sign-in** — first foreground sync runs the instant a JWT lands in [TokenStore].
 *  • **Foreground resume** — covered by the same login-watching loop because the
 *    StateFlow re-collects from the latest value when the activity re-attaches.
 *  • **Network restored** — when [NetworkMonitor] flips offline → online while
 *    a user is signed in, we trigger an immediate sync so the user sees current
 *    data without tapping refresh.
 *  • **Periodic safety net** — every [pollIntervalMs] (default 5 min) while
 *    signed in *and* online. With the SignalR realtime channel doing the
 *    minute-to-minute work, this poll is purely a belt-and-braces sweep for
 *    pushes that may have been dropped during a transport hiccup.
 *
 * The full closed-app push channel (FCM) is intentionally a later slice —
 * keeping this scheduler simple makes "what makes the data refresh" easy to
 * reason about.
 */
class SyncScheduler(
    private val tokenStore: TokenStore,
    private val networkMonitor: NetworkMonitor,
    private val coordinator: SyncCoordinator,
    private val appScope: CoroutineScope,
    private val pollIntervalMs: Long = 5L * 60L * 1_000L,
) {
    @Volatile private var loop: Job? = null

    /** Idempotent — multiple calls are safe; only the first actually starts the loop. */
    fun start() {
        if (loop?.isActive == true) return
        loop = appScope.launch {
            // We re-launch the inner polling loop every time (signedIn, online) flips,
            // so the loop simply doesn't exist while the user is logged out or offline.
            combine(
                tokenStore.accessTokenFlow,
                networkMonitor.isOnline,
            ) { token, online -> token != null && online }
                .distinctUntilChanged()
                .collectLatest { canSync ->
                    if (!canSync) return@collectLatest
                    // Immediate sync on transition into the "can sync" state — captures
                    // both sign-in (token appears) and reconnect (online flips true).
                    runCatching { coordinator.refreshAll() }
                    while (true) {
                        delay(pollIntervalMs)
                        runCatching { coordinator.refreshAll() }
                    }
                }
        }
    }

    /**
     * Run a single refresh on demand, regardless of the polling cadence.
     * Returns immediately; the caller observes [SyncCoordinator.isSyncing] for
     * progress and the `sync_state` table for results.
     */
    fun refreshNow() {
        appScope.launch {
            val signedIn = tokenStore.accessTokenFlow.firstOrNull() != null
            if (!signedIn) return@launch
            val online = networkMonitor.isOnline.first()
            if (!online) return@launch
            coordinator.refreshAll()
        }
    }
}
