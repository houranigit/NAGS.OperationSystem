package com.nags.operations.ui

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import com.nags.operations.AppGraph
import com.nags.operations.ui.adhoc.AdHocFlightsViewModel
import com.nags.operations.ui.perlanding.PerLandingFlightsViewModel
import com.nags.operations.ui.flights.MyFlightsViewModel
import com.nags.operations.ui.login.LoginViewModel
import com.nags.operations.ui.sync.SyncCenterViewModel
import com.nags.operations.ui.workorder.WorkOrderDraftsViewModel
import com.nags.operations.ui.notifications.NotificationsViewModel

/**
 * Resolves ViewModels from [AppGraph]. Routes use this through Compose's
 * `viewModel { }` so each NavBackStackEntry gets its own scoped instance.
 */
class AppViewModelFactory(private val graph: AppGraph) : ViewModelProvider.Factory {

    @Suppress("UNCHECKED_CAST")
    override fun <T : ViewModel> create(modelClass: Class<T>): T {
        return when (modelClass) {
            LoginViewModel::class.java -> LoginViewModel(graph.authRepository)
            MyFlightsViewModel::class.java -> MyFlightsViewModel(
                repository = graph.flightsRepository,
                draftsRepository = graph.workOrderDraftsRepository,
                outboxRepository = graph.workOrderOutboxRepository,
                coordinator = graph.syncCoordinator,
                mobileApi = graph.mobileApi,
                networkMonitor = graph.networkMonitor,
            )
            NotificationsViewModel::class.java -> NotificationsViewModel(
                repository = graph.notificationsRepository,
            )
            WorkOrderDraftsViewModel::class.java -> WorkOrderDraftsViewModel(
                draftsRepository = graph.workOrderDraftsRepository,
            )
            PerLandingFlightsViewModel::class.java -> PerLandingFlightsViewModel(
                repository = graph.flightsRepository,
                draftsRepository = graph.workOrderDraftsRepository,
                outboxRepository = graph.workOrderOutboxRepository,
                coordinator = graph.syncCoordinator,
                networkMonitor = graph.networkMonitor,
            )
            AdHocFlightsViewModel::class.java -> AdHocFlightsViewModel(
                repository = graph.flightsRepository,
                draftsRepository = graph.workOrderDraftsRepository,
                outboxRepository = graph.workOrderOutboxRepository,
                coordinator = graph.syncCoordinator,
                networkMonitor = graph.networkMonitor,
            )
            SyncCenterViewModel::class.java -> SyncCenterViewModel(
                database = graph.database,
                coordinator = graph.syncCoordinator,
                networkMonitor = graph.networkMonitor,
                realtimeChannel = graph.realtimeChannel,
                outboxRepository = graph.workOrderOutboxRepository,
                outboxWorker = graph.outboxWorker,
            )
            else -> throw IllegalArgumentException("Unknown ViewModel ${modelClass.name}")
        } as T
    }
}
