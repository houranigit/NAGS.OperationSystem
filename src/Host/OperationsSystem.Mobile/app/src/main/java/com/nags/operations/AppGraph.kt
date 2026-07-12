package com.nags.operations

import android.content.Context
import com.nags.operations.data.TokenStore
import com.nags.operations.data.api.AuthApi
import com.nags.operations.data.api.HttpClientFactory
import com.nags.operations.data.api.MobileApi
import com.nags.operations.data.db.AppDatabase
import com.nags.operations.data.network.NetworkMonitor
import com.nags.operations.data.outbox.OutboxWorker
import com.nags.operations.data.outbox.OutboxWorkScheduler
import com.nags.operations.data.outbox.WorkOrderOutboxRepository
import com.nags.operations.data.realtime.RealtimeChannel
import com.nags.operations.data.repo.AuthRepository
import com.nags.operations.data.repo.CatalogsRepository
import com.nags.operations.data.repo.EmployeesRepository
import com.nags.operations.data.repo.FlightsRepository
import com.nags.operations.data.repo.WorkOrderDraftsRepository
import com.nags.operations.data.sync.SyncCoordinator
import com.nags.operations.data.sync.SyncScheduler
import com.nags.operations.data.sync.SyncTable
import io.ktor.client.HttpClient
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

/**
 * Tiny hand-rolled DI container so screens / ViewModels can resolve everything
 * they need without pulling in Hilt for a project this size. One instance per
 * process; it's safe to capture inside `remember { }` blocks.
 *
 * The container also owns a single process-lifetime [CoroutineScope] that the
 * sync scheduler and any other long-lived coroutines (network monitor flow,
 * future SignalR subscription) attach to. This scope is never cancelled — the
 * OS reclaims it on process death.
 */
class AppGraph private constructor(context: Context) {

    /** Process-lifetime supervisor scope. Errors in one job don't tear down the others. */
    val appScope: CoroutineScope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    val tokenStore: TokenStore = TokenStore(context.applicationContext)

    private val httpClient: HttpClient = HttpClientFactory.create(tokenStore)
    private val authApi: AuthApi = AuthApi(tokenStore, httpClient)
    val mobileApi: MobileApi = MobileApi(tokenStore, httpClient)

    val database: AppDatabase = AppDatabase.get(context.applicationContext)

    val catalogsRepository: CatalogsRepository = CatalogsRepository(database)
    val employeesRepository: EmployeesRepository = EmployeesRepository(database)
    val flightsRepository: FlightsRepository = FlightsRepository(database)
    val workOrderDraftsRepository: WorkOrderDraftsRepository = WorkOrderDraftsRepository(database)
    private val outboxWorkScheduler = OutboxWorkScheduler(context.applicationContext)
    val workOrderOutboxRepository: WorkOrderOutboxRepository =
        WorkOrderOutboxRepository(
            context = context.applicationContext,
            db = database,
            onPendingWork = outboxWorkScheduler::scheduleAndAwait,
        )

    val networkMonitor: NetworkMonitor = NetworkMonitor(context.applicationContext, appScope)

    val syncCoordinator: SyncCoordinator = SyncCoordinator(
        api = mobileApi,
        db = database,
        tokenStore = tokenStore,
        outboxRepository = workOrderOutboxRepository,
    )

    private val syncScheduler: SyncScheduler = SyncScheduler(
        tokenStore = tokenStore,
        networkMonitor = networkMonitor,
        coordinator = syncCoordinator,
        appScope = appScope,
    )

    /**
     * SignalR-driven push channel for near-instant updates. We let the channel
     * own its own connect/reconnect loop; it reads JWT + connectivity off the
     * same flows as the scheduler so they self-pause in lock-step on logout
     * or offline.
     */
    val realtimeChannel: RealtimeChannel = RealtimeChannel(
        tokenStore = tokenStore,
        networkMonitor = networkMonitor,
        coordinator = syncCoordinator,
        mobileApi = mobileApi,
        appScope = appScope,
        getCursors = {
            val rows = database.syncStateDao().snapshot()
            // Map storage-key rows to the wire-side table identifiers expected
            // by the catch-up endpoint. Tables with no cursor get a null entry
            // so the channel can still mention them in its catch-up call.
            val byStorageKey = rows.associate { it.tableName to it.cursor }
            mapOf(
                "flights" to byStorageKey[SyncTable.Flights.storageKey],
                "flights-per-landing" to byStorageKey[SyncTable.PerLandingFlights.storageKey],
                "flights-ad-hoc" to byStorageKey[SyncTable.AdHocFlights.storageKey],
                "employees" to byStorageKey[SyncTable.Employees.storageKey],
                "services" to byStorageKey[SyncTable.Services.storageKey],
                "tools" to byStorageKey[SyncTable.Tools.storageKey],
                "materials" to byStorageKey[SyncTable.Materials.storageKey],
                "general-supports" to byStorageKey[SyncTable.GeneralSupports.storageKey],
                "customers" to byStorageKey[SyncTable.Customers.storageKey],
                "aircraft-types" to byStorageKey[SyncTable.AircraftTypes.storageKey],
            )
        },
    )

    /**
     * Drains the offline work-order outbox into the v2 mobile API as soon as
     * `signedIn AND online AND hasPending` line up — same eager-start, self-
     * suspending discipline as [syncScheduler] and [realtimeChannel].
     */
    val outboxWorker: OutboxWorker = OutboxWorker(
        tokenStore = tokenStore,
        networkMonitor = networkMonitor,
        outboxRepository = workOrderOutboxRepository,
        mobileApi = mobileApi,
        syncCoordinator = syncCoordinator,
        appScope = appScope,
    )

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
            secureStorageReady = true
            resumeSessionServices()
        }
    }

    /**
     * Stops every authenticated job before revoking credentials. Same-user cache, drafts, and
     * queued work are deliberately retained; a later login with a different JWT subject runs the
     * account-switch cleanup before that new token is published.
     */
    suspend fun signOut() {
        sessionTransitionMutex.withLock {
            pauseSessionServices()
            authRepository.logout()
        }
    }

    /** Convenience for screens / view-models that want to trigger an on-demand refresh. */
    fun refreshNow() = syncScheduler.refreshNow()

    /** Single-activity foreground hook; an unchanged token/network pair otherwise emits nothing. */
    fun onForeground() {
        if (secureStorageReady) syncScheduler.refreshNow()
    }

    private suspend fun prepareForAccountSwitch() {
        sessionTransitionMutex.withLock {
            pauseSessionServices()
            // Make the old bearer unobservable before deleting its local rows. If cleanup fails,
            // saveLogin propagates the failure and never publishes the new account.
            tokenStore.clearTokens()
            syncCoordinator.clearForAccountSwitch()
            tokenStore.clearAll()
        }
    }

    private suspend fun pauseSessionServices() {
        workOrderOutboxRepository.pauseEnqueues()
        outboxWorker.stop()
        outboxWorkScheduler.pauseAndCancel()
        syncScheduler.stop()
        realtimeChannel.stop()
    }

    private suspend fun resumeSessionServices() {
        sessionTransitionMutex.withLock {
            workOrderOutboxRepository.resumeEnqueues()
            syncScheduler.start()
            realtimeChannel.start()
            outboxWorker.start()
            if (tokenStore.getAccessToken() != null) {
                outboxWorkScheduler.resumeAndSchedule()
            }
        }
    }

    companion object {
        @Volatile private var instance: AppGraph? = null

        fun get(context: Context): AppGraph {
            return instance ?: synchronized(this) {
                instance ?: AppGraph(context.applicationContext).also { instance = it }
            }
        }
    }
}
