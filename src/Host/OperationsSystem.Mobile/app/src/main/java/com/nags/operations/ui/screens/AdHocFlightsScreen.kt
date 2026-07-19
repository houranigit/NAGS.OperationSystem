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
import androidx.compose.material.icons.automirrored.filled.Assignment
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
import com.nags.operations.data.notifications.NotificationOpenRequest
import com.nags.operations.ui.adhoc.AdHocFlightsViewModel
import com.nags.operations.ui.components.EmptyState
import com.nags.operations.ui.components.ErrorState
import com.nags.operations.ui.components.FlightCard
import com.nags.operations.ui.components.FlightDetailsActionsSheet
import com.nags.operations.ui.components.FlightSheetCallbacks

/**
 * Ad Hoc tab — mirrors [PerLandingFlightsTab]; list comes from `/flights/ad-hoc`.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AdHocFlightsTab(
    viewModel: AdHocFlightsViewModel,
    sheetCallbacks: FlightSheetCallbacks = FlightSheetCallbacks(),
    requestedFlightRequest: NotificationOpenRequest? = null,
    requestedFlight: MobileFlightDto? = null,
    onRequestedFlightOpened: (request: NotificationOpenRequest) -> Unit = {},
) {
    var sheetFlight by remember { mutableStateOf<MobileFlightDto?>(null) }
    val state by viewModel.state.collectAsStateWithLifecycle()
    LaunchedEffect(Unit) {
        viewModel.refresh(userInitiated = false)
    }
    LaunchedEffect(requestedFlightRequest, requestedFlight?.id) {
        val request = requestedFlightRequest ?: return@LaunchedEffect
        val flight = requestedFlight ?: return@LaunchedEffect
        if (!request.flightId.equals(flight.id, ignoreCase = true)) return@LaunchedEffect

        sheetFlight = flight
        // The shell consumes the persisted request only after the destination owns the sheet.
        onRequestedFlightOpened(request)
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
            placeholder = { Text("Search for an Ad Hoc flight..") },
            leadingIcon = { Icon(Icons.Default.Search, contentDescription = null) },
            singleLine = true,
            shape = RoundedCornerShape(14.dp),
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 16.dp, vertical = 8.dp),
        )

        PerLandingFlightsStatusFilterRow(
            selected = state.statusFilter,
            onStatusSelected = viewModel::setStatusFilter,
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
                ) { CircularProgressIndicator() }

                state.error != null && state.items.isEmpty() -> Box(
                    modifier = Modifier.fillMaxSize(),
                    contentAlignment = Alignment.Center,
                ) {
                    ErrorState(
                        title = "Couldn't load Ad Hoc flights",
                        message = state.error!!,
                        onRetry = { viewModel.refresh(userInitiated = true) },
                    )
                }

                state.items.isEmpty() -> Box(
                    modifier = Modifier.fillMaxSize(),
                    contentAlignment = Alignment.Center,
                ) {
                    val noFiltersActive = state.search.isBlank() && state.statusFilter == null
                    EmptyState(
                        icon = Icons.AutoMirrored.Filled.Assignment,
                        title = if (noFiltersActive) "No Ad Hoc flights right now" else "No matching Ad Hoc flights",
                        message = when {
                            noFiltersActive ->
                                "No Ad Hoc flights at your station in the next 12 hours. Pull down or tap refresh to check again."
                            state.search.isNotBlank() ->
                                "No flight matches \"${state.search}\". Try a different search or filter."
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
                onCancelFlight = { id, canceledAtIso, reason, onFinished ->
                    viewModel.cancelFlight(id, canceledAtIso, reason, onFinished)
                },
            ),
            onDismiss = { sheetFlight = null },
        )
    }
}
