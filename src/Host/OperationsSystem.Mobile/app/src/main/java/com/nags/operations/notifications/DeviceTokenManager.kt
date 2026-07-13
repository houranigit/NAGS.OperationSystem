package com.nags.operations.notifications

import android.content.Context
import android.util.Log
import com.google.firebase.FirebaseApp
import com.google.firebase.messaging.FirebaseMessaging
import com.nags.operations.BuildConfig
import com.nags.operations.data.TokenStore
import com.nags.operations.data.api.NotificationsApi
import com.nags.operations.data.network.NetworkMonitor
import com.nags.operations.data.notifications.RegisterDeviceTokenRequest
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withTimeoutOrNull
import java.util.Locale
import java.util.UUID
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException

class DeviceTokenManager(
    private val context: Context,
    private val tokenStore: TokenStore,
    private val api: NotificationsApi,
    private val networkMonitor: NetworkMonitor,
    private val appScope: CoroutineScope,
) {
    private val preferences = context.getSharedPreferences("fcm_registration", Context.MODE_PRIVATE)
    private var observerJob: Job? = null
    private val destinationState = RegistrationDestinationState(
        preferences.getString(KEY_REGISTRATION_ID, null),
    )
    private val registrationEnabled = MutableStateFlow(true)
    private val registrationGeneration = MutableStateFlow(0L)

    fun start() {
        if (!isConfigured || observerJob != null) return
        observerJob = appScope.launch {
            combine(
                tokenStore.accessTokenFlow,
                networkMonitor.isOnline,
                registrationEnabled,
                registrationGeneration,
            ) { access, online, enabled, _ ->
                access != null && online && enabled
            }.collect { ready ->
                if (ready) registerCurrentTokenWithRetry()
            }
        }
    }

    fun pause() {
        registrationEnabled.value = false
    }

    fun resume() {
        registrationEnabled.value = true
        start()
    }

    fun onRegistered(token: String) {
        if (!isConfigured || token.isBlank() || !registrationEnabled.value) return
        // register() also invokes onRegistered for an unchanged cached FID. Only a genuinely new
        // destination may wake the collector, otherwise register -> callback -> register loops.
        if (!destinationState.update(token)) return
        preferences.edit().putString(KEY_REGISTRATION_ID, token).apply()
        registrationGeneration.value++
    }

    suspend fun revokeBeforeLogout() {
        if (!isConfigured) return
        val token = destinationState.current()
        if (!token.isNullOrBlank() && tokenStore.getAccessToken() != null) {
            withTimeoutOrNull(4_000L) { runCatching { api.revokeDevice(token) } }
        }
        withTimeoutOrNull(4_000L) { runCatching { unregisterFirebaseInstallation() } }
        destinationState.clear()
        preferences.edit().remove(KEY_REGISTRATION_ID).apply()
    }

    private suspend fun registerCurrentTokenWithRetry() {
        var delayMs = 1_000L
        var attempt = 0
        while (tokenStore.getAccessToken() != null &&
            networkMonitor.isOnline.value &&
            registrationEnabled.value
        ) {
            try {
                destinationState.current()?.let { register(it) }
                requestFirebaseRegistration()
                return
            } catch (e: Exception) {
                if (e is CancellationException) throw e
                attempt++
                Log.w(TAG, "FCM destination registration attempt $attempt failed; retrying", e)
                delay(delayMs)
                delayMs = (delayMs * 2).coerceAtMost(MAX_RETRY_DELAY_MS)
            }
        }
    }

    private suspend fun register(token: String) {
        api.registerDevice(
            RegisterDeviceTokenRequest(
                token = token,
                deviceId = installationDeviceId(),
                locale = Locale.getDefault().toLanguageTag(),
                appVersion = BuildConfig.VERSION_NAME,
            ),
        )
    }

    private fun installationDeviceId(): String {
        preferences.getString(KEY_DEVICE_ID, null)?.takeIf(String::isNotBlank)?.let { return it }
        return UUID.randomUUID().toString().also {
            preferences.edit().putString(KEY_DEVICE_ID, it).apply()
        }
    }

    private suspend fun requestFirebaseRegistration(): Unit = suspendCancellableCoroutine { continuation ->
        FirebaseMessaging.getInstance().register()
            .addOnSuccessListener { if (continuation.isActive) continuation.resume(Unit) }
            .addOnFailureListener { if (continuation.isActive) continuation.resumeWithException(it) }
    }

    private suspend fun unregisterFirebaseInstallation(): Unit = suspendCancellableCoroutine { continuation ->
        FirebaseMessaging.getInstance().unregister()
            .addOnSuccessListener { if (continuation.isActive) continuation.resume(Unit) }
            .addOnFailureListener { if (continuation.isActive) continuation.resumeWithException(it) }
    }

    private val isConfigured: Boolean
        get() = BuildConfig.FIREBASE_CONFIGURED && FirebaseApp.getApps(context).isNotEmpty()

    companion object {
        private const val TAG = "DeviceTokenManager"
        private const val KEY_REGISTRATION_ID = "registration_id"
        private const val KEY_DEVICE_ID = "device_id"
        private const val MAX_RETRY_DELAY_MS = 5 * 60 * 1_000L
    }
}

internal class RegistrationDestinationState(initialValue: String?) {
    @Volatile private var value: String? = initialValue

    fun current(): String? = value

    @Synchronized
    fun update(candidate: String): Boolean {
        if (candidate == value) return false
        value = candidate
        return true
    }

    @Synchronized
    fun clear() {
        value = null
    }
}
