package com.nags.operations

import com.nags.operations.data.*
import com.nags.operations.data.repo.FlightOperationsBatchesRepository
import com.nags.operations.data.repo.RoomFlightOperationsBatchStore
import com.nags.operations.data.repo.FlightOperationsGate
import kotlinx.serialization.json.Json
import java.util.Base64
import kotlinx.coroutines.CancellationException
import com.nags.operations.data.hasOwnCancellation
import com.nags.operations.data.visibleArrival
import com.nags.operations.data.visibleDeparture
import com.nags.operations.data.hasOwnWork

import android.content.Context
import androidx.room.withTransaction
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
    val flightsRepository = FlightsRepository(operationsApi, catalogDatabase, tokenStore, networkMonitor.isOnline, customerLogos,
        prepareOffline = ::prepareFlightOffline,
        localAdHocFlights = { adHocFlights.localFlights() }, unmaterializedAdHoc = { adHocFlights.isUnmaterialized(it) },
        reconcileAdHoc = { ids, complete, observed -> adHocFlights.reconcileManifest(ids, complete, observed) },
        revokeAdHoc = { owner, id -> adHocFlights.revokeForOwner(owner, id) }, recordAdHoc = { adHocFlights.recordAcknowledged(it) },
        confirmAdHocStation = { adHocFlights.confirmStation(it) },
        additionalCachedFlight = { discoveryFlights.cachedFlight(it) },
        beginAdditionalDetailRead = { discoveryFlights.detailRevision(it) },
        recordAdditionalFetched = { detail, revision -> discoveryFlights.recordFetched(detail, revision) },
        recordAdditionalAcknowledged = { discoveryFlights.recordAcknowledged(it) },
        prepareAdditionalOffline = { discoveryFlights.refreshKnownAndPrepare() },
        confirmAdditionalStation = { discoveryFlights.confirmStation(it) }, revokeAdditional = { owner, id -> discoveryFlights.revokeForOwner(owner, id) },
        additionalLogoFlights = { discoveryFlights.logoFlights() })
    private val flightOperationsSyncMutex = FlightOperationsGate()
    private val flightSubmissionGate = Mutex()
    private var flightWriteAcknowledgments = 0L
    val flightArrivals: FlightArrivalsRepository = FlightArrivalsRepository(
        catalogDatabase.flightArrivalDao(), logoOwners,
        currentOwner = {
            if (tokenStore.getAccessToken().isNullOrBlank()) null
            else sharedCatalogOwnerKey(HttpClientFactory.baseUrl, tokenStore.getSessionSubject())
        },
        operationGate = flightOperationsSyncMutex,
        operationWrite = { work -> flightOperationsSyncMutex.withLock { work() } },
        operationSave = { work -> flightOperationsSyncMutex.withLock { flightOperationsBatches.save(work) } },
        grouped = { id, _ -> flightOperationsBatches.blocksFreshDrain(id) },
        onAttemptRejected = { id, recordId -> flightOperationsBatches.legacyRejected("arrival", id, recordId) },
        groupedRetry = { id, recordId -> flightOperationsSyncMutex.withLock { flightOperationsBatches.retry("arrival", id, recordId) } },
        groupedDiscard = { id, recordId -> flightOperationsSyncMutex.withLock { flightOperationsBatches.discard("arrival", id, recordId) } },
        online = { networkMonitor.isOnline.value }, send = operationsApi::saveArrival,
        accepted = { id, saved -> flightsRepository.recordArrival(id, saved); discoveryFlights.recordArrival(id, saved); flightOperationsBatches.legacyArrivalAccepted(id, saved) },
        schedule = {
            FlightArrivalSyncWorker.schedule(appContext)
            appScope.launch { syncFlightOperations() }
        },
        json = HttpClientFactory.json,
        onLocalSaved = { discoveryFlights.recordLocalSave(it) },
        fetchFlight = flightsRepository::flight,
        hasUnacknowledgedOtherWork = { id -> flightServices.hasUnacknowledgedAttempt(id) || flightTasks.hasUnacknowledgedAttempt(id) || flightDepartures.hasUnacknowledgedAttempt(id) },
        hasSavedServices = { id -> flightServices.hasSavedInput(id) || flightTasks.hasSavedInput(id) },
        hasSavedCancellation = { id -> hasCancellationIntent(id) }, submissionGate = flightSubmissionGate,
        onAcknowledgedBeforeDelete = { id, requestVersion, acknowledgedVersion ->
            flightWriteAcknowledgments++
            flightOperationsBatches.onPriorWriteAcknowledged(id, requestVersion, acknowledgedVersion)
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
        operationGate = flightOperationsSyncMutex,
        operationWrite = { work -> flightOperationsSyncMutex.withLock { work() } },
        operationSave = { work -> flightOperationsSyncMutex.withLock { flightOperationsBatches.save(work) } },
        grouped = { id, _ -> flightOperationsBatches.blocksFreshDrain(id) },
        onAttemptRejected = { id, recordId -> flightOperationsBatches.legacyRejected("service", id, recordId) },
        groupedRetry = { id, recordId -> flightOperationsSyncMutex.withLock { flightOperationsBatches.retry("service", id, recordId) } },
        groupedDiscard = { id, recordId -> flightOperationsSyncMutex.withLock { flightOperationsBatches.discard("service", id, recordId) } },
        batchFiles = { flightOperationsBatches.fileReferences("service") },
        sendSave = { id, recordId, input, version -> flightArrivals.withCurrentSession { operationsApi.saveService(id, recordId, input, version) } },
        sendDelete = { id, recordId, version -> flightArrivals.withCurrentSession { operationsApi.removeService(id, recordId, version) } },
        acceptedSave = { id, saved -> flightsRepository.recordService(id, saved); discoveryFlights.recordService(id, saved); flightOperationsBatches.legacyServiceAccepted(id, saved) }, acceptedDelete = { id, saved -> flightsRepository.recordServiceDeleted(id, saved); discoveryFlights.recordServiceDeleted(id, saved); flightOperationsBatches.legacyServiceDeleted(id, saved.id) },
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
            flightOperationsBatches.onPriorWriteAcknowledged(id, before, after)
            flightArrivals.onPriorWriteAcknowledged(id, before, after)
            flightTasks.onPriorWriteAcknowledged(id, before, after)
            flightDepartures.onPriorWriteAcknowledged(id, before, after)
        },
        onAttachmentsAcknowledged = serviceAttachmentPreviews::recordAcknowledged,
        onLocalSaved = { discoveryFlights.recordLocalSave(it) },
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
        operationGate = flightOperationsSyncMutex,
        operationWrite = { work -> flightOperationsSyncMutex.withLock { work() } },
        operationSave = { work -> flightOperationsSyncMutex.withLock { flightOperationsBatches.save(work) } },
        grouped = { id, _ -> flightOperationsBatches.blocksFreshDrain(id) },
        onAttemptRejected = { id, recordId -> flightOperationsBatches.legacyRejected("task", id, recordId) },
        groupedRetry = { id, recordId -> flightOperationsSyncMutex.withLock { flightOperationsBatches.retry("task", id, recordId) } },
        groupedDiscard = { id, recordId -> flightOperationsSyncMutex.withLock { flightOperationsBatches.discard("task", id, recordId) } },
        batchFiles = { flightOperationsBatches.fileReferences("task") },
        sendSave = { id, recordId, input, version -> flightArrivals.withCurrentSession { operationsApi.saveTask(id, recordId, input, version) } },
        sendDelete = { id, recordId, version -> flightArrivals.withCurrentSession { operationsApi.removeTask(id, recordId, version) } },
        acceptedSave = { id, saved -> flightsRepository.recordTask(id, saved); discoveryFlights.recordTask(id, saved); flightOperationsBatches.legacyTaskAccepted(id, saved) }, acceptedDelete = { id, saved -> flightsRepository.recordTaskDeleted(id, saved); discoveryFlights.recordTaskDeleted(id, saved); flightOperationsBatches.legacyTaskDeleted(id, saved.id) },
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
        resourceCatalogs = { sharedCatalogsRepository.taskResourceCatalogs() },
        legacyDraftAttachment = { flightId, taskId, attachmentId ->
            withContext(Dispatchers.IO) { flightServices.attachmentPreview(flightId, taskId, attachmentId)?.readBytes() }
        },
        acknowledgedPredecessorVersion = ::acknowledgedBeforeWork,
        fetchFlight = flightsRepository::flight,
        hasSavedCancellation = { id -> hasCancellationIntent(id) }, submissionGate = flightSubmissionGate,
        onAcknowledgedBeforeDelete = { id, before, after ->
            flightWriteAcknowledgments++
            flightOperationsBatches.onPriorWriteAcknowledged(id, before, after)
            flightArrivals.onPriorWriteAcknowledged(id, before, after)
            flightServices.onPriorWriteAcknowledged(id, before, after)
            flightDepartures.onPriorWriteAcknowledged(id, before, after)
        },
        onAttachmentsAcknowledged = taskAttachmentPreviews::recordAcknowledged,
        onLocalSaved = { discoveryFlights.recordLocalSave(it) },
    )
    val flightDepartures: FlightDeparturesRepository = FlightDeparturesRepository(
        dao = catalogDatabase.flightDepartureDao(), ownerFlow = logoOwners,
        currentOwner = {
            if (tokenStore.getAccessToken().isNullOrBlank()) null
            else sharedCatalogOwnerKey(HttpClientFactory.baseUrl, tokenStore.getSessionSubject())
        },
        operationGate = flightOperationsSyncMutex,
        operationWrite = { work -> flightOperationsSyncMutex.withLock { work() } },
        operationSave = { work -> flightOperationsSyncMutex.withLock { flightOperationsBatches.save(work) } },
        grouped = { id, _ -> flightOperationsBatches.blocksFreshDrain(id) },
        onAttemptRejected = { id, recordId -> flightOperationsBatches.legacyRejected("departure", id, recordId) },
        groupedRetry = { id, recordId -> flightOperationsSyncMutex.withLock { flightOperationsBatches.retry("departure", id, recordId) } },
        groupedDiscard = { id, recordId -> flightOperationsSyncMutex.withLock { flightOperationsBatches.discard("departure", id, recordId) } },
        batchFiles = { flightOperationsBatches.fileReferences("departure") },
        online = { networkMonitor.isOnline.value }, send = operationsApi::saveDeparture,
        accepted = { id, saved -> flightsRepository.recordDeparture(id, saved); discoveryFlights.recordDeparture(id, saved); flightOperationsBatches.legacyDepartureAccepted(id, saved) },
        schedule = {
            FlightArrivalSyncWorker.schedule(appContext)
            appScope.launch { syncFlightOperations() }
        },
        json = HttpClientFactory.json,
        onLocalSaved = { discoveryFlights.recordLocalSave(it) },
        signatureStore = DiskDepartureSignatureStore(File(appContext.noBackupFilesDir, "flight-departure-signatures")),
        hasSavedArrival = { id -> flightArrivals.mustSyncBeforeServices(id) },
        hasSavedServices = { id -> flightServices.hasSavedInput(id) || flightTasks.hasSavedInput(id) },
        acknowledgedArrivalVersion = ::acknowledgedBeforeDeparture,
        fetchFlight = flightsRepository::flight,
        onSignatureAcknowledged = { id, saved, bytes -> cacheAcknowledgedSignature(id, saved, bytes) },
        onAcknowledgedBeforeDelete = { id, before, after ->
            flightWriteAcknowledgments++
            flightOperationsBatches.onPriorWriteAcknowledged(id, before, after)
            flightArrivals.onPriorWriteAcknowledged(id, before, after)
            flightServices.onPriorWriteAcknowledged(id, before, after)
            flightTasks.onPriorWriteAcknowledged(id, before, after)
        },
        hasSavedCancellation = { id -> hasCancellationIntent(id) }, submissionGate = flightSubmissionGate,
    )
    val flightCancellations: com.nags.operations.data.repo.FlightCancellationsRepository = com.nags.operations.data.repo.FlightCancellationsRepository(
        dao = catalogDatabase.flightCancellationDao(), ownerFlow = logoOwners,
        operationGate = flightOperationsSyncMutex,
        operationWrite = { work -> flightOperationsSyncMutex.withLock { work() } },
        currentOwner = {
            if (tokenStore.getAccessToken().isNullOrBlank()) null
            else sharedCatalogOwnerKey(HttpClientFactory.baseUrl, tokenStore.getSessionSubject())
        },
        online = { networkMonitor.isOnline.value }, send = operationsApi::saveCancellation, revert = operationsApi::revertCancellation,
        accepted = { id, saved -> flightsRepository.recordCancellation(id, saved); discoveryFlights.recordCancellation(id, saved) },
        onReverted = { id, saved -> flightsRepository.recordCancellationReverted(id, saved); discoveryFlights.recordCancellationReverted(id, saved) },
        onLocalSaved = { discoveryFlights.recordLocalSave(it) }, transaction = { work -> catalogDatabase.withTransaction { work() } },
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
    private val flightOperationsBatches: FlightOperationsBatchesRepository by lazy {
        FlightOperationsBatchesRepository(RoomFlightOperationsBatchStore(catalogDatabase),
            currentOwner = { if (tokenStore.getAccessToken().isNullOrBlank()) null else sharedCatalogOwnerKey(HttpClientFactory.baseUrl, tokenStore.getSessionSubject()) },
            online = { networkMonitor.isOnline.value }, fetch = flightsRepository::flight,
            send = operationsApi::saveOperationsBatch,
            accepted = { detail, input ->
                // Publish the complete HTTP ACK once; previews are optional and precede private-file release.
                flightsRepository.recordOperationsBatch(detail)
                for (row in input.services) detail.services.firstOrNull { it.id == row.serviceRecordId }?.let { record ->
                    try { serviceAttachmentPreviews.recordAcknowledged(detail.id, FlightServiceSaved(record, detail.rowVersion),
                        flightServices.batchInput(row)?.attachments.orEmpty().mapNotNull { file -> file.content?.let { file.id to Base64.getDecoder().decode(it) } }.toMap()) }
                    catch (cancelled: CancellationException) { throw cancelled } catch (_: Exception) { }
                }
                for (row in input.tasks) detail.tasks.firstOrNull { it.id == row.taskRecordId }?.let { record ->
                    try { taskAttachmentPreviews.recordAcknowledged(detail.id, FlightTaskSaved(record, detail.rowVersion),
                        flightTasks.batchInput(row)?.attachments.orEmpty().mapNotNull { file -> file.content?.let { file.id to Base64.getDecoder().decode(it) } }.toMap()) }
                    catch (cancelled: CancellationException) { throw cancelled } catch (_: Exception) { }
                }
                input.departure?.let { row ->
                    val target = HttpClientFactory.json.decodeFromString<SaveFlightDepartureRequest>(row.inputJson).targetSubmissionId
                    val record = detail.departureSubmissions.firstOrNull { if (target != null) it.id == target else it.submittedByEmployee.staffMemberId.equals(row.staffMemberId, true) }
                    val content = flightDepartures.batchInput(row).signatureContent
                    if (record != null && content != null) try { departureSignaturePreviews.recordAcknowledged(detail.id,
                        FlightDepartureSaved(record, detail.rowVersion), Base64.getDecoder().decode(content)) }
                    catch (cancelled: CancellationException) { throw cancelled } catch (_: Exception) { }
                }
                flightWriteAcknowledgments++
            },
            hydrateArrival = { HttpClientFactory.json.decodeFromString<SaveFlightArrivalRequest>(it.inputJson) },
            hydrateDeparture = flightDepartures::batchInput, hydrateService = flightServices::batchInput, hydrateTask = flightTasks::batchInput,
            acknowledgedVersion = ::acknowledgedBeforeWork,
            deferCleanup = { deferred -> flightServices.batchCleanupDeferred = deferred; flightTasks.batchCleanupDeferred = deferred; flightDepartures.batchCleanupDeferred = deferred },
            cleanup = { flightServices.cleanupAfterBatch(); flightTasks.cleanupAfterBatch(); flightDepartures.cleanupAfterBatch() },
            schedule = { FlightArrivalSyncWorker.schedule(appContext); appScope.launch { syncFlightOperations() } }, json = HttpClientFactory.json,
            mayPublishFlight = { id -> !adHocFlights.hasPendingIdentity(id) },
            onAcknowledged = { id, before, after ->
                flightArrivals.onPriorWriteAcknowledged(id, before, after)
                flightServices.onPriorWriteAcknowledged(id, before, after)
                flightTasks.onPriorWriteAcknowledged(id, before, after)
                flightDepartures.onPriorWriteAcknowledged(id, before, after)
            })
    }

    val adHocFlights: com.nags.operations.data.repo.AdHocFlightsRepository by lazy {
        val api = com.nags.operations.data.api.AdHocFlightsApi(httpClient)
        com.nags.operations.data.repo.AdHocFlightsRepository(catalogDatabase.adHocFlightDao(), logoOwners,
            currentOwner = { if (tokenStore.getAccessToken().isNullOrBlank()) null else sharedCatalogOwnerKey(HttpClientFactory.baseUrl, tokenStore.getSessionSubject()) },
            currentUser = tokenStore::getSessionSubject, currentDisplayName = tokenStore::getDisplayName,
            online = { networkMonitor.isOnline.value }, fetchContext = {
                val read = flightsRepository.beginExternalScopeRead()
                api.context().also { context -> flightsRepository.confirmExternalPage(FlightPage(emptyList(), stationId = context.stationId,
                    asOfUtc = context.asOfUtc, windowStartUtc = context.windowStartUtc, windowEndUtc = context.windowEndUtc), read) }
            }, create = api::create, update = api::update,
            gate = flightOperationsSyncMutex, commit = flightOperationsBatches::savePlanning,
            holdWork = flightOperationsBatches::holdForPlanning, releaseWork = flightOperationsBatches::releaseAfterPlanning,
            holdWorkForReview = flightOperationsBatches::holdPlanningWorkForReview,
            onLocalSaved = { discoveryFlights.recordLocalSave(it) }, onAcknowledgedDetail = flightsRepository::recordPlanningAcknowledged,
            removeWork = flightOperationsBatches::removeUnsentPlanningWork,
            hasWork = { id ->
                val owner = if (tokenStore.getAccessToken().isNullOrBlank()) null else sharedCatalogOwnerKey(HttpClientFactory.baseUrl, tokenStore.getSessionSubject())
                owner != null && (catalogDatabase.flightArrivalDao().rows(owner).any { it.flightId == id } ||
                    catalogDatabase.flightServiceDao().rows(owner).any { it.flightId == id } ||
                    catalogDatabase.flightTaskDao().rows(owner).any { it.flightId == id } ||
                    catalogDatabase.flightDepartureDao().rows(owner).any { it.flightId == id })
            }, transaction = { work -> catalogDatabase.withTransaction { work() } },
            schedule = { FlightArrivalSyncWorker.schedule(appContext); appScope.launch { syncFlightOperations() } },
            onPlanningAcknowledged = { id, before, after ->
                flightArrivals.onPriorWriteAcknowledged(id, before, after)
                flightDepartures.onPriorWriteAcknowledged(id, before, after)
                flightServices.onPriorWriteAcknowledged(id, before, after)
                flightTasks.onPriorWriteAcknowledged(id, before, after)
                flightWriteAcknowledgments++
            })
    }

    val discoveryFlights: com.nags.operations.data.repo.DiscoveryFlightsRepository by lazy {
        val api = com.nags.operations.data.api.DiscoveryFlightsApi(httpClient)
        com.nags.operations.data.repo.DiscoveryFlightsRepository(catalogDatabase.flightDiscoveryDao(), logoOwners,
            currentOwner = { if (tokenStore.getAccessToken().isNullOrBlank()) null else sharedCatalogOwnerKey(HttpClientFactory.baseUrl, tokenStore.getSessionSubject()) },
            online = { networkMonitor.isOnline.value }, fetchPage = api::page, fetchDetail = operationsApi::flight, fetchOfflinePage = api::offlinePage,
            prepareOffline = ::prepareFlightOffline, transaction = { work -> catalogDatabase.withTransaction { work() } },
            beginScopeRead = flightsRepository::beginExternalScopeRead, confirmScopeRead = flightsRepository::confirmExternalPage,
            localStationAuthority = { station -> flightsRepository.trustedCachedStation() == station || adHocFlights.cachedContext()?.stationId == station },
            hasPendingWork = { id -> id in pendingFlightOperationIds() }, isUnmaterializedLocalFlight = { adHocFlights.isUnmaterialized(it) },
            onFlightAccessLost = flightsRepository::recordExternalAccessLoss,
            beginLogoRead = customerLogos::beginMetadataRead,
            reconcileLogos = { items, read -> customerLogos.reconcileFlights(items.mapNotNull { item -> item.customerId?.let { it to item.customerLogoVersion } }.toMap(), read, complete = false) },
            prepareLogos = customerLogos::prefetch)
    }

    private suspend fun pendingFlightOperationIds(): Set<String> {
        val owner = if (tokenStore.getAccessToken().isNullOrBlank()) null else sharedCatalogOwnerKey(HttpClientFactory.baseUrl, tokenStore.getSessionSubject())
        if (owner == null) return emptySet()
        return (catalogDatabase.flightArrivalDao().rows(owner).map { it.flightId } + catalogDatabase.flightDepartureDao().rows(owner).map { it.flightId } +
            catalogDatabase.flightServiceDao().rows(owner).map { it.flightId } + catalogDatabase.flightTaskDao().rows(owner).map { it.flightId } +
            catalogDatabase.flightCancellationDao().rows(owner).map { it.flightId } + catalogDatabase.adHocFlightDao().rows(owner)
                .filter { it.status != "acknowledged" }.map { it.flightId }).toSet()
    }

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
            try { adHocFlights.refreshContext() }
            catch (cancelled: CancellationException) { throw cancelled }
            catch (_: Exception) { /* The prior granted context remains usable unless access was denied. */ }
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
        appScope.launch { adHocFlights.localChanges.collect { flightsRepository.notifyLocalChanges() } }
        appScope.launch { discoveryFlights.changes.collect { flightsRepository.notifyLocalChanges() } }
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

    suspend fun signOut() = flightOperationsSyncMutex.withLock {
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
            adHocFlights.clear()
            discoveryFlights.clear()
            flightOperationsBatches.clear()
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
        flightCancellations.hasSavedInput(id) || flightsRepository.hasCachedCancellationForSave(id)

    private suspend fun cacheAcknowledgedSignature(id: String, saved: com.nags.operations.data.FlightDepartureSaved, bytes: ByteArray) {
        departureSignaturePreviews.recordAcknowledged(id, saved, bytes)
    }

    private suspend fun persistArrivalAcknowledgment(id: String, before: String, after: String) {
        flightServices.onPriorWriteAcknowledged(id, before, after)
        flightTasks.onPriorWriteAcknowledged(id, before, after)
        flightDepartures.onArrivalAcknowledged(id, before, after)
    }

    /** Keep a multi-section local save together while each repository commits its durable intent. */
    suspend fun saveFlightOperationBatch(save: suspend () -> Unit) = flightOperationsSyncMutex.withLock { flightOperationsBatches.save(save) }

    suspend fun saveReviewedFlightOperation(flight: FlightDetail, target: FlightOperationReviewTarget, save: suspend () -> Unit) =
        flightOperationsSyncMutex.withLock { flightOperationsBatches.saveReviewed(flight, target, save) }

    suspend fun discardReviewedFlightOperation(flightId: String, target: FlightOperationReviewTarget) =
        flightOperationsSyncMutex.withLock { flightOperationsBatches.discardReviewed(flightId, target) }

    /** Relax actual bounds before work, then synchronize work before tightening actual bounds. */
    suspend fun syncFlightOperations(): Boolean {
        if (!secureStorageReady) return true
        return flightOperationsSyncMutex.withLock {
            // A late parent ACK can unblock a service skipped earlier in this pass. Continue
            // only after acknowledged progress; transient failures use the worker's backoff.
            suspend fun <T> legacyDrain(drain: suspend () -> T): T {
                flightOperationsBatches.adoptLegacyPending()
                return drain()
            }
            val adHocPending = adHocFlights.syncPending()
            var previousAcknowledgments: Long
            do {
                previousAcknowledgments = flightWriteAcknowledgments
                legacyDrain { flightArrivals.syncPending(priorToServices = true) }
                legacyDrain { flightDepartures.syncPending(priorToServices = true) }
                val servicesPending = legacyDrain { flightServices.syncPending() }
                val tasksPending = legacyDrain { flightTasks.syncPending() }
                val arrivalsPending = legacyDrain { flightArrivals.syncPending() }
                val departuresPending = legacyDrain { flightDepartures.syncPending() }
                val batchesPending = flightOperationsBatches.syncPending()
                val cancellationsPending = legacyDrain { flightCancellations.syncPending() }
                if (adHocPending || arrivalsPending || servicesPending || tasksPending || departuresPending || batchesPending || cancellationsPending) return@withLock true
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

    /** Persist replaceable remote media separately from the durable upload queues. */
    private suspend fun prepareFlightOffline(flight: FlightDetail) {
        for (service in flight.services) serviceAttachmentPreviews.prefetch(flight.id, service.id, service.attachments)
        for (task in flight.tasks) taskAttachmentPreviews.prefetch(flight.id, task.id, task.attachments)
        departureSignaturePreviews.prefetch(flight)
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

    private suspend fun prepareForAccountSwitch() = flightOperationsSyncMutex.withLock {
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
            adHocFlights.clear()
            discoveryFlights.clear()
            flightOperationsBatches.clear()
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
