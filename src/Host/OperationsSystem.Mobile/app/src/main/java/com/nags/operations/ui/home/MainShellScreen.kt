package com.nags.operations.ui.home

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.WindowInsetsSides
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.only
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.ScaffoldDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.currentBackStackEntryAsState
import androidx.navigation.compose.rememberNavController
import com.nags.operations.AppGraph
import com.nags.operations.data.TokenStore
import com.nags.operations.ui.AppViewModelFactory
import com.nags.operations.ui.components.AppHeader
import com.nags.operations.ui.components.BottomNavBar
import com.nags.operations.ui.components.BottomNavDestination
import com.nags.operations.ui.components.FlightSheetCallbacks
import com.nags.operations.ui.components.NotificationPermissionPrompt
import com.nags.operations.data.notifications.NotificationOpenRequest
import com.nags.operations.ui.adhoc.AdHocFlightsViewModel
import com.nags.operations.ui.perlanding.PerLandingFlightsViewModel
import com.nags.operations.ui.flights.MyFlightsViewModel
import com.nags.operations.ui.screens.AdHocFlightsTab
import com.nags.operations.ui.screens.PerLandingFlightsTab
import com.nags.operations.ui.screens.MyFlightsTab
import com.nags.operations.ui.screens.WorkOrderDraftsTab
import com.nags.operations.ui.workorder.WorkOrderDraftsViewModel

private fun matchesBottomNavDestination(route: String?, dest: BottomNavDestination): Boolean =
    when (dest) {
        BottomNavDestination.MyFlights -> route == BottomNavDestination.MyFlights.route
        BottomNavDestination.PerLanding -> route == BottomNavDestination.PerLanding.route
        BottomNavDestination.Create -> false
        BottomNavDestination.AdHoc -> route == BottomNavDestination.AdHoc.route
        BottomNavDestination.Drafts -> route == BottomNavDestination.Drafts.route
    }

/**
 * Authenticated shell: branded [AppHeader], bottom navigation, and tab content
 * (My flights / Per Landing / Create / Ad Hoc / Drafts) without a duplicate Material top app bar.
 */
@Composable
fun MainShellScreen(
    tokenStore: TokenStore,
    graph: AppGraph,
    factory: AppViewModelFactory,
    onOpenSyncCenter: () -> Unit,
    onLogout: () -> Unit,
    onOpenWorkOrderDraft: (draftId: String) -> Unit,
    onOpenCreateAdHocFlight: () -> Unit,
    onOpenNotifications: () -> Unit,
    notificationOpenRequest: NotificationOpenRequest? = null,
    onNotificationHandled: (String?) -> Unit = {},
    flightSheetCallbacks: FlightSheetCallbacks = FlightSheetCallbacks(),
) {
    val displayName by tokenStore.displayNameFlow
        .collectAsStateWithLifecycle(initialValue = null)
    val isOnline by graph.networkMonitor.isOnline.collectAsStateWithLifecycle()
    val isSyncing by graph.syncCoordinator.isSyncing.collectAsStateWithLifecycle()
    val unreadNotifications by graph.notificationsRepository.observeUnreadCount()
        .collectAsStateWithLifecycle(initialValue = 0)

    val innerNav = rememberNavController()
    val innerEntry by innerNav.currentBackStackEntryAsState()
    val selectedTab = when (innerEntry?.destination?.route) {
        BottomNavDestination.PerLanding.route -> BottomNavDestination.PerLanding
        BottomNavDestination.AdHoc.route -> BottomNavDestination.AdHoc
        BottomNavDestination.Drafts.route -> BottomNavDestination.Drafts
        else -> BottomNavDestination.MyFlights
    }

    LaunchedEffect(notificationOpenRequest?.notificationId, notificationOpenRequest?.flightId) {
        if (notificationOpenRequest != null &&
            innerEntry?.destination?.route != BottomNavDestination.MyFlights.route
        ) {
            innerNav.navigate(BottomNavDestination.MyFlights.route) {
                popUpTo(BottomNavDestination.MyFlights.route) { inclusive = true }
                launchSingleTop = true
            }
        }
        // MyFlightsTab consumes the request after it has started either the authoritative by-id
        // open or the schedule-level list refresh. Keeping it pending until then also covers a
        // missed SignalR refresh while this tab was already composed.
    }

    NotificationPermissionPrompt()

    Scaffold(
        bottomBar = {
            BottomNavBar(
                selected = selectedTab,
                onSelected = { dest ->
                    if (dest == BottomNavDestination.Create) {
                        onOpenCreateAdHocFlight()
                        return@BottomNavBar
                    }
                    val route = innerEntry?.destination?.route
                    if (!matchesBottomNavDestination(route, dest)) {
                        innerNav.navigate(dest.route) {
                            popUpTo(BottomNavDestination.MyFlights.route) {
                                inclusive = false
                                saveState = true
                            }
                            launchSingleTop = true
                            restoreState = true
                        }
                    }
                },
            )
        },
        containerColor = MaterialTheme.colorScheme.background,
        // Without a topBar, default scaffold top insets leave a gap above [AppHeader]; match Sync Center edge-to-edge.
        contentWindowInsets = ScaffoldDefaults.contentWindowInsets.only(WindowInsetsSides.Horizontal),
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .background(MaterialTheme.colorScheme.background),
        ) {
            AppHeader(
                displayName = displayName,
                onLogout = onLogout,
                onSyncCenterClick = onOpenSyncCenter,
                isOnline = isOnline,
                isSyncing = isSyncing,
                onNotificationsClick = onOpenNotifications,
                unreadNotifications = unreadNotifications,
            )
            NavHost(
                navController = innerNav,
                startDestination = BottomNavDestination.MyFlights.route,
                modifier = Modifier.weight(1f),
            ) {
                composable(BottomNavDestination.MyFlights.route) {
                    val vm: MyFlightsViewModel = viewModel(factory = factory)
                    MyFlightsTab(
                        viewModel = vm,
                        sheetCallbacks = flightSheetCallbacks,
                        requestedFlightRequest = notificationOpenRequest,
                        onRequestedFlightOpened = onNotificationHandled,
                    )
                }
                composable(BottomNavDestination.PerLanding.route) {
                    val vm: PerLandingFlightsViewModel = viewModel(factory = factory)
                    PerLandingFlightsTab(
                        viewModel = vm,
                        sheetCallbacks = flightSheetCallbacks,
                    )
                }
                composable(BottomNavDestination.AdHoc.route) {
                    val vm: AdHocFlightsViewModel = viewModel(factory = factory)
                    AdHocFlightsTab(
                        viewModel = vm,
                        sheetCallbacks = flightSheetCallbacks,
                    )
                }
                composable(BottomNavDestination.Drafts.route) {
                    val vm: WorkOrderDraftsViewModel = viewModel(factory = factory)
                    WorkOrderDraftsTab(
                        viewModel = vm,
                        onOpenDraft = onOpenWorkOrderDraft,
                    )
                }
            }
        }
    }
}
