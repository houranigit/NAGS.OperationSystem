package com.nags.operations

import com.nags.operations.data.hasOwnCancellation
import com.nags.operations.data.hasOwnWork

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
import com.nags.operations.data.repo.FlightArrivalsRepository
import com.nags.operations.data.repo.FlightDeparturesRepository
import com.nags.operations.data.repo.DiskDepartureSignatureStore
import com.nags.operations.data.repo.DepartureSignaturePreviews
import com.nags.operations.data.outbox.FlightArrivalSyncWorker
import com.nags.operations.data.repo.NotificationsRepository
import com.nags.operations.data.repo.SharedCatalogsRepository
import com.nags.operations.data.realtime.CatalogRealtimeChannel
import com.nags.operations.data.sync.CatalogSyncScheduler
import com.nags.operations.data.sync.SyncTarget
import com.nags.operations.notifications.DeviceTokenManager
import com.nags.operations.notifications.NotificationNavigationCoordinator
import com.nags.operations.notifications.SystemNotificationManager
import io.ktor.client.HttpClient
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import com.nags.operations.data.repo.CustomerLogoStore
import com.nags.operations.data.repo.sharedCatalogOwnerKey
import java.io.File

/** Process-scoped dependencies for flights, shared catalogs, identity, and notifications. */
class AppGraph private constructor(context: Context) {
    private val appContext = context.applicationContext
    val appScope: CoroutineScope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    val tokenStore: TokenStore = TokenStore(context.applicationContext)

    private val httpClient: HttpClient = HttpClientFactory.create(tokenStore)
    private val authApi: AuthApi = AuthApi(tokenStore, httpClient)
    val operationsApi: OperationsApi = OperationsApi(tokenStore, httpClient)
    private val notificationsApi: NotificationsApi = NotificationsApi(tokenStore, httpClient)
    private val sharedCatalogsApi = SharedCatalogsApi(tokenStore, httpClient)
    private val database = NotificationsDatabase.get(context.applicationContext)
    private val catalogDatabase = CatalogDatabase.get(context.applicationContext)

    val networkMonitor: NetworkMonitor = NetworkMonitor(context.applicationContext, appScope)
    private val logoOwners = combine(tokenStore.accessTokenFlow, tokenStore.sessionSubjectFlow) { token, subject ->
        if (token.isNullOrBlank()) null else sharedCatalogOwnerKey(HttpClientFactory.baseUrl, subject)
    }.distinctUntilChanged()
    private val customerLogos = CustomerLogoStore(
        root = File(context.noBackupFilesDir, "customer-logos"), fetch = operationsApi::customerLogo,
        ownerFlow = logoOwners,
        currentOwner = {
            if (tokenStore.getAccessToken().isNullOrBlank()) null
            else sharedCatalogOwnerKey(HttpClientFactory.baseUrl, tokenStore.getSessionSubject())
        },
        online = networkMonitor.isOnline, scope = appScope,
    )
    val flightsRepository = FlightsRepository(operationsApi, catalogDatabase, tokenStore, networkMonitor.isOnline, customerLogos)
    private val flightOperationsSyncMutex = Mutex()
    private val flightSubmissionGate = Mutex()
    val flightArrivals = FlightArrivalsRepository(
        catalogDatabase.flightArrivalDao(), logoOwners,
        currentOwner = {
            if (tokenStore.getAccessToken().isNullOrBlank()) null
            else sharedCatalogOwnerKey(HttpClientFactory.baseUrl, tokenStore.getSessionSubject())
        },
        online = { networkMonitor.isOnline.value }, send = operationsApi::saveArrival,
        accepted = flightsRepository::recordArrival,
        schedule = {
            FlightArrivalSyncWorker.schedule(appContext)
            appScope.launch { syncFlightOperations() }
        },
        json = HttpClientFactory.json,
        hasSavedCancellation = { id -> hasCancellationIntent(id) }, submissionGate = flightSubmissionGate,
        onAcknowledgedBeforeDelete = { id, requestVersion, acknowledgedVersion ->
            persistArrivalAcknowledgment(id, requestVersion, acknowledgedVersion)
        },
    )
    val flightDepartures = FlightDeparturesRepository(
        dao = catalogDatabase.flightDepartureDao(), ownerFlow = logoOwners,
        currentOwner = {
            if (tokenStore.getAccessToken().isNullOrBlank()) null
            else sharedCatalogOwnerKey(HttpClientFactory.baseUrl, tokenStore.getSessionSubject())
        },
        online = { networkMonitor.isOnline.value }, send = operationsApi::saveDeparture,
        accepted = flightsRepository::recordDeparture,
        schedule = {
            FlightArrivalSyncWorker.schedule(appContext)
            appScope.launch { syncFlightOperations() }
        },
        json = HttpClientFactory.json,
        signatureStore = DiskDepartureSignatureStore(File(appContext.noBackupFilesDir, "flight-departure-signatures")),
        hasSavedArrival = flightArrivals::hasSavedInput,
        acknowledgedArrivalVersion = flightArrivals::acknowledgedVersion,
        fetchFlight = flightsRepository::flight,
        onSignatureAcknowledged = { id, saved, bytes -> cacheAcknowledgedSignature(id, saved, bytes) },
        hasSavedCancellation = { id -> hasCancellationIntent(id) }, submissionGate = flightSubmissionGate,
    )
    val flightCancellations = com.nags.operations.data.repo.FlightCancellationsRepository(
        dao = catalogDatabase.flightCancellationDao(), ownerFlow = logoOwners,
        currentOwner = {
            if (tokenStore.getAccessToken().isNullOrBlank()) null
            else sharedCatalogOwnerKey(HttpClientFactory.baseUrl, tokenStore.getSessionSubject())
        },
        online = { networkMonitor.isOnline.value }, send = operationsApi::saveCancellation, revert = operationsApi::revertCancellation,
        accepted = flightsRepository::recordCancellation, onReverted = flightsRepository::recordCancellationReverted,
        schedule = { FlightArrivalSyncWorker.schedule(appContext); appScope.launch { syncFlightOperations() } },
        json = HttpClientFactory.json, submissionGate = flightSubmissionGate,
        hasSavedWork = { id -> flightArrivals.hasSavedInput(id) || flightDepartures.hasSavedInput(id) || flightsRepository.cachedFlight(id)?.hasOwnWork() == true },
    )
    val departureSignaturePreviews = DepartureSignaturePreviews(
        directory = File(appContext.noBackupFilesDir, "departure-signature-previews"),
        currentOwner = {
            if (tokenStore.getAccessToken().isNullOrBlank()) null
            else sharedCatalogOwnerKey(HttpClientFactory.baseUrl, tokenStore.getSessionSubject())
        },
        online = { networkMonitor.isOnline.value }, fetch = operationsApi::departureSignature,
        pendingBytes = { id, changeId -> flightDepartures.signaturePreview(id, changeId) },
    )
    val notificationsRepository: NotificationsRepository =
        NotificationsRepository(notificationsApi, database, tokenStore, appScope, networkMonitor.isOnline)
    val sharedCatalogsRepository = SharedCatalogsRepository(sharedCatalogsApi, catalogDatabase, tokenStore, customerLogos)
    val isSyncing = combine(sharedCatalogsRepository.isSyncing, flightsRepository.isSyncing) { catalogs, flights ->
        catalogs || flights
    }.stateIn(appScope, SharingStarted.Eagerly, false)
    private val catalogSyncScheduler = CatalogSyncScheduler(
        accessTokens = tokenStore.accessTokenFlow,
        online = networkMonitor.isOnline,
        refreshCatalogs = sharedCatalogsRepository::refresh,
        clearCatalogs = {
            sharedCatalogsRepository.clearLocal()
            flightsRepository.clearLocal()
        },
        appScope = appScope,
        refreshFlightCache = {
            syncFlightOperations()
            flightsRepository.refreshCache()
        },
    )
    private val catalogRealtimeChannel = CatalogRealtimeChannel(
        accessTokens = tokenStore.accessTokenFlow,
        online = networkMonitor.isOnline,
        requestRefresh = { target ->
            when (target) {
                is SyncTarget.Catalog -> catalogSyncScheduler.refreshCatalog(target.catalog)
                SyncTarget.Flights -> catalogSyncScheduler.refreshFlights()
            }
        },
        requestFullRefresh = catalogSyncScheduler::refreshNow,
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
            combine(networkMonitor.isOnline, tokenStore.sessionSubjectFlow) { online, subject -> online to subject }
                .distinctUntilChanged().collect { (online, subject) ->
                    if (secureStorageReady && online && !subject.isNullOrBlank()) notificationsRepository.syncPendingReads()
                }
        }
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
            flightsRepository.clearLocal()
            flightArrivals.clear()
            flightDepartures.clear()
            flightCancellations.clear()
            departureSignaturePreviews.clear()
        }
    }

    private suspend fun hasCancellationIntent(id: String): Boolean =
        flightCancellations.hasSavedInput(id) || flightsRepository.cachedFlight(id)?.hasOwnCancellation() == true

    private suspend fun cacheAcknowledgedSignature(id: String, saved: com.nags.operations.data.FlightDepartureSaved, bytes: ByteArray) {
        departureSignaturePreviews.recordAcknowledged(id, saved, bytes)
    }

    private suspend fun persistArrivalAcknowledgment(id: String, before: String, after: String) {
        flightDepartures.onArrivalAcknowledged(id, before, after)
    }

    /** Retain one background drain for both queues, always acknowledging arrival dependencies first. */
    suspend fun syncFlightOperations(): Boolean {
        if (!secureStorageReady) return true
        return flightOperationsSyncMutex.withLock {
            val arrivalsPending = flightArrivals.syncPending()
            val departuresPending = flightDepartures.syncPending()
            val cancellationsPending = flightCancellations.syncPending()
            arrivalsPending || departuresPending || cancellationsPending
        }
    }

    fun refreshCatalogs() = catalogSyncScheduler.refreshNow()

    /** Recheck read-only caches and the inbox badge when the app returns to the foreground. */
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
            flightsRepository.clearLocal()
            flightArrivals.clear()
            flightDepartures.clear()
            flightCancellations.clear()
            departureSignaturePreviews.clear()
            tokenStore.clearAll()
        }
    }

    private suspend fun resumeSessionServices() {
        legacyWorkCancellation.join()
        sessionTransitionMutex.withLock {
            flightArrivals.resume()
            flightDepartures.resume()
            flightCancellations.resume()
            catalogSyncScheduler.start()
            FlightArrivalSyncWorker.schedule(appContext)
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
        flightArrivals.pause()
        flightDepartures.pause()
        flightCancellations.pause()
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
