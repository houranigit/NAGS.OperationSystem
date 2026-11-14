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
import androidx.compose.material3.ModalNavigationDrawer
import androidx.compose.material3.ModalDrawerSheet
import androidx.compose.material3.DrawerValue
import androidx.compose.material3.rememberDrawerState
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.platform.LocalContext
import com.nags.operations.ui.account.ProfileMenuContent
import com.nags.operations.ui.account.openNotificationPreferences
import kotlinx.coroutines.launch
import androidx.compose.material3.Scaffold
import androidx.compose.material3.ScaffoldDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.key
import androidx.compose.ui.layout.SubcomposeLayout
import androidx.compose.ui.layout.SubcomposeLayoutState
import androidx.compose.ui.layout.SubcomposeSlotReusePolicy
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.saveable.rememberSaveableStateHolder
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
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
import com.nags.operations.ui.notifications.NotificationsScreen
import com.nags.operations.ui.notifications.NotificationsViewModel

@Composable
fun MainShellScreen(
    graph: AppGraph,
    factory: AppViewModelFactory,
    subject: String?,
    onCreateAdHoc: () -> Unit,
    onOpenAccount: () -> Unit,
    onOpenSyncCenter: () -> Unit,
    onOpenPassword: () -> Unit,
    onLogout: () -> Unit,
    externalPendingFlightId: String? = null,
    externalPendingNavigationKey: String? = null,
    onExternalPendingHandled: () -> Unit = {},
    notificationOpenRequest: NotificationOpenRequest? = null,
    onNotificationHandled: (String?) -> Unit = {},
) {
    val drawer = rememberDrawerState(DrawerValue.Closed)
    val drawerScope = rememberCoroutineScope()
    val context = LocalContext.current
    var pendingMenuAction by remember { mutableStateOf<(() -> Unit)?>(null) }
    fun openMenuDestination(action: () -> Unit) {
        pendingMenuAction = action
        drawerScope.launch { drawer.close() }
    }
    // Dispatch separately from the close animation: a competing drawer gesture may cancel that animation.
    LaunchedEffect(drawer.isClosed, pendingMenuAction) {
        if (drawer.isClosed) {
            val action = pendingMenuAction
            pendingMenuAction = null
            action?.invoke()
        }
    }
    val displayName by graph.tokenStore.displayNameFlow.collectAsStateWithLifecycle(initialValue = null)
    val unreadNotifications by graph.notificationsRepository.observeUnreadCount()
        .collectAsStateWithLifecycle(initialValue = 0)
    val flights: FlightsViewModel = viewModel(key = "flights:$subject", factory = factory)
    val flightState by flights.state.collectAsStateWithLifecycle()
    var selectedPage by rememberSaveable(subject) { mutableIntStateOf(0) }
    var selectedFlightTab by rememberSaveable(subject) { mutableIntStateOf(0) }
    val pageState = rememberSaveableStateHolder()
    // Reuse page nodes without keeping inactive page effects or BackHandlers alive.
    val pageLayoutState = remember(subject) {
        SubcomposeLayoutState(object : SubcomposeSlotReusePolicy {
            override fun getSlotsToRetain(slotIds: SubcomposeSlotReusePolicy.SlotIdsSet) {
                slotIds.trimToSize(5)
            }
            override fun areCompatible(slotId: Any?, reusableSlotId: Any?) = slotId == reusableSlotId
        })
    }
    val focusManager = LocalFocusManager.current
    val keyboard = LocalSoftwareKeyboardController.current
    val pageInteractionEnabled = drawer.currentValue == DrawerValue.Closed && drawer.targetValue == DrawerValue.Closed
    val flightsSearchOpen = selectedPage == 0 && flightState.isSearchOpen
    var navigationHeightPx by remember { mutableIntStateOf(0) }
    val navigationHeight = with(LocalDensity.current) { navigationHeightPx.toDp() }

    fun selectPage(page: Int) {
        if (page == selectedPage) return
        keyboard?.hide()
        focusManager.clearFocus()
        selectedPage = page
    }

    LaunchedEffect(externalPendingFlightId, externalPendingNavigationKey) {
        if (externalPendingFlightId != null && externalPendingNavigationKey != null) {
            keyboard?.hide(); focusManager.clearFocus()
            flights.closeSearch()
            selectedPage = 0
        }
    }
    LaunchedEffect(notificationOpenRequest) {
        if (notificationOpenRequest != null) {
            pendingMenuAction = null
            drawerScope.launch { drawer.close() }
            keyboard?.hide()
            focusManager.clearFocus()
            flights.closeSearch()
            selectedPage = 0
            selectedFlightTab = 0
        }
    }
    BackHandler(enabled = pageInteractionEnabled && !(selectedPage == 0 && (flightState.isSearchOpen || flightState.selectedFlightId != null)) &&
        (selectedPage != 0 || selectedFlightTab != 0)) {
        if (selectedPage != 0) selectPage(0) else selectedFlightTab = 0
    }
    NotificationPermissionPrompt()
    ModalNavigationDrawer(drawerState = drawer, gesturesEnabled = drawer.isOpen,
        drawerContent = {
            ModalDrawerSheet(drawerState = drawer, modifier = Modifier.widthIn(max = 360.dp).fillMaxWidth(0.9f),
                drawerShape = RoundedCornerShape(topEnd = 24.dp, bottomEnd = 24.dp),
                drawerContainerColor = MaterialTheme.colorScheme.surface) {
                ProfileMenuContent(displayName, onClose = { drawerScope.launch { drawer.close() } },
                    onProfile = { openMenuDestination(onOpenAccount) },
                    onSync = { openMenuDestination(onOpenSyncCenter) },
                    onPassword = { openMenuDestination(onOpenPassword) },
                    onNotificationSettings = { openMenuDestination { openNotificationPreferences(context) } },
                    onLogout = { openMenuDestination(onLogout) })
            }
        }) {
        Scaffold(
            containerColor = MaterialTheme.colorScheme.surface,
            contentWindowInsets = ScaffoldDefaults.contentWindowInsets.only(WindowInsetsSides.Horizontal),
        ) { padding ->
            Column(Modifier.fillMaxSize().padding(padding)) {
                AppHeader(displayName = displayName, onProfileClick = {
                    keyboard?.hide(); focusManager.clearFocus()
                    drawerScope.launch { drawer.open() }
                },
                    onCreateAdHoc = {
                        keyboard?.hide()
                        focusManager.clearFocus()
                        onCreateAdHoc()
                    })
                Box(Modifier.weight(1f).fillMaxWidth()
                    .windowInsetsPadding(WindowInsets.navigationBars.only(WindowInsetsSides.Bottom))) {
                    key(subject) {
                        SubcomposeLayout(state = pageLayoutState, modifier = Modifier.fillMaxSize()) { constraints ->
                            val activePage = selectedPage
                            val childConstraints = constraints.copy(minWidth = 0, minHeight = 0)
                            val placeables = subcompose("$subject:$activePage") {
                                pageState.SaveableStateProvider("$subject:$activePage") {
                                    if (activePage == 0) {
                                        FlightsScreen(viewModel = flights, selectedTab = selectedFlightTab,
                                            onSelectTab = { selectedFlightTab = it },
                                            bottomContentPadding = if (flightsSearchOpen) 80.dp else maxOf(80.dp, navigationHeight + 10.dp),
                                            notificationOpenRequest = notificationOpenRequest,
                                            onNotificationHandled = onNotificationHandled,
                                            externalPendingFlightId = externalPendingFlightId,
                                            externalPendingNavigationKey = externalPendingNavigationKey,
                                            onExternalPendingHandled = onExternalPendingHandled,
                                            interactionEnabled = pageInteractionEnabled)
                                    } else if (activePage == 4) {
                                        val notifications: NotificationsViewModel = viewModel(key = "notifications:$subject", factory = factory)
                                        NotificationsScreen(viewModel = notifications, onBack = { selectPage(0) },
                                            onOpenFlight = graph.notificationNavigation::publish,
                                            onOpenSchedule = graph.notificationNavigation::publish,
                                            embedded = true, bottomContentPadding = maxOf(80.dp, navigationHeight + 10.dp))
                                    } else if (activePage == 2 || activePage == 3) {
                                        val source = if (activePage == 2) com.nags.operations.data.FlightDiscoverySource.Search
                                            else com.nags.operations.data.FlightDiscoverySource.History
                                        val sourceFactory = remember(factory, source) { factory.forDiscovery(source) }
                                        val sourceFlights: FlightsViewModel = viewModel(key = "discovery:$subject:${source.key}", factory = sourceFactory)
                                        FlightsScreen(viewModel = sourceFlights, discoverySource = source,
                                            interactionEnabled = pageInteractionEnabled,
                                            bottomContentPadding = maxOf(80.dp, navigationHeight + 10.dp))
                                    } else MainShellPage(page = activePage)
                                }
                            }.map { it.measure(childConstraints) }
                            layout(constraints.maxWidth, constraints.maxHeight) {
                                placeables.forEach { it.placeRelative(0, 0) }
                            }
                        }
                    }
                    if (!flightsSearchOpen) {
                        MainBottomNavigation(selectedPage = selectedPage, onSelectPage = ::selectPage,
                            unreadNotifications = unreadNotifications,
                            modifier = Modifier.align(Alignment.BottomCenter).padding(bottom = 10.dp)
                                .onSizeChanged { navigationHeightPx = it.height })
                    }
                }
            }
        }
    }
}
