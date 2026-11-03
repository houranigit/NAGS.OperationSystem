package com.nags.operations.ui.home

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.WindowInsetsSides
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.only
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.ScaffoldDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
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
    onLogout: () -> Unit,
    onOpenNotifications: () -> Unit,
    onOpenAccount: () -> Unit,
    onOpenSyncCenter: () -> Unit,
    notificationOpenRequest: NotificationOpenRequest? = null,
    onNotificationHandled: (String?) -> Unit = {},
) {
    val displayName by graph.tokenStore.displayNameFlow.collectAsStateWithLifecycle(initialValue = null)
    val unreadNotifications by graph.notificationsRepository.observeUnreadCount()
        .collectAsStateWithLifecycle(initialValue = 0)
    val isOnline by graph.networkMonitor.isOnline.collectAsStateWithLifecycle()
    val isSyncing by graph.sharedCatalogsRepository.isSyncing.collectAsStateWithLifecycle()
    val subject by graph.tokenStore.sessionSubjectFlow.collectAsStateWithLifecycle(initialValue = null)
    val flights: FlightsViewModel = viewModel(key = "flights:$subject", factory = factory)
    NotificationPermissionPrompt()
    Scaffold(
        containerColor = MaterialTheme.colorScheme.background,
        contentWindowInsets = ScaffoldDefaults.contentWindowInsets.only(WindowInsetsSides.Horizontal),
    ) { padding ->
        Column(Modifier.fillMaxSize().padding(padding)) {
            AppHeader(displayName = displayName, onLogout = onLogout,
                onNotificationsClick = onOpenNotifications, onProfileClick = onOpenAccount,
                unreadNotifications = unreadNotifications, onSyncCenterClick = onOpenSyncCenter,
                isOnline = isOnline, isSyncing = isSyncing)
            FlightsScreen(viewModel = flights, notificationOpenRequest = notificationOpenRequest,
                onNotificationHandled = onNotificationHandled)
        }
    }
}
