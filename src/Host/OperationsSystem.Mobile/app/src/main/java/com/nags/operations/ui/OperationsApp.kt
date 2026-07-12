package com.nags.operations.ui

import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.navigation.NavType
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import androidx.navigation.navArgument
import com.nags.operations.AppGraph
import com.nags.operations.ui.components.FlightSheetCallbacks
import com.nags.operations.ui.home.MainShellScreen
import com.nags.operations.ui.invite.InviteEmployeesScreen
import com.nags.operations.ui.invite.InviteEmployeesViewModel
import com.nags.operations.ui.login.LoginScreen
import com.nags.operations.ui.login.LoginViewModel
import com.nags.operations.ui.screens.CreateWorkOrderScreen
import com.nags.operations.ui.screens.ReturnToRampScreen
import com.nags.operations.ui.sync.SyncCenterScreen
import com.nags.operations.ui.sync.SyncCenterViewModel
import com.nags.operations.ui.workorder.CreateWorkOrderLaunchMode
import com.nags.operations.ui.workorder.CreateWorkOrderViewModel
import com.nags.operations.ui.workorder.ReturnToRampViewModel
import kotlinx.coroutines.launch

private object Routes {
    const val Login = "login"
    const val Main = "main"
    const val SyncCenter = "sync-center"
    const val CreateWorkOrder = "create-work-order/{flightId}"
    const val ReturnToRamp = "return-to-ramp/{flightId}"
    const val CreateAdHocFlight = "create-ad-hoc-flight"
    const val WorkOrderDraft = "work-order-draft/{draftId}"
    const val InviteEmployees = "invite/{flightId}"
}

private const val NavAnimMs = 220

@Composable
fun OperationsApp() {
    val context = LocalContext.current
    val graph = remember { AppGraph.get(context) }
    val factory = remember(graph) { AppViewModelFactory(graph) }
    val navController = rememberNavController()
    val coroutineScope = rememberCoroutineScope()

    NavHost(
        navController = navController,
        startDestination = Routes.Login,
        enterTransition = { fadeIn(tween(NavAnimMs)) },
        exitTransition = { fadeOut(tween(NavAnimMs)) },
        popEnterTransition = { fadeIn(tween(NavAnimMs)) },
        popExitTransition = { fadeOut(tween(NavAnimMs)) },
    ) {
        composable(Routes.Login) {
            val vm: LoginViewModel = viewModel(factory = factory)
            LoginScreen(
                viewModel = vm,
                tokenStore = graph.tokenStore,
                onLoggedIn = {
                    navController.navigate(Routes.Main) {
                        popUpTo(Routes.Login) { inclusive = true }
                    }
                },
            )
        }
        composable(Routes.Main) {
            MainShellScreen(
                tokenStore = graph.tokenStore,
                graph = graph,
                factory = factory,
                onOpenSyncCenter = { navController.navigate(Routes.SyncCenter) },
                onOpenCreateAdHocFlight = { navController.navigate(Routes.CreateAdHocFlight) },
                flightSheetCallbacks = FlightSheetCallbacks(
                    onCreateWorkOrder = { flightId ->
                        navController.navigate("create-work-order/$flightId")
                    },
                    onCompleteWorkOrderDraft = { draftId ->
                        navController.navigate("work-order-draft/$draftId")
                    },
                    onReturnToRamp = { flightId ->
                        navController.navigate("return-to-ramp/$flightId")
                    },
                    onInviteTeammate = { flightId ->
                        navController.navigate("invite/$flightId")
                    },
                ),
                onOpenWorkOrderDraft = { draftId ->
                    navController.navigate("work-order-draft/$draftId")
                },
                onLogout = {
                    coroutineScope.launch {
                        graph.signOut()
                        navController.navigate(Routes.Login) {
                            popUpTo(Routes.Main) { inclusive = true }
                        }
                    }
                },
            )
        }
        composable(Routes.CreateAdHocFlight) {
            val vm: CreateWorkOrderViewModel = viewModel(
                key = "ad_hoc_scratch",
                factory = remember(graph) {
                    object : ViewModelProvider.Factory {
                        @Suppress("UNCHECKED_CAST")
                        override fun <T : ViewModel> create(modelClass: Class<T>): T =
                            CreateWorkOrderViewModel(
                                launchMode = CreateWorkOrderLaunchMode.AdHocScratch,
                                flightsRepository = graph.flightsRepository,
                                draftsRepository = graph.workOrderDraftsRepository,
                                outboxRepository = graph.workOrderOutboxRepository,
                                tokenStore = graph.tokenStore,
                                applicationScope = graph.appScope,
                                catalogsRepository = graph.catalogsRepository,
                                employeesRepository = graph.employeesRepository,
                            ) as T
                    }
                },
            )
            CreateWorkOrderScreen(
                viewModel = vm,
                onBack = { navController.popBackStack() },
            )
        }
        composable(
            route = Routes.CreateWorkOrder,
            arguments = listOf(navArgument("flightId") { type = NavType.StringType }),
        ) { entry ->
            val flightId = entry.arguments?.getString("flightId") ?: return@composable
            val vm: CreateWorkOrderViewModel = viewModel(
                key = flightId,
                factory = remember(flightId, graph) {
                    object : ViewModelProvider.Factory {
                        @Suppress("UNCHECKED_CAST")
                        override fun <T : ViewModel> create(modelClass: Class<T>): T =
                            CreateWorkOrderViewModel(
                                launchMode = CreateWorkOrderLaunchMode.FromFlight(flightId),
                                flightsRepository = graph.flightsRepository,
                                draftsRepository = graph.workOrderDraftsRepository,
                                outboxRepository = graph.workOrderOutboxRepository,
                                tokenStore = graph.tokenStore,
                                applicationScope = graph.appScope,
                                catalogsRepository = graph.catalogsRepository,
                                employeesRepository = graph.employeesRepository,
                            ) as T
                    }
                },
            )
            CreateWorkOrderScreen(
                viewModel = vm,
                onBack = { navController.popBackStack() },
            )
        }
        composable(
            route = Routes.ReturnToRamp,
            arguments = listOf(navArgument("flightId") { type = NavType.StringType }),
        ) { entry ->
            val flightId = entry.arguments?.getString("flightId") ?: return@composable
            val vm: ReturnToRampViewModel = viewModel(
                key = "rtr_$flightId",
                factory = remember(flightId, graph) {
                    object : ViewModelProvider.Factory {
                        @Suppress("UNCHECKED_CAST")
                        override fun <T : ViewModel> create(modelClass: Class<T>): T =
                            ReturnToRampViewModel(
                                flightId = flightId,
                                flightsRepository = graph.flightsRepository,
                                outboxRepository = graph.workOrderOutboxRepository,
                                mobileApi = graph.mobileApi,
                                applicationScope = graph.appScope,
                                catalogsRepository = graph.catalogsRepository,
                                employeesRepository = graph.employeesRepository,
                            ) as T
                    }
                },
            )
            ReturnToRampScreen(
                viewModel = vm,
                onBack = { navController.popBackStack() },
            )
        }
        composable(
            route = Routes.WorkOrderDraft,
            arguments = listOf(navArgument("draftId") { type = NavType.StringType }),
        ) { entry ->
            val draftId = entry.arguments?.getString("draftId") ?: return@composable
            val vm: CreateWorkOrderViewModel = viewModel(
                key = "draft_$draftId",
                factory = remember(draftId, graph) {
                    object : ViewModelProvider.Factory {
                        @Suppress("UNCHECKED_CAST")
                        override fun <T : ViewModel> create(modelClass: Class<T>): T =
                            CreateWorkOrderViewModel(
                                launchMode = CreateWorkOrderLaunchMode.FromDraft(draftId),
                                flightsRepository = graph.flightsRepository,
                                draftsRepository = graph.workOrderDraftsRepository,
                                outboxRepository = graph.workOrderOutboxRepository,
                                tokenStore = graph.tokenStore,
                                applicationScope = graph.appScope,
                                catalogsRepository = graph.catalogsRepository,
                                employeesRepository = graph.employeesRepository,
                            ) as T
                    }
                },
            )
            CreateWorkOrderScreen(
                viewModel = vm,
                onBack = { navController.popBackStack() },
            )
        }
        composable(
            route = Routes.InviteEmployees,
            arguments = listOf(navArgument("flightId") { type = NavType.StringType }),
        ) { entry ->
            val flightId = entry.arguments?.getString("flightId") ?: return@composable
            val vm: InviteEmployeesViewModel = viewModel(
                key = "invite_$flightId",
                factory = remember(flightId, graph) {
                    object : ViewModelProvider.Factory {
                        @Suppress("UNCHECKED_CAST")
                        override fun <T : ViewModel> create(modelClass: Class<T>): T =
                            InviteEmployeesViewModel(
                                flightId = flightId,
                                flightsRepository = graph.flightsRepository,
                                employeesRepository = graph.employeesRepository,
                                mobileApi = graph.mobileApi,
                                syncCoordinator = graph.syncCoordinator,
                                networkMonitor = graph.networkMonitor,
                                tokenStore = graph.tokenStore,
                            ) as T
                    }
                },
            )
            InviteEmployeesScreen(
                viewModel = vm,
                onBack = { navController.popBackStack() },
            )
        }
        composable(Routes.SyncCenter) {
            val vm: SyncCenterViewModel = viewModel(factory = factory)
            SyncCenterScreen(
                viewModel = vm,
                onBack = { navController.popBackStack() },
            )
        }
    }
}
