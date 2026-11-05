package com.nags.operations.ui.flights

import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.selectableGroup
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.FlightTakeoff
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.snapshotFlow
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.LifecycleResumeEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.nags.operations.R
import com.nags.operations.data.FlightCategory
import com.nags.operations.data.notifications.NotificationOpenRequest
import com.nags.operations.ui.components.EmptyState
import com.nags.operations.ui.components.ErrorState
import com.nags.operations.ui.components.FlightCard
import com.nags.operations.ui.components.FlightOverviewBanner
import com.nags.operations.ui.invite.InviteEmployeesDialog
import com.nags.operations.ui.util.formatIsoForDisplay
import java.time.ZoneId
import kotlinx.coroutines.flow.distinctUntilChanged

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun FlightsScreen(
    viewModel: FlightsViewModel,
    notificationOpenRequest: NotificationOpenRequest? = null,
    onNotificationHandled: (String?) -> Unit = {},
    selectedTab: Int = 0,
    onSelectTab: (Int) -> Unit = {},
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val listState = rememberLazyListState()
    val selectedCategory = FlightCategory.entries.getOrElse(selectedTab) { FlightCategory.MyFlights }
    val visibleFlights = if (state.category == selectedCategory) state.items else emptyList()
    LaunchedEffect(viewModel, selectedCategory) {
        viewModel.setCategory(selectedCategory)
    }
    LaunchedEffect(viewModel, listState, selectedTab) {
        snapshotFlow {
            nextFlightPageToLoad(state, selectedTab, state.items.size,
                listState.layoutInfo.visibleItemsInfo.lastOrNull()?.index ?: -1)
        }.distinctUntilChanged().collect { page ->
            if (page != null) viewModel.loadMore()
        }
    }
    LaunchedEffect(state.search, selectedTab) {
        listState.scrollToItem(0)
    }
    LifecycleResumeEffect(viewModel) {
        viewModel.refresh()
        onPauseOrDispose { }
    }
    LaunchedEffect(notificationOpenRequest) {
        val request = notificationOpenRequest ?: return@LaunchedEffect
        if (request.flightId != null) {
            viewModel.openFlight(request.flightId)
        } else {
            viewModel.refresh()
            onNotificationHandled(request.notificationId)
        }
    }
    LaunchedEffect(notificationOpenRequest, state.detail, state.detailError, state.permissionDenied, state.lastUnavailableFlightId) {
        val request = notificationOpenRequest ?: return@LaunchedEffect
        val current = viewModel.state.value
        if (request.flightId != null && (current.permissionDenied || current.lastUnavailableFlightId == request.flightId ||
            (request.flightId == current.selectedFlightId && (current.detail != null || current.detailError != null)))) {
            onNotificationHandled(request.notificationId)
        }
    }

    fun closeFlight() {
        notificationOpenRequest?.let { request ->
            if (request.flightId == state.selectedFlightId) onNotificationHandled(request.notificationId)
        }
        viewModel.closeFlight()
    }

    Column(Modifier.fillMaxSize()) {
        FlightsPageTabs(selectedTab = selectedTab, onSelectTab = {
            viewModel.setCategory(FlightCategory.entries[it])
            onSelectTab(it)
        },
            onRefresh = viewModel::refresh,
            refreshEnabled = !state.isLoading && !state.isRefreshing)
        Text(stringResource(R.string.flights_time_zone, ZoneId.systemDefault().id),
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.padding(start = 16.dp, end = 16.dp, top = 6.dp, bottom = 2.dp))
        if (state.isUsingCachedList || !state.isOnline) {
            Text(stringResource(if (state.isUsingCachedList) R.string.flights_cached_list else R.string.flights_offline_hint),
                style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(horizontal = 16.dp, vertical = 4.dp))
        }
        PullToRefreshBox(isRefreshing = state.isRefreshing, onRefresh = viewModel::refresh,
            modifier = Modifier.weight(1f)) {
            when {
                visibleFlights.isEmpty() && (state.category != selectedCategory || state.isLoading || state.isRefreshing ||
                    (state.hasMore && state.error == null && state.loadMoreError == null)) ->
                    FlightListSkeleton(Modifier.fillMaxSize())
                state.error != null && visibleFlights.isEmpty() -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    ErrorState(title = stringResource(if (state.permissionDenied) R.string.flights_access else R.string.flights_load_failed),
                        message = if (state.permissionDenied) stringResource(R.string.flights_view_forbidden) else state.error!!, onRetry = viewModel::refresh)
                }
                visibleFlights.isEmpty() && state.loadMoreError != null -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    ErrorState(title = stringResource(R.string.flights_load_failed),
                        message = state.loadMoreError!!, onRetry = viewModel::loadMore)
                }
                visibleFlights.isEmpty() -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    EmptyState(icon = Icons.Default.FlightTakeoff,
                        title = stringResource(if (state.search.isNotBlank()) R.string.flights_no_matches
                            else when (selectedCategory) {
                                FlightCategory.PerLanding -> R.string.flights_per_landing_empty
                                FlightCategory.AdHoc -> R.string.flights_adhoc_empty
                                FlightCategory.MyFlights -> R.string.flights_empty
                            }),
                        message = stringResource(if (state.search.isBlank()) R.string.flights_empty_hint else R.string.flights_search_hint),
                        actionLabel = stringResource(R.string.flights_refresh_short), onAction = viewModel::refresh)
                }
                else -> LazyColumn(
                    state = listState,
                    contentPadding = PaddingValues(start = 16.dp, top = 8.dp, end = 16.dp, bottom = 80.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp),
                    modifier = Modifier.fillMaxSize(),
                ) {
                    state.error?.let { error ->
                        item(key = "list-error") {
                            Text(error, color = MaterialTheme.colorScheme.error)
                            TextButton(onClick = viewModel::refresh) { Text(stringResource(R.string.flights_retry)) }
                        }
                    }
                    items(visibleFlights, key = { it.id }, contentType = { "flight" }) { flight ->
                        FlightCard(flight = flight, onClick = { viewModel.openFlight(flight.id) },
                            loadCustomerLogo = viewModel::customerLogo)
                    }
                    item(key = "flight-count") {
                        Text(pluralStringResource(R.plurals.flights_showing_count,
                            state.totalCount.coerceIn(0, Int.MAX_VALUE.toLong()).toInt(), state.items.size, state.totalCount),
                            style = MaterialTheme.typography.labelMedium,
                            modifier = Modifier.padding(vertical = 4.dp))
                    }
                    if (state.isLoadingMore) {
                        item(key = "loading-flights") { FlightCardSkeleton() }
                    }
                    state.loadMoreError?.let { error ->
                        item(key = "paging-error") {
                            Column(Modifier.fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally) {
                                Text(error, color = MaterialTheme.colorScheme.error)
                                TextButton(onClick = viewModel::loadMore) { Text(stringResource(R.string.flights_retry)) }
                            }
                        }
                    }
                }
            }
        }
    }
    if (state.selectedFlightId != null) {
        ModalBottomSheet(onDismissRequest = ::closeFlight,
            sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true)) {
            Column(Modifier.fillMaxWidth().verticalScroll(rememberScrollState())
                .padding(start = 20.dp, end = 20.dp, bottom = 28.dp),
                verticalArrangement = Arrangement.spacedBy(16.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(stringResource(R.string.flights_details), style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold,
                        modifier = Modifier.weight(1f))
                    IconButton(onClick = ::closeFlight) {
                        Icon(Icons.Default.Close, contentDescription = stringResource(R.string.flights_close_details))
                    }
                }
                if (state.invitationSent) Text(stringResource(R.string.flights_invite_sent),
                    color = MaterialTheme.colorScheme.primary)
                when {
                    state.isLoadingDetail -> CircularProgressIndicator(Modifier.padding(20.dp))
                    state.detailError != null -> ErrorState(message = state.detailError!!,
                        onRetry = { state.selectedFlightId?.let(viewModel::openFlight) })
                    state.detail != null -> {
                        val flight = state.detail!!
                        if (flight.fromCache) Text(stringResource(R.string.flights_cached_detail),
                            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                        FlightOverviewBanner(customerIataCode = flight.customerIataCode.orEmpty(),
                            customerName = flight.customerName, stationCode = flight.stationIata,
                            operationTypeCode = flight.operationTypeName, flightNumber = flight.flightNumber,
                            aircraftModel = flight.aircraftTypeModel,
                            staDisplay = formatIsoForDisplay(flight.scheduledArrivalUtc),
                            stdDisplay = formatIsoForDisplay(flight.scheduledDepartureUtc))
                        Text(flight.stationName, style = MaterialTheme.typography.bodyMedium)
                        if (state.canInviteEmployees && !flight.isPerLanding) {
                            Button(onClick = viewModel::openInvitations, enabled = state.isOnline && !flight.fromCache, modifier = Modifier.fillMaxWidth()) {
                                Text(stringResource(R.string.flights_invite_employees))
                            }
                        }
                        Text(stringResource(R.string.flights_planned_services), style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                        if (flight.plannedServices.isEmpty()) Text(stringResource(R.string.flights_no_planned_services))
                        flight.plannedServices.forEach { service -> Text(service.name) }
                        Text(stringResource(R.string.flights_assigned_staff), style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                        if (flight.assignedEmployees.isEmpty()) Text(stringResource(R.string.flights_no_assigned_staff))
                        flight.assignedEmployees.forEach { employee -> Text("${employee.fullName} · ${employee.employeeId}") }
                        Text(stringResource(R.string.flights_id, flight.id), style = MaterialTheme.typography.labelSmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                }
            }
        }
    }
    if (state.isInvitationOpen) InviteEmployeesDialog(state, viewModel)

}

@Composable
private fun FlightsPageTabs(
    selectedTab: Int,
    onSelectTab: (Int) -> Unit,
    onRefresh: () -> Unit,
    refreshEnabled: Boolean,
) {
    val labels = listOf(stringResource(R.string.flights_tab_my),
        stringResource(R.string.flights_tab_per_landing), stringResource(R.string.flights_tab_adhoc))
    val ink = MaterialTheme.colorScheme.onSurface
    Column {
        Row(Modifier.fillMaxWidth().padding(horizontal = 4.dp), verticalAlignment = Alignment.CenterVertically) {
            Row(Modifier.weight(1f).horizontalScroll(rememberScrollState()).selectableGroup()) {
                labels.forEachIndexed { index, label ->
                    val selected = index == selectedTab
                    Box(Modifier.selectable(selected = selected, onClick = { onSelectTab(index) }, role = Role.Tab)
                        .padding(horizontal = 12.dp)
                        .heightIn(min = 48.dp)
                        .drawBehind {
                            if (selected) drawRect(ink, Offset(0f, size.height - 2.dp.toPx()), Size(size.width, 2.dp.toPx()))
                        }, contentAlignment = Alignment.Center) {
                        Text(label, style = MaterialTheme.typography.bodyMedium.copy(fontSize = 14.sp),
                            fontWeight = if (selected) FontWeight.SemiBold else FontWeight.Medium,
                            color = if (selected) ink else ink.copy(alpha = 0.55f), maxLines = 1)
                    }
                }
            }
            IconButton(onClick = onRefresh, enabled = refreshEnabled) {
                Icon(Icons.Default.Refresh, contentDescription = stringResource(R.string.flights_refresh))
            }
        }
        HorizontalDivider(color = ink.copy(alpha = 0.1f))
    }
}
