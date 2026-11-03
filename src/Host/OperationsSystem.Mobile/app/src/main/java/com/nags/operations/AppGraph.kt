package com.nags.operations

import android.content.Context
import androidx.work.WorkManager
import androidx.work.await
import com.nags.operations.data.TokenStore
import com.nags.operations.data.api.AuthApi
import com.nags.operations.data.api.HttpClientFactory
import com.nags.operations.data.api.NotificationsApi
import com.nags.operations.data.api.OperationsApi
import com.nags.operations.data.api.SharedCatalogsApi
import com.nags.operations.data.db.CatalogDatabase
import com.nags.operations.data.db.NotificationsDatabase
import com.nags.operations.data.network.NetworkMonitor
import com.nags.operations.data.notifications.NotificationPushPayload
import com.nags.operations.data.repo.AuthRepository
import com.nags.operations.data.repo.FlightsRepository
import com.nags.operations.data.repo.NotificationsRepository
import com.nags.operations.data.repo.SharedCatalogsRepository
import com.nags.operations.data.realtime.CatalogRealtimeChannel
import com.nags.operations.data.sync.CatalogSyncScheduler
import com.nags.operations.notifications.DeviceTokenManager
import com.nags.operations.notifications.NotificationNavigationCoordinator
import com.nags.operations.notifications.SystemNotificationManager
import io.ktor.client.HttpClient
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

/** Process-scoped dependencies for online flights, shared catalogs, identity, and notifications. */
class AppGraph private constructor(context: Context) {
    val appScope: CoroutineScope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    val tokenStore: TokenStore = TokenStore(context.applicationContext)

    private val httpClient: HttpClient = HttpClientFactory.create(tokenStore)
    private val authApi: AuthApi = AuthApi(tokenStore, httpClient)
    val operationsApi: OperationsApi = OperationsApi(tokenStore, httpClient)
    private val notificationsApi: NotificationsApi = NotificationsApi(tokenStore, httpClient)
    private val sharedCatalogsApi = SharedCatalogsApi(tokenStore, httpClient)
    private val database = NotificationsDatabase.get(context.applicationContext)
    private val catalogDatabase = CatalogDatabase.get(context.applicationContext)

    val flightsRepository: FlightsRepository = FlightsRepository(operationsApi)
    val notificationsRepository: NotificationsRepository =
        NotificationsRepository(notificationsApi, database, tokenStore)
    val sharedCatalogsRepository = SharedCatalogsRepository(sharedCatalogsApi, catalogDatabase, tokenStore)
    val networkMonitor: NetworkMonitor = NetworkMonitor(context.applicationContext, appScope)
    private val catalogSyncScheduler = CatalogSyncScheduler(
        accessTokens = tokenStore.accessTokenFlow,
        online = networkMonitor.isOnline,
        refreshCatalogs = sharedCatalogsRepository::refresh,
        clearCatalogs = sharedCatalogsRepository::clearLocal,
        appScope = appScope,
    )
    private val catalogRealtimeChannel = CatalogRealtimeChannel(
        accessTokens = tokenStore.accessTokenFlow,
        online = networkMonitor.isOnline,
        requestCatalogRefresh = catalogSyncScheduler::refreshNow,
        appScope = appScope,
    )
    val catalogConnectionState get() = catalogRealtimeChannel.state
    val lastCatalogEventAt get() = catalogRealtimeChannel.lastEventAt
    val notificationNavigation = NotificationNavigationCoordinator(context.applicationContext)
    private val systemNotificationManager = SystemNotificationManager(context.applicationContext)
    private val notificationDisplayGate = NotificationDisplayGate()
    val deviceTokenManager = DeviceTokenManager(
        context = context.applicationContext,
        tokenStore = tokenStore,
        api = notificationsApi,
        networkMonitor = networkMonitor,
        appScope = appScope,
    )

    // Retire persisted jobs from an installed older client. Its worker entry point is also inert,
    // so a job already dispatched during the upgrade cannot submit obsolete mutations.
    private val legacyWorkCancellation = appScope.launch(Dispatchers.IO) {
        val workManager = WorkManager.getInstance(context.applicationContext)
        workManager.cancelUniqueWork("operations-work-order-outbox").await()
        workManager.cancelAllWorkByTag("operations-outbox").await()
    }
    private val sessionTransitionMutex = Mutex()
    @Volatile private var secureStorageReady = false

    val authRepository: AuthRepository = AuthRepository(
        api = authApi,
        tokenStore = tokenStore,
        onAccountSwitch = { _, _ -> prepareForAccountSwitch() },
        onSessionPublished = { resumeSessionServices() },
    )

    init {
        appScope.launch {
            tokenStore.initializeSecureStorage()
            legacyWorkCancellation.join()
            secureStorageReady = true
            resumeSessionServices()
        }
    }

    suspend fun signOut() {
        sessionTransitionMutex.withLock {
            notificationNavigation.clearForSessionEnd()
            deactivateNotificationDisplay(tokenStore.getSessionSubject())
            pauseCatalogSync()
            deviceTokenManager.pause()
            deviceTokenManager.revokeBeforeLogout()
            authRepository.logout()
            notificationsRepository.clearLocal()
            sharedCatalogsRepository.clearLocal()
        }
    }

    fun refreshCatalogs() = catalogSyncScheduler.refreshNow()

    /** Flight screens refresh separately; catalogs and the inbox badge refresh on foreground. */
    fun onForeground() {
        if (secureStorageReady) {
            catalogSyncScheduler.refreshNow()
            appScope.launch {
                if (tokenStore.getAccessToken() != null) {
                    runCatching { notificationsRepository.refreshUnreadCount() }
                }
            }
        }
    }

    fun showNotificationIfCurrent(
        payload: NotificationPushPayload,
        expectedSubject: String,
    ): Boolean = notificationDisplayGate.runIfCurrent(expectedSubject) { activeSubject ->
        systemNotificationManager.show(payload, activeSubject)
    }

    private suspend fun prepareForAccountSwitch() {
        sessionTransitionMutex.withLock {
            notificationNavigation.clearForSessionEnd()
            deactivateNotificationDisplay(tokenStore.getSessionSubject())
            pauseCatalogSync()
            deviceTokenManager.pause()
            deviceTokenManager.revokeBeforeLogout()
            // Remove the old bearer before clearing its notification rows and publishing a new user.
            tokenStore.clearTokens()
            notificationsRepository.clearLocal()
            sharedCatalogsRepository.clearLocal()
            tokenStore.clearAll()
        }
    }

    private suspend fun resumeSessionServices() {
        legacyWorkCancellation.join()
        sessionTransitionMutex.withLock {
            catalogSyncScheduler.start()
            catalogRealtimeChannel.start()
            deviceTokenManager.resume()
            if (tokenStore.getAccessToken() != null) {
                tokenStore.getSessionSubject()?.takeIf(String::isNotBlank)?.let {
                    notificationDisplayGate.activate(it)
                }
                appScope.launch { runCatching { notificationsRepository.refresh() } }
            }
        }
    }

    private suspend fun pauseCatalogSync() {
        catalogRealtimeChannel.stop()
        catalogSyncScheduler.stop()
    }

    private fun deactivateNotificationDisplay(fallbackSubject: String?) {
        notificationDisplayGate.deactivate(fallbackSubject) { subject ->
            if (subject.isNullOrBlank()) {
                systemNotificationManager.cancelAllManaged()
            } else {
                systemNotificationManager.cancelForAccount(subject)
            }
        }
    }

    companion object {
        @Volatile private var instance: AppGraph? = null

        fun get(context: Context): AppGraph = instance ?: synchronized(this) {
            instance ?: AppGraph(context.applicationContext).also { instance = it }
        }
    }
}

/** Serializes system-alert display against logout/account-switch cancellation. */
internal class NotificationDisplayGate {
    private val lock = Any()
    private var activeSubject: String? = null
    private var acceptsInitialAuthenticatedSubject = true

    fun activate(subject: String) = synchronized(lock) {
        activeSubject = subject
        acceptsInitialAuthenticatedSubject = true
    }

    fun deactivate(fallbackSubject: String?, cancel: (String?) -> Unit) = synchronized(lock) {
        val subjectToCancel = activeSubject ?: fallbackSubject
        activeSubject = null
        acceptsInitialAuthenticatedSubject = false
        cancel(subjectToCancel)
    }

    fun runIfCurrent(expectedSubject: String, action: (String) -> Unit): Boolean = synchronized(lock) {
        // A killed app can be awakened directly in FirebaseMessagingService before AppGraph's
        // asynchronous startup reaches resumeSessionServices. The authenticated service call may
        // establish that initial subject, but a logout/switch deactivation blocks reactivation.
        if (activeSubject == null && acceptsInitialAuthenticatedSubject) {
            activeSubject = expectedSubject
        }
        val active = activeSubject
        if (active == null || !active.equals(expectedSubject, ignoreCase = true)) return false
        action(active)
        true
    }
}
