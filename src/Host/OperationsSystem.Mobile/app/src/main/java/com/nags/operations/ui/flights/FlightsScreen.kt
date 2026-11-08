package com.nags.operations.ui.flights

import androidx.activity.compose.BackHandler
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.tween
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.PagerState
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.selectableGroup
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.FlightTakeoff
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.runtime.Composable
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.saveable.rememberSaveableStateHolder
import androidx.compose.runtime.setValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.snapshotFlow
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.lerp
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.layout.positionInParent
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.LifecycleResumeEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.nags.operations.R
import com.nags.operations.data.FlightCategory
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.notifications.NotificationOpenRequest
import com.nags.operations.ui.components.EmptyState
import com.nags.operations.ui.components.ErrorState
import com.nags.operations.ui.components.FlightCard
import com.nags.operations.ui.invite.InviteEmployeesDialog
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.drop
import kotlinx.coroutines.launch
import kotlinx.serialization.json.Json
import kotlin.math.abs
import kotlin.math.roundToInt

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
    // Keep each list alive even when the pager disposes an offscreen page.
    val normalListStates = key(state.stationId, state.permissionDenied) {
        List(FlightCategory.entries.size) { rememberLazyListState() }
    }
    val searchListStates = key(state.search, state.stationId, state.permissionDenied) {
        List(FlightCategory.entries.size) { rememberLazyListState() }
    }
    val listStates = if (state.search.isNotBlank()) searchListStates else normalListStates
    val keyboard = LocalSoftwareKeyboardController.current
    val focusManager = LocalFocusManager.current
    fun closeSearch() {
        keyboard?.hide()
        focusManager.clearFocus()
        viewModel.closeSearch()
    }
    BackHandler(enabled = state.isSearchOpen && state.selectedFlightId == null) { closeSearch() }
    val pagerState = rememberPagerState(initialPage = selectedTab) { FlightCategory.entries.size }
    val pagerScope = rememberCoroutineScope()
    val notifyTabSelection by rememberUpdatedState(onSelectTab)
    val requestedTab = if (notificationOpenRequest != null) 0 else selectedTab
    LaunchedEffect(viewModel, requestedTab, notificationOpenRequest) {
        // A selection echoed by the host must not interrupt the next swipe.
        if (pagerState.settledPage != requestedTab ||
            (notificationOpenRequest != null && pagerState.isScrollInProgress)) {
            pagerState.animateScrollToPage(requestedTab,
                animationSpec = tween(TAB_SLIDE_MILLIS, easing = FastOutSlowInEasing))
        }
        viewModel.setCategory(FlightCategory.entries[requestedTab])
    }
    LaunchedEffect(viewModel, pagerState) {
        // Restoration is handled above; only a completed swipe or tap selects a new feed.
        snapshotFlow { pagerState.settledPage }.drop(1).distinctUntilChanged().collect { page ->
            viewModel.setCategory(FlightCategory.entries[page])
            notifyTabSelection(page)
        }
    }
    LifecycleResumeEffect(viewModel) {
        viewModel.refreshSilently()
        onPauseOrDispose { }
    }
    LaunchedEffect(notificationOpenRequest) {
        val request = notificationOpenRequest ?: return@LaunchedEffect
        if (request.flightId != null) {
            viewModel.openFlight(request.flightId)
        } else {
            viewModel.refreshSilently()
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

    val selectTab: (Int) -> Unit = { page ->
        pagerScope.launch {
            pagerState.animateScrollToPage(page,
                animationSpec = tween(TAB_SLIDE_MILLIS, easing = FastOutSlowInEasing))
        }
    }
    Column(Modifier.fillMaxSize().imePadding()) {
        FlightTabSearchHeader(isSearchOpen = state.isSearchOpen, query = state.search,
            onQueryChange = viewModel::setSearch, onOpenSearch = viewModel::openSearch,
            onCloseSearch = ::closeSearch) {
            FlightsPageTabs(pagerState = pagerState, onSelectTab = selectTab, enabled = !state.isSearchOpen)
        }
        if (state.isUsingCachedList || !state.isOnline) {
            Text(stringResource(if (state.isUsingCachedList) R.string.flights_cached_list else R.string.flights_offline_hint),
                style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(horizontal = 16.dp, vertical = 4.dp))
        }
        HorizontalPager(state = pagerState, modifier = Modifier.weight(1f).fillMaxWidth().clipToBounds()) { page ->
            val category = FlightCategory.entries[page]
            FlightListPage(state = state.forCategory(category), viewModel = viewModel,
                listState = listStates[page],
                active = page == pagerState.settledPage && state.category == category)
        }
        if (state.isSearchOpen) {
            FlightSearchScopeTabs(selectedTab = pagerState.currentPage, onSelectTab = selectTab,
                modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp))
        }
    }
    if (state.selectedFlightId != null) key(state.selectedFlightId) {
        var recordingForm by rememberSaveable { mutableStateOf<String?>(null) }
        // The open form owns a stable snapshot, including across activity recreation.
        // A silent detail refresh can temporarily clear state.detail.
        var recordingFlightSnapshot by rememberSaveable { mutableStateOf<String?>(null) }
        val recordingFlight = remember(recordingFlightSnapshot) {
            recordingFlightSnapshot?.let { Json.decodeFromString<FlightDetail>(it) }
        }
        val recordingFormState = rememberSaveableStateHolder()
        FlightDetailsSheet(state = state, onClose = ::closeFlight,
            onRetry = { state.selectedFlightId?.let(viewModel::openFlight) },
            onInvite = viewModel::openInvitations,
            onOperations = { recordingFlightSnapshot = state.detail?.let { Json.encodeToString(it) }; recordingForm = "operations" },
            onCanceled = { recordingFlightSnapshot = state.detail?.let { Json.encodeToString(it) }; recordingForm = "cancellation" },
            loadCustomerLogo = viewModel::customerLogo)
        // An ordinary silent detail refresh must not replace an open form or discard its edits.
        val flight = recordingFlight ?: state.detail
        val activeForm = recordingForm
        if (activeForm != null && flight != null) {
            recordingFormState.SaveableStateProvider(activeForm) {
                val closeForm = {
                    recordingForm = null
                    recordingFlightSnapshot = null
                    recordingFormState.removeState(activeForm)
                }
                if (activeForm == "operations") {
                    val aircraftTypes by viewModel.arrivalAircraftTypes.collectAsStateWithLifecycle(initialValue = emptyList())
                    val catalogs by viewModel.operationsCatalogs.collectAsStateWithLifecycle(initialValue = FlightOperationsCatalogs())
                    FlightOperationsForm(flight, aircraftTypes, viewModel::customerLogo, onClose = closeForm, catalogs = catalogs)
                } else {
                    FlightCancellationForm(flight, viewModel::customerLogo, onClose = closeForm)
                }
            }
        }
    }
    if (state.isInvitationOpen) InviteEmployeesDialog(state, viewModel)

}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun FlightListPage(
    state: FlightsUiState,
    viewModel: FlightsViewModel,
    listState: LazyListState,
    active: Boolean,
) {
    // Only the settled feed can prefetch; tab selection does not reset its scroll position.
    val currentState by rememberUpdatedState(state)
    val keyboard = LocalSoftwareKeyboardController.current
    val focusManager = LocalFocusManager.current
    LaunchedEffect(viewModel, listState, active) {
        if (!active) return@LaunchedEffect
        snapshotFlow {
            nextFlightPageToLoad(currentState, currentState.category.ordinal, currentState.items.size,
                listState.layoutInfo.visibleItemsInfo.lastOrNull()?.index ?: -1)
        }.distinctUntilChanged().collect { page ->
            if (page != null) viewModel.loadMore()
        }
    }
    val onRefresh: () -> Unit = { if (active) viewModel.refresh() }
    val onLoadMore: () -> Unit = { if (active) viewModel.loadMore() }
    PullToRefreshBox(isRefreshing = state.showRefreshIndicator, onRefresh = onRefresh,
        modifier = if (active) Modifier.fillMaxSize() else Modifier.fillMaxSize().clearAndSetSemantics { }) {
        when {
            !state.hasLoadedList && state.items.isEmpty() && (state.isLoading || state.isRefreshing ||
                (state.hasMore && state.error == null && state.loadMoreError == null)) ->
                FlightListSkeleton(Modifier.fillMaxSize())
            state.error != null && state.items.isEmpty() -> Box(Modifier.fillMaxSize().verticalScroll(rememberScrollState()), contentAlignment = Alignment.Center) {
                ErrorState(title = stringResource(if (state.permissionDenied) R.string.flights_access else R.string.flights_load_failed),
                    message = if (state.permissionDenied) stringResource(R.string.flights_view_forbidden) else state.error, onRetry = onRefresh)
            }
            state.items.isEmpty() && state.loadMoreError != null -> Box(Modifier.fillMaxSize().verticalScroll(rememberScrollState()), contentAlignment = Alignment.Center) {
                ErrorState(title = stringResource(R.string.flights_load_failed),
                    message = state.loadMoreError, onRetry = onLoadMore)
            }
            state.items.isEmpty() -> Box(Modifier.fillMaxSize().verticalScroll(rememberScrollState()), contentAlignment = Alignment.Center) {
                EmptyState(icon = Icons.Default.FlightTakeoff,
                    title = stringResource(if (state.search.isNotBlank()) R.string.flights_no_matches
                        else when (state.category) {
                            FlightCategory.PerLanding -> R.string.flights_per_landing_empty
                            FlightCategory.AdHoc -> R.string.flights_adhoc_empty
                            FlightCategory.MyFlights -> R.string.flights_empty
                        }),
                    message = stringResource(if (state.search.isBlank()) R.string.flights_empty_hint else R.string.flights_search_hint))
            }
            else -> LazyColumn(
                state = listState,
                contentPadding = PaddingValues(start = 16.dp, top = 16.dp, end = 16.dp, bottom = 80.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp),
                modifier = Modifier.fillMaxSize(),
            ) {
                state.error?.let { error ->
                    item(key = "list-error") {
                        Text(error, color = MaterialTheme.colorScheme.error)
                        TextButton(onClick = onRefresh) { Text(stringResource(R.string.flights_retry)) }
                    }
                }
                items(state.items, key = { it.id }, contentType = { "flight" }) { flight ->
                    FlightCard(flight = flight, onClick = {
                        if (active) {
                            keyboard?.hide()
                            focusManager.clearFocus()
                            viewModel.openFlight(flight.id)
                        }
                    },
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
                            TextButton(onClick = onLoadMore) { Text(stringResource(R.string.flights_retry)) }
                        }
                    }
                }
            }
        }
    }
}

private const val TAB_SLIDE_MILLIS = 240

@Composable
private fun FlightsPageTabs(
    pagerState: PagerState,
    onSelectTab: (Int) -> Unit,
    enabled: Boolean = true,
) {
    val labels = listOf(stringResource(R.string.flights_tab_my),
        stringResource(R.string.flights_tab_per_landing), stringResource(R.string.flights_tab_adhoc))
    val activeInk = MaterialTheme.colorScheme.primary
    val inactiveInk = MaterialTheme.colorScheme.onSurfaceVariant
    val tabBounds = remember(labels) { mutableStateMapOf<Int, Rect>() }
    // Recompose color at bounded swipe steps; draw the marker at exact swipe progress.
    val textPositionSteps by remember(pagerState) {
        derivedStateOf {
            ((pagerState.currentPage + pagerState.currentPageOffsetFraction) * 100).roundToInt()
        }
    }
    val tabPadding = with(LocalDensity.current) { 8.dp.toPx() }
    Column {
        BoxWithConstraints(Modifier.fillMaxWidth().padding(horizontal = 8.dp)) {
            val maximumTabWidth = (maxWidth / labels.size).coerceIn(48.dp, 160.dp)
            Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()).selectableGroup()
                .drawBehind {
                    val tabPosition = (pagerState.currentPage + pagerState.currentPageOffsetFraction)
                        .coerceIn(0f, labels.lastIndex.toFloat())
                    val firstIndex = tabPosition.toInt()
                    val first = tabBounds[firstIndex] ?: return@drawBehind
                    val next = tabBounds[(firstIndex + 1).coerceAtMost(labels.lastIndex)] ?: first
                    val fraction = tabPosition - firstIndex
                    val left = first.left + (next.left - first.left) * fraction
                    val right = first.right + (next.right - first.right) * fraction
                    drawRect(activeInk, Offset(left, size.height - 2.dp.toPx()), Size(right - left, 2.dp.toPx()))
                }) {
                labels.forEachIndexed { index, label ->
                    val selected = index == pagerState.currentPage
                    val emphasis = (1f - abs(textPositionSteps / 100f - index)).coerceIn(0f, 1f)
                    Box(Modifier.onGloballyPositioned { coordinates ->
                            val left = coordinates.positionInParent().x + tabPadding
                            tabBounds[index] = Rect(left, 0f, left + coordinates.size.width - 2 * tabPadding, 0f)
                        }
                        .selectable(selected = selected, enabled = enabled, onClick = { onSelectTab(index) }, role = Role.Tab)
                        .widthIn(min = 48.dp, max = maximumTabWidth)
                        .padding(horizontal = 8.dp)
                        .heightIn(min = 48.dp), contentAlignment = Alignment.Center) {
                        // Keep the label's measured width stable throughout the swipe.
                        Text(label, style = MaterialTheme.typography.bodyMedium.copy(fontSize = 13.sp),
                            fontWeight = FontWeight.Bold, color = Color.Transparent, maxLines = 1,
                            softWrap = false, overflow = TextOverflow.Ellipsis,
                            modifier = Modifier.clearAndSetSemantics { })
                        Text(label, style = MaterialTheme.typography.bodyMedium.copy(fontSize = 13.sp),
                            fontWeight = FontWeight.Bold, color = lerp(inactiveInk, activeInk, emphasis), maxLines = 1,
                            softWrap = false, overflow = TextOverflow.Ellipsis)
                    }
                }
            }
        }
    }
}
