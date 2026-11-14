package com.nags.operations.ui

import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.ui.res.stringResource
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import com.nags.operations.R
import com.nags.operations.ui.account.PasswordScreen
import com.nags.operations.ui.account.PasswordViewModel
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import com.nags.operations.AppGraph
import com.nags.operations.ui.account.AccountScreen
import com.nags.operations.ui.account.AccountViewModel
import com.nags.operations.ui.home.AdHocFlightShell
import com.nags.operations.ui.home.MainShellScreen
import com.nags.operations.ui.login.LoginScreen
import com.nags.operations.ui.login.LoginViewModel
import com.nags.operations.ui.notifications.NotificationsScreen
import com.nags.operations.ui.notifications.NotificationsViewModel
import com.nags.operations.ui.sync.SyncCenterScreen
import com.nags.operations.ui.sync.SyncCenterViewModel
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.launch

private object Routes {
    const val Login = "login"
    const val Flights = "flights"
    const val Notifications = "notifications"
    const val Account = "account"
    const val SyncCenter = "sync"
    const val Password = "password"
    const val AdHoc = "adhoc-create"
    const val AdHocRecovery = "adhoc-edit/{flightId}"
}

/** A null UI session means DataStore has not emitted yet, rather than a signed-out account. */
private class UiSession(val accessToken: String?, val subject: String?)

@Composable
fun OperationsApp() {
    val context = LocalContext.current
    val graph = remember { AppGraph.get(context) }
    val factory = remember(graph) { AppViewModelFactory(graph) }
    val navController = rememberNavController()
    val scope = rememberCoroutineScope()
    var confirmSignOut by rememberSaveable { mutableStateOf(false) }
    var passwordChanged by rememberSaveable { mutableStateOf(false) }
    var explicitSignInRequired by rememberSaveable { mutableStateOf(false) }
    var pendingReviewFlightId by rememberSaveable { mutableStateOf<String?>(null) }
    var pendingReviewKey by rememberSaveable { mutableStateOf<String?>(null) }
    val cachedName by graph.tokenStore.displayNameFlow.collectAsStateWithLifecycle(initialValue = null)
    val sessionFlow = remember(graph) {
        combine(graph.tokenStore.accessTokenFlow, graph.tokenStore.sessionSubjectFlow) { token, subject -> UiSession(token, subject) }
    }
    val session by sessionFlow.collectAsStateWithLifecycle(initialValue = null)
    val pendingNotification by graph.notificationNavigation.pending.collectAsStateWithLifecycle(initialValue = null)
    // Preserve the restored back stack and account-owned ViewModels while the initial read is pending.
    val loadedSession = session ?: return
    val accessToken = loadedSession.accessToken
    val subject = loadedSession.subject

    fun navigateToLogin() {
        if (navController.currentDestination?.route == Routes.Login) return
        navController.navigate(Routes.Login) {
            popUpTo(navController.graph.id) { inclusive = true }
            launchSingleTop = true
        }
    }
    fun signOut() {
        explicitSignInRequired = true
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
            graph.notificationsRepository.markReadOnOpen(id)
        }
        if (navController.currentDestination?.route != Routes.Flights &&
            !navController.popBackStack(Routes.Flights, inclusive = false)) {
            navController.navigate(Routes.Flights) {
                popUpTo(navController.graph.id) { inclusive = true }
                launchSingleTop = true
            }
        }
    }
    fun openFlightReview(flightId: String, navigationKey: String) {
        pendingReviewFlightId = flightId
        pendingReviewKey = navigationKey
        if (!navController.popBackStack(Routes.Flights, inclusive = false)) {
            navController.navigate(Routes.Flights) { launchSingleTop = true }
        }
    }
    if (confirmSignOut) AlertDialog(onDismissRequest = { confirmSignOut = false },
        title = { Text(stringResource(R.string.account_sign_out)) },
        text = { Text(stringResource(R.string.profile_sign_out_warning)) },
        confirmButton = { TextButton(onClick = { confirmSignOut = false; signOut() }) {
            Text(stringResource(R.string.account_sign_out), color = MaterialTheme.colorScheme.error)
        } }, dismissButton = { TextButton(onClick = { confirmSignOut = false }) { Text(stringResource(R.string.flights_cancel)) } })
    NavHost(navController = navController, startDestination = Routes.Login,
        enterTransition = { fadeIn(tween(220)) }, exitTransition = { fadeOut(tween(220)) },
        popEnterTransition = { fadeIn(tween(220)) }, popExitTransition = { fadeOut(tween(220)) }) {
        composable(Routes.Login) {
            val model: LoginViewModel = viewModel(factory = factory)
            LoginScreen(viewModel = model, accessTokenAvailable = accessToken != null && !explicitSignInRequired,
                noticeMessage = if (passwordChanged) stringResource(R.string.password_changed_sign_in) else null,
                onLoggedIn = {
                    explicitSignInRequired = false
                    passwordChanged = false
                    navController.navigate(Routes.Flights) {
                        popUpTo(Routes.Login) { inclusive = true }
                        launchSingleTop = true
                    }
                })
        }
        composable(Routes.Flights) {
            MainShellScreen(graph = graph, factory = factory, subject = subject,
                onCreateAdHoc = { navController.navigate(Routes.AdHoc) { launchSingleTop = true } },
                onOpenAccount = { navController.navigate(Routes.Account) { launchSingleTop = true } },
                onOpenSyncCenter = { navController.navigate(Routes.SyncCenter) { launchSingleTop = true } },
                onOpenPassword = { navController.navigate(Routes.Password) { launchSingleTop = true } },
                onLogout = { confirmSignOut = true },
                externalPendingFlightId = pendingReviewFlightId,
                externalPendingNavigationKey = pendingReviewKey,
                onExternalPendingHandled = { pendingReviewFlightId = null; pendingReviewKey = null },
                notificationOpenRequest = pendingNotification,
                onNotificationHandled = graph.notificationNavigation::consume)
        }
        composable(Routes.Account) {
            val model: AccountViewModel = viewModel(key = "account:$subject", factory = factory)
            AccountScreen(viewModel = model, onBack = { navController.popBackStack() }, onLogout = { confirmSignOut = true },
                onOpenSyncCenter = { navController.navigate(Routes.SyncCenter) { launchSingleTop = true } },
                onOpenPassword = { navController.navigate(Routes.Password) { launchSingleTop = true } }, cachedName = cachedName)
        }
        composable(Routes.Password) {
            val passwordFactory = remember(graph) { object : ViewModelProvider.Factory {
                @Suppress("UNCHECKED_CAST")
                override fun <T : ViewModel> create(modelClass: Class<T>): T =
                    PasswordViewModel(graph.authRepository, graph.networkMonitor) as T
            } }
            val model: PasswordViewModel = viewModel(key = "password:$subject", factory = passwordFactory)
            PasswordScreen(model, onBack = { navController.popBackStack() }, onChanged = {
                explicitSignInRequired = true
                passwordChanged = true
                scope.launch {
                    graph.authRepository.requireSignInAfterPasswordChange()
                    navigateToLogin()
                }
            })
        }
        composable(Routes.SyncCenter) {
            val model: SyncCenterViewModel = viewModel(key = "sync:$subject", factory = factory)
            SyncCenterScreen(viewModel = model, onBack = { navController.popBackStack() },
                onOpenAdHoc = { id -> navController.navigate("adhoc-edit/$id") }, onReviewFlight = ::openFlightReview)
        }
        composable(Routes.AdHoc) {
            AdHocFlightShell(graph, factory, onClose = { navController.popBackStack() })
        }
        composable(Routes.AdHocRecovery) { entry ->
            AdHocFlightShell(graph, factory, onClose = { navController.popBackStack() }, flightId = entry.arguments?.getString("flightId"))
        }
        // Retain the previous inbox destination for restored navigation stacks.
        composable(Routes.Notifications) {
            val model: NotificationsViewModel = viewModel(key = "notifications:$subject", factory = factory)
            NotificationsScreen(viewModel = model, onBack = { navController.popBackStack() },
                onOpenFlight = graph.notificationNavigation::publish,
                onOpenSchedule = graph.notificationNavigation::publish)
        }
    }
}
