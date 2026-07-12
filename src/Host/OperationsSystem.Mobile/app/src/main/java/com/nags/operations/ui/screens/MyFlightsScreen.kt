package com.nags.operations.ui.screens

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.FlightTakeoff
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.material3.pulltorefresh.rememberPullToRefreshState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.nags.operations.data.MobileFlightDto
import com.nags.operations.ui.components.EmptyState
import com.nags.operations.ui.components.ErrorState
import com.nags.operations.ui.components.FlightCard
import com.nags.operations.ui.components.FlightDetailsActionsSheet
import com.nags.operations.ui.components.FlightSheetCallbacks
import com.nags.operations.ui.flights.MyFlightsViewModel

/**
 * My flights tab body — sits under [com.nags.operations.ui.components.AppHeader]
 * inside [com.nags.operations.ui.home.MainShellScreen].
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun MyFlightsTab(
    viewModel: MyFlightsViewModel,
    sheetCallbacks: FlightSheetCallbacks = FlightSheetCallbacks(),
) {
    var sheetFlight by remember { mutableStateOf<MobileFlightDto?>(null) }
    val state by viewModel.state.collectAsStateWithLifecycle()
    LaunchedEffect(Unit) {
        viewModel.refresh(userInitiated = false)
    }

    val allowedStatusFilters = remember { StandardFlightStatusFilterKinds.toSet() }
    LaunchedEffect(state.statusFilter) {
        val s = state.statusFilter ?: return@LaunchedEffect
        if (s !in allowedStatusFilters) viewModel.setStatusFilter(null)
    }

    val listState = rememberLazyListState()
    val pullToRefreshState = rememberPullToRefreshState()

    Column(modifier = Modifier.fillMaxSize()) {
        OutlinedTextField(
            value = state.search,
            onValueChange = viewModel::setSearch,
            placeholder = { Text("Search for an assigned flight..") },
            leadingIcon = { Icon(Icons.Default.Search, contentDescription = null) },
            singleLine = true,
            shape = RoundedCornerShape(14.dp),
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 16.dp, vertical = 8.dp),
        )

        MyFlightsStatusFilterRow(
            selected = state.statusFilter,
            quickFilter = state.quickFilter,
            onStatusSelected = viewModel::setStatusFilter,
            onQuickFilterSelected = viewModel::setQuickFilter,
        )

        PullToRefreshBox(
            state = pullToRefreshState,
            isRefreshing = state.isRefreshing,
            onRefresh = { viewModel.refresh(userInitiated = true) },
            modifier = Modifier.fillMaxSize(),
        ) {
            when {
                state.isLoading && state.items.isEmpty() -> Box(
                    modifier = Modifier.fillMaxSize(),
                    contentAlignment = Alignment.Center,
                ) {
                    CircularProgressIndicator()
                }

                state.error != null && state.items.isEmpty() -> Box(
                    modifier = Modifier.fillMaxSize(),
                    contentAlignment = Alignment.Center,
                ) {
                    ErrorState(
                        title = "Couldn't load flights",
                        message = state.error!!,
                        onRetry = { viewModel.refresh(userInitiated = true) },
                    )
                }

                state.items.isEmpty() -> Box(
                    modifier = Modifier.fillMaxSize(),
                    contentAlignment = Alignment.Center,
                ) {
                    val noFiltersActive = state.search.isBlank() &&
                        state.statusFilter == null &&
                        state.quickFilter == null
                    EmptyState(
                        icon = Icons.Default.FlightTakeoff,
                        title = if (noFiltersActive) "No flights yet" else "No matching flights",
                        message = when {
                            noFiltersActive ->
                                "You don't have any flights in the next 12 hours. Pull down or tap refresh to check again."
                            state.search.isNotBlank() ->
                                "No flight matches \"${state.search}\". Try a different search or filter."
                            state.quickFilter == MyFlightsViewModel.QuickFilter.Pending ->
                                "All your flights have a work order. Tap a chip to clear the filter."
                            else -> "No flights match the selected status. Try clearing the filter."
                        },
                        actionLabel = "Refresh",
                        onAction = { viewModel.refresh(userInitiated = true) },
                    )
                }

                else -> LazyColumn(
                    state = listState,
                    contentPadding = PaddingValues(horizontal = 16.dp, vertical = 8.dp),
                    verticalArrangement = Arrangement.spacedBy(12.dp),
                    modifier = Modifier.fillMaxSize(),
                ) {
                    itemsIndexed(state.items, key = { _, flight -> flight.id }) { _, flight ->
                        FlightCard(
                            flight = flight,
                            pending = state.pendingByFlightId[flight.id],
                            hasLocalDraft = state.draftIdByFlightId.containsKey(flight.id),
                            onClick = { sheetFlight = flight },
                        )
                    }
                }
            }
        }
    }

    sheetFlight?.let { f ->
        FlightDetailsActionsSheet(
            flight = f,
            localDraftId = state.draftIdByFlightId[f.id],
            isOnline = state.isOnline,
            showInvite = true,
            callbacks = FlightSheetCallbacks(
                onCreateWorkOrder = { id ->
                    sheetFlight = null
                    sheetCallbacks.onCreateWorkOrder(id)
                },
                onCompleteWorkOrderDraft = { draftId ->
                    sheetFlight = null
                    sheetCallbacks.onCompleteWorkOrderDraft(draftId)
                },
                onReturnToRamp = { id ->
                    sheetFlight = null
                    sheetCallbacks.onReturnToRamp(id)
                },
                onInviteTeammate = { id ->
                    sheetFlight = null
                    sheetCallbacks.onInviteTeammate(id)
                },
                onCancelFlight = { id, canceledAtIso, reason ->
                    sheetFlight = null
                    viewModel.cancelFlight(id, canceledAtIso, reason)
                },
            ),
            onDismiss = { sheetFlight = null },
        )
    }
}
