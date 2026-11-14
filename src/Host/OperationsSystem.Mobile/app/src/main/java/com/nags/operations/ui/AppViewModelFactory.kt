package com.nags.operations.ui

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import com.nags.operations.AppGraph
import com.nags.operations.data.FlightDiscoverySource
import com.nags.operations.ui.account.AccountViewModel
import com.nags.operations.ui.flights.FlightsViewModel
import com.nags.operations.ui.login.LoginViewModel
import com.nags.operations.ui.notifications.NotificationsViewModel
import com.nags.operations.ui.sync.SyncCenterViewModel

class AppViewModelFactory(private val graph: AppGraph) : ViewModelProvider.Factory {
    @Suppress("UNCHECKED_CAST")
    override fun <T : ViewModel> create(modelClass: Class<T>): T = when (modelClass) {
        LoginViewModel::class.java -> LoginViewModel(graph.authRepository)
        FlightsViewModel::class.java -> flights()
        AccountViewModel::class.java -> AccountViewModel(graph.authRepository, graph.networkMonitor)
        SyncCenterViewModel::class.java -> SyncCenterViewModel(
            graph.sharedCatalogsRepository, graph.flightsRepository, graph.networkMonitor, graph.catalogConnectionState,
            graph.lastCatalogEventAt, graph::refreshCatalogs, graph.isSyncing, graph.flightArrivals, graph.flightDepartures, graph.flightCancellations,
            graph.flightServices, graph.flightTasks, adHocRepository = graph.adHocFlights,
            offlineSearchStatuses = graph.discoveryFlights.offlineSearchStatus,
        )
        NotificationsViewModel::class.java -> NotificationsViewModel(graph.notificationsRepository, graph.flightsRepository)
        else -> throw IllegalArgumentException("Unknown ViewModel ${modelClass.name}")
    } as T

    /** Each feed keeps its own list/query state while sharing the existing flight journey. */
    fun forDiscovery(source: FlightDiscoverySource): ViewModelProvider.Factory = object : ViewModelProvider.Factory {
        @Suppress("UNCHECKED_CAST")
        override fun <T : ViewModel> create(modelClass: Class<T>): T {
            require(modelClass == FlightsViewModel::class.java)
            return flights(source) as T
        }
    }

    private fun flights(source: FlightDiscoverySource? = null) = FlightsViewModel(
        graph.flightsRepository, graph.authRepository, graph.networkMonitor, graph.sharedCatalogsRepository, graph.flightArrivals, graph.flightDepartures,
        graph.tokenStore.displayNameFlow, graph.departureSignaturePreviews::load, graph.flightCancellations,
        graph.flightServices, graph::loadServiceAttachment, graph::loadPendingServiceAttachment, graph::saveFlightOperationBatch,
        graph.flightTasks, graph::loadTaskAttachment, graph::loadPendingTaskAttachment,
        graph::saveReviewedFlightOperation, graph::discardReviewedFlightOperation,
        adHocRepository = graph.adHocFlights,
        sourcePage = if (source == null) null else { page, size, query ->
            graph.discoveryFlights.page(source, query.orEmpty(), page, size)
        },
    )
}
