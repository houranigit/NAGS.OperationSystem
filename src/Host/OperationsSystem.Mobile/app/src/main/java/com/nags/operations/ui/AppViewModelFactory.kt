package com.nags.operations.ui

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import com.nags.operations.AppGraph
import com.nags.operations.ui.account.AccountViewModel
import com.nags.operations.ui.flights.FlightsViewModel
import com.nags.operations.ui.login.LoginViewModel
import com.nags.operations.ui.notifications.NotificationsViewModel

class AppViewModelFactory(private val graph: AppGraph) : ViewModelProvider.Factory {
    @Suppress("UNCHECKED_CAST")
    override fun <T : ViewModel> create(modelClass: Class<T>): T = when (modelClass) {
        LoginViewModel::class.java -> LoginViewModel(graph.authRepository)
        FlightsViewModel::class.java -> FlightsViewModel(graph.flightsRepository, graph.authRepository)
        AccountViewModel::class.java -> AccountViewModel(graph.authRepository, graph.networkMonitor)
        NotificationsViewModel::class.java -> NotificationsViewModel(graph.notificationsRepository)
        else -> throw IllegalArgumentException("Unknown ViewModel ${modelClass.name}")
    } as T
}
