package com.nags.operations.ui.home

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.WindowInsetsSides
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.navigationBars
import androidx.compose.foundation.layout.only
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.ScaffoldDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import com.nags.operations.AppGraph
import com.nags.operations.data.notifications.NotificationOpenRequest
import com.nags.operations.ui.AppViewModelFactory
import com.nags.operations.ui.components.AppHeader
import com.nags.operations.ui.components.NotificationPermissionPrompt
import com.nags.operations.ui.flights.FlightsScreen
import com.nags.operations.ui.flights.FlightsViewModel

@Composable
fun MainShellScreen(
    graph: AppGraph,
    factory: AppViewModelFactory,
    onOpenNotifications: () -> Unit,
    onOpenAccount: () -> Unit,
    notificationOpenRequest: NotificationOpenRequest? = null,
    onNotificationHandled: (String?) -> Unit = {},
) {
    val displayName by graph.tokenStore.displayNameFlow.collectAsStateWithLifecycle(initialValue = null)
    val unreadNotifications by graph.notificationsRepository.observeUnreadCount()
        .collectAsStateWithLifecycle(initialValue = 0)
    val subject by graph.tokenStore.sessionSubjectFlow.collectAsStateWithLifecycle(initialValue = null)
    val flights: FlightsViewModel = viewModel(key = "flights:$subject", factory = factory)
    val flightState by flights.state.collectAsStateWithLifecycle()
    var selectedPage by rememberSaveable { mutableIntStateOf(0) }
    var selectedFlightTab by rememberSaveable { mutableIntStateOf(0) }

    LaunchedEffect(notificationOpenRequest) {
        if (notificationOpenRequest != null) {
            flights.closeSearch()
            selectedPage = 0
            selectedFlightTab = 0
        }
    }
    BackHandler(enabled = !flightState.isSearchOpen && (selectedPage != 0 || selectedFlightTab != 0)) {
        if (selectedPage != 0) selectedPage = 0 else selectedFlightTab = 0
    }
    NotificationPermissionPrompt()
    Scaffold(
        containerColor = MaterialTheme.colorScheme.surface,
        contentWindowInsets = ScaffoldDefaults.contentWindowInsets.only(WindowInsetsSides.Horizontal),
    ) { padding ->
        Column(Modifier.fillMaxSize().padding(padding)) {
            AppHeader(displayName = displayName, onProfileClick = onOpenAccount,
                onNotificationsClick = onOpenNotifications, unreadNotifications = unreadNotifications)
            Box(Modifier.weight(1f).fillMaxWidth()
                .windowInsetsPadding(WindowInsets.navigationBars.only(WindowInsetsSides.Bottom))) {
                if (selectedPage == 0) {
                    FlightsScreen(viewModel = flights, selectedTab = selectedFlightTab,
                        onSelectTab = { selectedFlightTab = it },
                        notificationOpenRequest = notificationOpenRequest,
                        onNotificationHandled = onNotificationHandled)
                }
                if (!flightState.isSearchOpen) {
                    MainBottomNavigation(selectedPage = selectedPage, onSelectPage = { selectedPage = it },
                        onSearch = {
                            selectedPage = 0
                            flights.openSearch()
                        },
                        modifier = Modifier.align(Alignment.BottomCenter).padding(bottom = 10.dp))
                }
            }
        }
    }
}
