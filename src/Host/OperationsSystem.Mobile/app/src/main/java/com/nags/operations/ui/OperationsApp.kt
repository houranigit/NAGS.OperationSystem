package com.nags.operations.ui

import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import com.nags.operations.AppGraph
import com.nags.operations.ui.account.AccountScreen
import com.nags.operations.ui.account.AccountViewModel
import com.nags.operations.ui.home.MainShellScreen
import com.nags.operations.ui.flights.FlightSearchScreen
import com.nags.operations.ui.login.LoginScreen
import com.nags.operations.ui.login.LoginViewModel
import com.nags.operations.ui.notifications.NotificationsScreen
import com.nags.operations.ui.notifications.NotificationsViewModel
import com.nags.operations.ui.sync.SyncCenterScreen
import com.nags.operations.ui.sync.SyncCenterViewModel
import kotlinx.coroutines.launch

private object Routes {
    const val Login = "login"
    const val Flights = "flights"
    const val Search = "search"
    const val Notifications = "notifications"
    const val Account = "account"
    const val SyncCenter = "sync"
}

@Composable
fun OperationsApp() {
    val context = LocalContext.current
    val graph = remember { AppGraph.get(context) }
    val factory = remember(graph) { AppViewModelFactory(graph) }
    val navController = rememberNavController()
    val scope = rememberCoroutineScope()
    val accessToken by graph.tokenStore.accessTokenFlow.collectAsStateWithLifecycle(initialValue = null)
    val pendingNotification by graph.notificationNavigation.pending.collectAsStateWithLifecycle(initialValue = null)
    val subject by graph.tokenStore.sessionSubjectFlow.collectAsStateWithLifecycle(initialValue = null)

    fun navigateToLogin() {
        if (navController.currentDestination?.route == Routes.Login) return
        navController.navigate(Routes.Login) {
            popUpTo(navController.graph.id) { inclusive = true }
            launchSingleTop = true
        }
    }
    fun signOut() {
        scope.launch {
            graph.signOut()
            navigateToLogin()
        }
    }

    LaunchedEffect(accessToken) {
        if (accessToken == null && navController.currentDestination?.route != null) navigateToLogin()
    }
    LaunchedEffect(pendingNotification, accessToken) {
        val request = pendingNotification ?: return@LaunchedEffect
        if (accessToken == null) return@LaunchedEffect
        if (!request.recipientUserId.isNullOrBlank() &&
            !request.recipientUserId.equals(graph.tokenStore.getSessionSubject(), ignoreCase = true)) {
            graph.notificationNavigation.discardForWrongAccount()
            return@LaunchedEffect
        }
        request.notificationId?.let { id ->
            graph.appScope.launch { runCatching { graph.notificationsRepository.markRead(id) } }
        }
        if (navController.currentDestination?.route != Routes.Flights &&
            !navController.popBackStack(Routes.Flights, inclusive = false)) {
            navController.navigate(Routes.Flights) {
                popUpTo(navController.graph.id) { inclusive = true }
                launchSingleTop = true
            }
        }
    }
    NavHost(navController = navController, startDestination = Routes.Login,
        enterTransition = { fadeIn(tween(220)) }, exitTransition = { fadeOut(tween(220)) },
        popEnterTransition = { fadeIn(tween(220)) }, popExitTransition = { fadeOut(tween(220)) }) {
        composable(Routes.Login) {
            val model: LoginViewModel = viewModel(factory = factory)
            LoginScreen(viewModel = model, tokenStore = graph.tokenStore, onLoggedIn = {
                navController.navigate(Routes.Flights) {
                    popUpTo(Routes.Login) { inclusive = true }
                    launchSingleTop = true
                }
            })
        }
        composable(Routes.Flights) {
            MainShellScreen(graph = graph, factory = factory,
                onOpenNotifications = { navController.navigate(Routes.Notifications) },
                onOpenAccount = { navController.navigate(Routes.Account) },
                onOpenSearch = { navController.navigate(Routes.Search) { launchSingleTop = true } },
                notificationOpenRequest = pendingNotification,
                onNotificationHandled = graph.notificationNavigation::consume)
        }
        composable(Routes.Search,
            enterTransition = { slideInHorizontally(tween(280)) { it } },
            exitTransition = { fadeOut(tween(160)) },
            popEnterTransition = { fadeIn(tween(160)) },
            popExitTransition = { slideOutHorizontally(tween(280)) { it } }) {
            FlightSearchScreen(onBack = { navController.popBackStack() })
        }
        composable(Routes.Account) {
            val model: AccountViewModel = viewModel(key = "account:$subject", factory = factory)
            AccountScreen(viewModel = model, onBack = { navController.popBackStack() }, onLogout = ::signOut,
                onOpenSyncCenter = { navController.navigate(Routes.SyncCenter) })
        }
        composable(Routes.SyncCenter) {
            val model: SyncCenterViewModel = viewModel(key = "sync:$subject", factory = factory)
            SyncCenterScreen(viewModel = model, onBack = { navController.popBackStack() })
        }
        composable(Routes.Notifications) {
            val model: NotificationsViewModel = viewModel(key = "notifications:$subject", factory = factory)
            NotificationsScreen(viewModel = model, onBack = { navController.popBackStack() },
                onOpenFlight = graph.notificationNavigation::publish,
                onOpenSchedule = graph.notificationNavigation::publish)
        }
    }
}
