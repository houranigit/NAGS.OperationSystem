package com.nags.operations.data.network

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.net.NetworkRequest
import androidx.core.content.getSystemService
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.callbackFlow
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.CoroutineScope

/**
 * Observes the OS connectivity callbacks and emits a coarse "are we online?"
 * boolean. The sync coordinator uses this to kick a fresh sync whenever the
 * device transitions from offline → online so the user sees the latest data
 * the moment they re-enter coverage on the ramp.
 *
 * We deliberately stay coarse — no metering / VPN / captive-portal distinctions
 * here; if anything claims to be a working network we trust it and let the
 * HTTP layer surface real failures.
 */
class NetworkMonitor(
    context: Context,
    scope: CoroutineScope,
) {
    private val cm = context.applicationContext.getSystemService<ConnectivityManager>()

    val isOnline: StateFlow<Boolean> = callbackFlow {
        if (cm == null) {
            trySend(false)
            awaitClose { /* no callback registered */ }
            return@callbackFlow
        }
        val callback = object : ConnectivityManager.NetworkCallback() {
            override fun onAvailable(network: Network) {
                trySend(true)
            }

            override fun onLost(network: Network) {
                trySend(hasActiveCapability())
            }

            override fun onCapabilitiesChanged(network: Network, caps: NetworkCapabilities) {
                trySend(caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET))
            }
        }
        val request = NetworkRequest.Builder()
            .addCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
            .build()
        cm.registerNetworkCallback(request, callback)
        trySend(hasActiveCapability())
        awaitClose { cm.unregisterNetworkCallback(callback) }
    }.stateIn(scope, SharingStarted.Eagerly, initialValue = hasActiveCapability())

    private fun hasActiveCapability(): Boolean {
        val active = cm?.activeNetwork ?: return false
        val caps = cm.getNetworkCapabilities(active) ?: return false
        return caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
    }
}
