package com.nags.operations

import com.nags.operations.data.hasOwnCancellation
import com.nags.operations.data.visibleArrival
import com.nags.operations.data.visibleDeparture
import com.nags.operations.data.hasOwnWork
import com.nags.operations.data.taskCalculationType

import android.content.Context
import android.net.Uri
import androidx.core.content.FileProvider
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
import com.nags.operations.data.repo.FlightServicesRepository
import com.nags.operations.data.repo.FlightTasksRepository
import com.nags.operations.data.repo.DiskTaskAttachmentStore
import com.nags.operations.data.repo.TaskAttachmentPreviews
import com.nags.operations.data.repo.DiskServiceAttachmentStore
import com.nags.operations.data.repo.ServiceAttachmentPreviews
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
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import com.nags.operations.data.repo.CustomerLogoStore
import com.nags.operations.data.repo.sharedCatalogOwnerKey
import java.io.File
import java.io.ByteArrayOutputStream
import kotlinx.coroutines.withContext

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
    private var flightWriteAcknowledgments = 0L
    val flightArrivals: FlightArrivalsRepository = FlightArrivalsRepository(
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
        fetchFlight = flightsRepository::flight,
        hasUnacknowledgedOtherWork = { id -> flightServices.hasUnacknowledgedAttempt(id) || flightTasks.hasUnacknowledgedAttempt(id) || flightDepartures.hasUnacknowledgedAttempt(id) },
        hasSavedServices = { id -> flightServices.hasSavedInput(id) || flightTasks.hasSavedInput(id) },
        hasSavedCancellation = { id -> hasCancellationIntent(id) }, submissionGate = flightSubmissionGate,
        onAcknowledgedBeforeDelete = { id, requestVersion, acknowledgedVersion ->
            flightWriteAcknowledgments++
            persistArrivalAcknowledgment(id, requestVersion, acknowledgedVersion)
        },
    )
    val serviceAttachmentPreviews = ServiceAttachmentPreviews(
        directory = File(appContext.filesDir, "flight-service-attachment-previews"),
        currentOwner = {
            if (tokenStore.getAccessToken().isNullOrBlank()) null
            else sharedCatalogOwnerKey(HttpClientFactory.baseUrl, tokenStore.getSessionSubject())
        },
        online = { networkMonitor.isOnline.value }, fetch = operationsApi::serviceAttachment,
    )
    val flightServices: FlightServicesRepository = FlightServicesRepository(
        dao = catalogDatabase.flightServiceDao(),
        attachmentStore = DiskServiceAttachmentStore(File(appContext.filesDir, "flight-service-attachments"), ::readWorkAttachment),
        ownerFlow = logoOwners,
        currentOwner = {
            if (tokenStore.getAccessToken().isNullOrBlank()) null
            else sharedCatalogOwnerKey(HttpClientFactory.baseUrl, tokenStore.getSessionSubject())
        },
        online = { networkMonitor.isOnline.value },
        sendSave = { id, recordId, input, version -> flightArrivals.withCurrentSession { operationsApi.saveService(id, recordId, input, version) } },
        sendDelete = { id, recordId, version -> flightArrivals.withCurrentSession { operationsApi.removeService(id, recordId, version) } },
        acceptedSave = flightsRepository::recordService, acceptedDelete = flightsRepository::recordServiceDeleted,
        schedule = { FlightArrivalSyncWorker.schedule(appContext); appScope.launch { syncFlightOperations() } },
        json = HttpClientFactory.json,
        hasSavedArrival = { id -> flightArrivals.mustSyncBeforeServices(id) },
        hasUnacknowledgedDepartureAttempt = { id -> flightDepartures.mustSyncBeforeServices(id) },
        hasUnacknowledgedSiblingWork = { id -> flightTasks.hasUnacknowledgedAttempt(id) },
        effectiveLocalActuals = { flight ->
            com.nags.operations.data.FlightServiceActualBounds(
                flightArrivals.pendingInput(flight.id)?.ata ?: flight.visibleArrival()?.ata,
                flightDepartures.pendingInput(flight.id)?.atd ?: flight.visibleDeparture()?.atd,
            )
        },
        acknowledgedPredecessorVersion = ::acknowledgedBeforeWork,
        fetchFlight = flightsRepository::flight,
        hasSavedCancellation = { id -> hasCancellationIntent(id) }, submissionGate = flightSubmissionGate,
        onAcknowledgedBeforeDelete = { id, before, after ->
            flightWriteAcknowledgments++
            flightArrivals.onPriorWriteAcknowledged(id, before, after)
            flightTasks.onPriorWriteAcknowledged(id, before, after)
            flightDepartures.onPriorWriteAcknowledged(id, before, after)
        },
        onAttachmentsAcknowledged = serviceAttachmentPreviews::recordAcknowledged,
    )
    val taskAttachmentPreviews = TaskAttachmentPreviews(
        directory = File(appContext.filesDir, "flight-task-attachment-previews"),
        currentOwner = {
            if (tokenStore.getAccessToken().isNullOrBlank()) null
            else sharedCatalogOwnerKey(HttpClientFactory.baseUrl, tokenStore.getSessionSubject())
        },
        online = { networkMonitor.isOnline.value }, fetch = operationsApi::taskAttachment,
    )
    val flightTasks: FlightTasksRepository = FlightTasksRepository(
        dao = catalogDatabase.flightTaskDao(),
        attachmentStore = DiskTaskAttachmentStore(File(appContext.filesDir, "flight-task-attachments"), ::readWorkAttachment),
        ownerFlow = logoOwners,
        currentOwner = {
            if (tokenStore.getAccessToken().isNullOrBlank()) null
            else sharedCatalogOwnerKey(HttpClientFactory.baseUrl, tokenStore.getSessionSubject())
        },
        online = { networkMonitor.isOnline.value },
        sendSave = { id, recordId, input, version -> flightArrivals.withCurrentSession { operationsApi.saveTask(id, recordId, input, version) } },
        sendDelete = { id, recordId, version -> flightArrivals.withCurrentSession { operationsApi.removeTask(id, recordId, version) } },
        acceptedSave = flightsRepository::recordTask, acceptedDelete = flightsRepository::recordTaskDeleted,
        schedule = { FlightArrivalSyncWorker.schedule(appContext); appScope.launch { syncFlightOperations() } },
        json = HttpClientFactory.json,
        hasSavedArrival = { id -> flightArrivals.mustSyncBeforeServices(id) },
        hasUnacknowledgedDepartureAttempt = { id -> flightDepartures.mustSyncBeforeServices(id) },
        hasUnacknowledgedSiblingWork = { id -> flightServices.hasUnacknowledgedAttempt(id) },
        effectiveLocalActuals = { flight ->
            com.nags.operations.data.FlightServiceActualBounds(
                flightArrivals.pendingInput(flight.id)?.ata ?: flight.visibleArrival()?.ata,
                flightDepartures.pendingInput(flight.id)?.atd ?: flight.visibleDeparture()?.atd,
            )
        },
        resourceCatalogs = {
            fun types(items: List<com.nags.operations.data.ResourceCatalogItem>) = items.mapNotNull {
                val type = it.taskCalculationType()
                type?.let { value -> it.id to value }
            }.toMap()
            com.nags.operations.data.FlightTaskResourceCatalogs(types(sharedCatalogsRepository.toolsFlow().first()),
                types(sharedCatalogsRepository.materialsFlow().first()), types(sharedCatalogsRepository.generalSupportsFlow().first()))
        },
        legacyDraftAttachment = { flightId, taskId, attachmentId ->
            withContext(Dispatchers.IO) { flightServices.attachmentPreview(flightId, taskId, attachmentId)?.readBytes() }
        },
        acknowledgedPredecessorVersion = ::acknowledgedBeforeWork,
        fetchFlight = flightsRepository::flight,
        hasSavedCancellation = { id -> hasCancellationIntent(id) }, submissionGate = flightSubmissionGate,
        onAcknowledgedBeforeDelete = { id, before, after ->
            flightWriteAcknowledgments++
            flightArrivals.onPriorWriteAcknowledged(id, before, after)
            flightServices.onPriorWriteAcknowledged(id, before, after)
            flightDepartures.onPriorWriteAcknowledged(id, before, after)
        },
        onAttachmentsAcknowledged = taskAttachmentPreviews::recordAcknowledged,
    )
    val flightDepartures: FlightDeparturesRepository = FlightDeparturesRepository(
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
        hasSavedArrival = { id -> flightArrivals.mustSyncBeforeServices(id) },
        hasSavedServices = { id -> flightServices.hasSavedInput(id) || flightTasks.hasSavedInput(id) },
        acknowledgedArrivalVersion = ::acknowledgedBeforeDeparture,
        fetchFlight = flightsRepository::flight,
        onSignatureAcknowledged = { id, saved, bytes -> cacheAcknowledgedSignature(id, saved, bytes) },
        onAcknowledgedBeforeDelete = { id, before, after ->
            flightWriteAcknowledgments++
            flightArrivals.onPriorWriteAcknowledged(id, before, after)
            flightServices.onPriorWriteAcknowledged(id, before, after)
            flightTasks.onPriorWriteAcknowledged(id, before, after)
        },
        hasSavedCancellation = { id -> hasCancellationIntent(id) }, submissionGate = flightSubmissionGate,
    )
    val flightCancellations: com.nags.operations.data.repo.FlightCancellationsRepository = com.nags.operations.data.repo.FlightCancellationsRepository(
        dao = catalogDatabase.flightCancellationDao(), ownerFlow = logoOwners,
        currentOwner = {
            if (tokenStore.getAccessToken().isNullOrBlank()) null
            else sharedCatalogOwnerKey(HttpClientFactory.baseUrl, tokenStore.getSessionSubject())
        },
        online = { networkMonitor.isOnline.value }, send = operationsApi::saveCancellation, revert = operationsApi::revertCancellation,
        accepted = flightsRepository::recordCancellation, onReverted = flightsRepository::recordCancellationReverted,
        schedule = { FlightArrivalSyncWorker.schedule(appContext); appScope.launch { syncFlightOperations() } },
        json = HttpClientFactory.json, submissionGate = flightSubmissionGate,
        hasSavedWork = { id -> flightArrivals.hasSavedInput(id) || flightServices.hasSavedInput(id) || flightTasks.hasSavedInput(id) || flightDepartures.hasSavedInput(id) ||
            flightsRepository.cachedFlight(id)?.let { it.hasOwnWork() || it.services.isNotEmpty() || it.tasks.isNotEmpty() } == true },
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
            flightServices.clear()
            flightTasks.clear()
            flightDepartures.clear()
            flightCancellations.clear()
            departureSignaturePreviews.clear()
            serviceAttachmentPreviews.clear()
            taskAttachmentPreviews.clear()
        }
    }

    private suspend fun hasCancellationIntent(id: String): Boolean =
        flightCancellations.hasSavedInput(id) || flightsRepository.cachedFlight(id)?.hasOwnCancellation() == true

    private suspend fun cacheAcknowledgedSignature(id: String, saved: com.nags.operations.data.FlightDepartureSaved, bytes: ByteArray) {
        departureSignaturePreviews.recordAcknowledged(id, saved, bytes)
    }

    private suspend fun persistArrivalAcknowledgment(id: String, before: String, after: String) {
        flightServices.onPriorWriteAcknowledged(id, before, after)
        flightTasks.onPriorWriteAcknowledged(id, before, after)
        flightDepartures.onArrivalAcknowledged(id, before, after)
    }

    /** Keep a multi-section local save together while each repository commits its durable intent. */
    suspend fun saveFlightOperationBatch(save: suspend () -> Unit) = flightOperationsSyncMutex.withLock { save() }

    /** Relax actual bounds before work, then synchronize work before tightening actual bounds. */
    suspend fun syncFlightOperations(): Boolean {
        if (!secureStorageReady) return true
        return flightOperationsSyncMutex.withLock {
            // A late parent ACK can unblock a service skipped earlier in this pass. Continue
            // only after acknowledged progress; transient failures use the worker's backoff.
            var previousAcknowledgments: Long
            do {
                previousAcknowledgments = flightWriteAcknowledgments
                flightArrivals.syncPending(priorToServices = true)
                flightDepartures.syncPending(priorToServices = true)
                val servicesPending = flightServices.syncPending()
                val tasksPending = flightTasks.syncPending()
                val arrivalsPending = flightArrivals.syncPending()
                val departuresPending = flightDepartures.syncPending()
                val cancellationsPending = flightCancellations.syncPending()
                if (arrivalsPending || servicesPending || tasksPending || departuresPending || cancellationsPending) return@withLock true
            } while (previousAcknowledgments != flightWriteAcknowledgments)
            false
        }
    }

    private suspend fun acknowledgedBeforeWork(flightId: String, original: String): String {
        var version = original
        repeat(64) {
            val next = flightTasks.acknowledgedVersion(flightId, flightServices.acknowledgedVersion(flightId,
                flightDepartures.acknowledgedVersion(flightId, flightArrivals.acknowledgedVersion(flightId, version))))
            if (next == version) return version
            version = next
        }
        return version
    }

    private suspend fun acknowledgedBeforeDeparture(flightId: String, original: String): String {
        var version = original
        repeat(64) {
            val next = flightTasks.acknowledgedVersion(flightId,
                flightServices.acknowledgedVersion(flightId, flightArrivals.acknowledgedVersion(flightId, version)))
            if (next == version) return version
            version = next
        }
        return version
    }

    suspend fun loadServiceAttachment(flightId: String, serviceId: String, attachmentId: String, uploadedAtUtc: String): String? =
        serviceAttachmentPreviews.load(flightId, serviceId, attachmentId, uploadedAtUtc)?.let {
            FileProvider.getUriForFile(appContext, "${appContext.packageName}.flightmedia", it).toString()
        }

    suspend fun loadPendingServiceAttachment(flightId: String, serviceId: String, attachmentId: String): String? =
        flightServices.attachmentPreview(flightId, serviceId, attachmentId)?.let {
            FileProvider.getUriForFile(appContext, "${appContext.packageName}.flightmedia", it).toString()
        }

    suspend fun loadTaskAttachment(flightId: String, taskId: String, attachmentId: String, uploadedAtUtc: String): String? =
        taskAttachmentPreviews.load(flightId, taskId, attachmentId, uploadedAtUtc)?.let {
            FileProvider.getUriForFile(appContext, "${appContext.packageName}.flightmedia", it).toString()
        }

    suspend fun loadPendingTaskAttachment(flightId: String, taskId: String, attachmentId: String): String? =
        (flightTasks.attachmentPreview(flightId, taskId, attachmentId)
            ?: flightServices.attachmentPreview(flightId, taskId, attachmentId))?.let {
            FileProvider.getUriForFile(appContext, "${appContext.packageName}.flightmedia", it).toString()
        }

    private suspend fun readWorkAttachment(source: String): ByteArray = withContext(Dispatchers.IO) {
        val uri = Uri.parse(source)
        check(uri.scheme == "content") { "Select the attachment again before saving." }
        val input = checkNotNull(appContext.contentResolver.openInputStream(uri)) { "The attachment is unavailable." }
        input.use { stream ->
            val output = ByteArrayOutputStream()
            val buffer = ByteArray(8192)
            while (true) {
                val count = stream.read(buffer)
                if (count < 0) break
                check(output.size() + count <= 25 * 1024 * 1024) { "Attachments cannot exceed 25 MiB." }
                output.write(buffer, 0, count)
            }
            output.toByteArray()
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
            flightServices.clear()
            flightTasks.clear()
            flightDepartures.clear()
            flightCancellations.clear()
            departureSignaturePreviews.clear()
            serviceAttachmentPreviews.clear()
            taskAttachmentPreviews.clear()
            tokenStore.clearAll()
        }
    }

    private suspend fun resumeSessionServices() {
        legacyWorkCancellation.join()
        sessionTransitionMutex.withLock {
            flightArrivals.resume()
            flightServices.resume()
            flightTasks.resume()
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
        flightServices.pause()
        flightTasks.pause()
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
