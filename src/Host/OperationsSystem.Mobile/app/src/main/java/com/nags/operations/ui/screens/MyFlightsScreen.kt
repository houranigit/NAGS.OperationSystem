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
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.SnackbarResult
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
import androidx.compose.ui.res.stringResource
import com.nags.operations.R
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.nags.operations.data.MobileFlightDto
import com.nags.operations.data.notifications.NotificationOpenRequest
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
    requestedFlightRequest: NotificationOpenRequest? = null,
    onRequestedFlightOpened: (notificationId: String?) -> Unit = {},
) {
    var sheetFlight by remember { mutableStateOf<MobileFlightDto?>(null) }
    var sheetRequest by remember { mutableStateOf<NotificationOpenRequest?>(null) }
    val state by viewModel.state.collectAsStateWithLifecycle()
    val snackbarHostState = remember { SnackbarHostState() }
    val openErrorMessage = stringResource(R.string.notifications_flight_unavailable)
    val retryLabel = stringResource(R.string.notifications_retry)
    LaunchedEffect(Unit) {
        viewModel.refresh(userInitiated = false)
    }
    LaunchedEffect(requestedFlightRequest) {
        val request = requestedFlightRequest ?: return@LaunchedEffect
        if (request.flightId != null) {
            // A newer notification for the same flight is still a distinct authoritative open.
            sheetFlight = null
            sheetRequest = null
            viewModel.openRequestedFlight(request)
        } else {
            // A bulk-schedule notification has no row deep link. Refresh even when My Flights was
            // already composed so a missed realtime envelope cannot leave the old list visible.
            viewModel.refresh(userInitiated = false)
            onRequestedFlightOpened(request.notificationId)
        }
    }
    LaunchedEffect(
        state.requestedFlightRequest,
        state.requestedFlight?.id,
        requestedFlightRequest,
    ) {
        val completedRequest = state.requestedFlightRequest
        if (completedRequest != null && completedRequest == requestedFlightRequest) {
            state.requestedFlight?.let {
                sheetRequest = completedRequest
                sheetFlight = it
                onRequestedFlightOpened(completedRequest.notificationId)
            }
        }
    }
    LaunchedEffect(
        state.requestedFlightError,
        state.requestedFlightRequest,
        requestedFlightRequest,
    ) {
        val failedRequest = state.requestedFlightRequest
        if (state.requestedFlightError != null &&
            failedRequest != null &&
            failedRequest == requestedFlightRequest
        ) {
            val result = snackbarHostState.showSnackbar(openErrorMessage, retryLabel)
            if (result == SnackbarResult.ActionPerformed) {
                viewModel.openRequestedFlight(failedRequest, force = true)
            } else {
                viewModel.consumeRequestedFlight(failedRequest)
                onRequestedFlightOpened(failedRequest.notificationId)
            }
        }
    }

    fun closeSheet() {
        val openedRequest = sheetRequest
        sheetFlight = null
        sheetRequest = null
        if (openedRequest != null) viewModel.consumeRequestedFlight(openedRequest)
    }

    val allowedStatusFilters = remember { StandardFlightStatusFilterKinds.toSet() }
    LaunchedEffect(state.statusFilter) {
        val s = state.statusFilter ?: return@LaunchedEffect
        if (s !in allowedStatusFilters) viewModel.setStatusFilter(null)
    }

    val listState = rememberLazyListState()
    val pullToRefreshState = rememberPullToRefreshState()

    Box(modifier = Modifier.fillMaxSize()) {
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
                                "You don't have any flights in the previous or next 12 hours. Pull down or tap refresh to check again."
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
                            onClick = {
                                sheetRequest = null
                                sheetFlight = flight
                            },
                        )
                    }
                }
            }
        }
    }
        SnackbarHost(
            hostState = snackbarHostState,
            modifier = Modifier.align(Alignment.BottomCenter).padding(16.dp),
        )
    }

    sheetFlight?.let { f ->
        FlightDetailsActionsSheet(
            flight = f,
            localDraftId = state.draftIdByFlightId[f.id],
            isOnline = state.isOnline,
            showInvite = true,
            onRevalidateFlight = if (sheetRequest != null) {
                viewModel::revalidateRequestedFlight
            } else {
                null
            },
            callbacks = FlightSheetCallbacks(
                onCreateWorkOrder = { id ->
                    closeSheet()
                    sheetCallbacks.onCreateWorkOrder(id)
                },
                onCompleteWorkOrderDraft = { draftId ->
                    closeSheet()
                    sheetCallbacks.onCompleteWorkOrderDraft(draftId)
                },
                onReturnToRamp = { id ->
                    closeSheet()
                    sheetCallbacks.onReturnToRamp(id)
                },
                onInviteTeammate = { id ->
                    closeSheet()
                    sheetCallbacks.onInviteTeammate(id)
                },
                onCancelFlight = { id, canceledAtIso, reason, onFinished ->
                    viewModel.cancelFlight(id, canceledAtIso, reason, onFinished)
                },
            ),
            onDismiss = ::closeSheet,
        )
    }
}
