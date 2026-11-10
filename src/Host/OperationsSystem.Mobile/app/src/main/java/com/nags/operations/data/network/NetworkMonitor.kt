package com.nags.operations.data.network

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import androidx.core.content.getSystemService
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.callbackFlow
import kotlinx.coroutines.flow.stateIn

/** Observes validation of the app's default network; HTTP still reports individual request failures. */
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
        val state = DefaultNetworkState<Network>()
        val callback = object : ConnectivityManager.NetworkCallback() {
            override fun onAvailable(network: Network) {
                state.onAvailable(network)?.let { trySend(it) }
            }

            override fun onLost(network: Network) {
                // The synchronous activeNetwork snapshot may still contain this lost network.
                state.onLost(network)?.let { trySend(it) }
            }

            override fun onCapabilitiesChanged(network: Network, caps: NetworkCapabilities) {
                val validated = caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET) &&
                    caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_VALIDATED)
                state.onCapabilitiesChanged(network, validated)?.let { trySend(it) }
            }
        }
        // API 26+ delivers capabilities immediately after availability. Waiting for those
        // callbacks avoids both synchronous-query races and a stale registration-time snapshot.
        cm.registerDefaultNetworkCallback(callback)
        awaitClose { cm.unregisterNetworkCallback(callback) }
    }.stateIn(scope, SharingStarted.Eagerly, initialValue = false)
}

/** Keeps callbacks from a superseded default network from changing the latest network's state. */
internal class DefaultNetworkState<N> {
    private var currentNetwork: N? = null
    var isOnline: Boolean = false
        private set

    fun onAvailable(network: N): Boolean? {
        if (network == currentNetwork) return null
        currentNetwork = network
        return update(false)
    }

    fun onCapabilitiesChanged(network: N, validated: Boolean): Boolean? =
        if (network == currentNetwork) update(validated) else null

    fun onLost(network: N): Boolean? {
        if (network != currentNetwork) return null
        currentNetwork = null
        return update(false)
    }

    private fun update(online: Boolean): Boolean? {
        if (online == isOnline) return null
        isOnline = online
        return online
    }
}
