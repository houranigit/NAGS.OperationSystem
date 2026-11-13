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
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.LifecycleResumeEffect
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.repeatOnLifecycle
import java.util.UUID
import com.nags.operations.data.hasOwnCancellation
import com.nags.operations.data.hasOwnWork
import com.nags.operations.data.ownCancellation
import com.nags.operations.data.cancellationEditable
import com.nags.operations.data.cancellationAtUtcForCard
import com.nags.operations.data.userMessage
import com.nags.operations.R
import com.nags.operations.data.FlightCategory
import com.nags.operations.data.FlightDiscoverySource
import com.nags.operations.ui.components.FeedDateHeading
import androidx.compose.material.icons.automirrored.filled.ManageSearch
import androidx.compose.material.icons.filled.Search
import androidx.compose.ui.platform.LocalLocale
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.notifications.NotificationOpenRequest
import com.nags.operations.ui.components.EmptyState
import com.nags.operations.ui.components.InlineNotice
import com.nags.operations.ui.components.InlineNoticeKind
import com.nags.operations.ui.components.FlightCard
import com.nags.operations.ui.invite.InviteEmployeesDialog
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.drop
import kotlinx.coroutines.launch
import kotlinx.coroutines.delay
import kotlinx.serialization.json.Json
import kotlin.math.abs
import kotlin.math.roundToInt
import java.time.Instant

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun FlightsScreen(
    viewModel: FlightsViewModel,
    notificationOpenRequest: NotificationOpenRequest? = null,
    onNotificationHandled: (String?) -> Unit = {},
    selectedTab: Int = 0,
    onSelectTab: (Int) -> Unit = {},
    bottomContentPadding: Dp = 80.dp,
    discoverySource: FlightDiscoverySource? = null,
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    var pendingNavigationFlightId by rememberSaveable { mutableStateOf<String?>(null) }
    var pendingNavigationKey by rememberSaveable { mutableStateOf<String?>(null) }
    // Keep each list alive even when the pager disposes an offscreen page.
    val normalListStates = key(state.stationId, state.permissionDenied) {
        List(if (discoverySource == null) FlightCategory.entries.size else 1) { rememberLazyListState() }
    }
    val searchListStates = key(state.search, state.stationId, state.permissionDenied) {
        List(if (discoverySource == null) FlightCategory.entries.size else 1) { rememberLazyListState() }
    }
    val listStates = if (state.search.isNotBlank()) searchListStates else normalListStates
    val keyboard = LocalSoftwareKeyboardController.current
    val focusManager = LocalFocusManager.current
    fun closeSearch() {
        keyboard?.hide()
        focusManager.clearFocus()
        viewModel.closeSearch()
    }
    BackHandler(enabled = discoverySource == null && state.isSearchOpen && state.selectedFlightId == null) { closeSearch() }
    val pagerState = rememberPagerState(initialPage = selectedTab) { if (discoverySource == null) FlightCategory.entries.size else 1 }
    LaunchedEffect(viewModel, discoverySource) { if (discoverySource != null) viewModel.openSearch() }
    val pagerScope = rememberCoroutineScope()
    val notifyTabSelection by rememberUpdatedState(onSelectTab)
    val requestedTab = if (notificationOpenRequest != null) 0 else selectedTab
    LaunchedEffect(viewModel, requestedTab, notificationOpenRequest) {
        if (discoverySource != null) return@LaunchedEffect
        // A selection echoed by the host must not interrupt the next swipe.
        if (pagerState.settledPage != requestedTab ||
            (notificationOpenRequest != null && pagerState.isScrollInProgress)) {
            pagerState.animateScrollToPage(requestedTab,
                animationSpec = tween(TAB_SLIDE_MILLIS, easing = FastOutSlowInEasing))
        }
        viewModel.setCategory(FlightCategory.entries[requestedTab])
    }
    LaunchedEffect(viewModel, pagerState) {
        if (discoverySource != null) return@LaunchedEffect
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
        pendingNavigationFlightId = null
        pendingNavigationKey = null
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
    fun openPending(flightId: String, targetKey: String) {
        keyboard?.hide()
        focusManager.clearFocus()
        pendingNavigationFlightId = flightId
        pendingNavigationKey = targetKey
        viewModel.openFlight(flightId)
    }
    Column(Modifier.fillMaxSize().imePadding()) {
        if (discoverySource == null) FlightTabSearchHeader(isSearchOpen = state.isSearchOpen, query = state.search,
            onQueryChange = viewModel::setSearch, onOpenSearch = viewModel::openSearch,
            onCloseSearch = ::closeSearch) {
            FlightsPageTabs(pagerState = pagerState, onSelectTab = selectTab, enabled = !state.isSearchOpen)
        }
        else {
            val placeholder = stringResource(if (discoverySource == FlightDiscoverySource.Search)
                R.string.home_search_flight_number else R.string.home_search_my_work)
            FlightTabSearchHeader(isSearchOpen = true, query = state.search,
                onQueryChange = viewModel::setSearch, onOpenSearch = viewModel::openSearch,
                onCloseSearch = {}, searchDescription = placeholder, placeholder = placeholder,
                showCloseButton = false, autoFocusOnOpen = false, searchAtPhysicalRight = true,
                searchIcon = if (discoverySource == FlightDiscoverySource.Search) Icons.AutoMirrored.Filled.ManageSearch else Icons.Default.Search,
                tabs = {})
        }
        if (state.isUsingCachedList || !state.isOnline) {
            InlineNotice(stringResource(if (state.isUsingCachedList) R.string.flights_cached_list else R.string.flights_offline_hint),
                modifier = Modifier.padding(horizontal = 16.dp, vertical = 4.dp), kind = InlineNoticeKind.Info)
        }
        if (discoverySource != null) Box(Modifier.weight(1f).fillMaxWidth()) {
            FlightListPage(state, viewModel, listStates.first(), bottomContentPadding, active = true,
                onPendingClick = ::openPending, discoverySource = discoverySource)
        }
        else HorizontalPager(state = pagerState, modifier = Modifier.weight(1f).fillMaxWidth().clipToBounds()) { page ->
            val category = FlightCategory.entries[page]
            FlightListPage(state = state.forCategory(category), viewModel = viewModel,
                listState = listStates[page],
                bottomContentPadding = bottomContentPadding,
                active = page == pagerState.settledPage && state.category == category,
                onPendingClick = ::openPending)
        }
        if (discoverySource == null && state.isSearchOpen) {
            FlightSearchScopeTabs(selectedTab = pagerState.currentPage, onSelectTab = selectTab,
                modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp))
        }
    }
    if (state.selectedFlightId != null) key(state.selectedFlightId) {
        var sheetNow by remember { mutableStateOf(Instant.now()) }
        val sheetLifecycleOwner = LocalLifecycleOwner.current
        LaunchedEffect(sheetLifecycleOwner) {
            sheetLifecycleOwner.lifecycle.repeatOnLifecycle(Lifecycle.State.RESUMED) {
                while (true) {
                    sheetNow = Instant.now()
                    delay(60_000L - sheetNow.toEpochMilli() % 60_000L)
                }
            }
        }
        // These subscriptions outlive the editor so a completed disk save updates the sheet immediately.
        val pendingArrivals by (viewModel.arrivals?.changes ?: kotlinx.coroutines.flow.flowOf(emptyList()))
            .collectAsStateWithLifecycle(initialValue = emptyList())
        val pendingDepartures by (viewModel.departures?.changes ?: kotlinx.coroutines.flow.flowOf(emptyList()))
            .collectAsStateWithLifecycle(initialValue = emptyList())
        val pendingCancellations by (viewModel.cancellations?.changes ?: kotlinx.coroutines.flow.flowOf(emptyList()))
            .collectAsStateWithLifecycle(initialValue = emptyList())
        val pendingServices by (viewModel.services?.changes ?: kotlinx.coroutines.flow.flowOf(emptyList()))
            .collectAsStateWithLifecycle(initialValue = emptyList())
        val pendingTasks by (viewModel.tasks?.changes ?: kotlinx.coroutines.flow.flowOf(emptyList()))
            .collectAsStateWithLifecycle(initialValue = emptyList())
        val serviceFormDrafts by (viewModel.services?.drafts ?: kotlinx.coroutines.flow.flowOf(emptyList()))
            .collectAsStateWithLifecycle(initialValue = emptyList())
        val pendingCancellation = pendingCancellations.firstOrNull { it.flightId == state.selectedFlightId }
        val aircraftTypes by viewModel.arrivalAircraftTypes.collectAsStateWithLifecycle(initialValue = emptyList())
        val sheetCatalogs by viewModel.operationsCatalogs.collectAsStateWithLifecycle(initialValue = FlightOperationsCatalogs())
        val actualsStaff by viewModel.actualsStaff.collectAsStateWithLifecycle(initialValue = emptyList())
        val currentUserName by viewModel.currentUserDisplayName.collectAsStateWithLifecycle(initialValue = null)
        val actuals = state.detail?.actualDetails(
            pendingArrival = pendingArrivals.firstOrNull { it.flightId == state.selectedFlightId },
            pendingDeparture = pendingDepartures.firstOrNull { it.flightId == state.selectedFlightId },
            aircraftTypes = aircraftTypes, stationStaff = actualsStaff, currentUserName = currentUserName,
            youLabel = stringResource(R.string.flight_actuals_you))
        val progressStatus = state.detail?.progressStatus(
            pendingArrival = pendingArrivals.firstOrNull { it.flightId == state.selectedFlightId },
            pendingDeparture = pendingDepartures.firstOrNull { it.flightId == state.selectedFlightId },
            pendingCancellation = pendingCancellation) ?: FlightProgressStatus.Draft
        val ownsWork = state.detail?.hasOwnWork() == true || pendingArrivals.any { it.flightId == state.selectedFlightId } ||
            pendingDepartures.any { it.flightId == state.selectedFlightId } ||
            pendingServices.any { it.flightId == state.selectedFlightId } || pendingTasks.any { it.flightId == state.selectedFlightId } ||
            state.detail?.services?.isNotEmpty() == true || state.detail?.tasks?.isNotEmpty() == true
        val ownsCancellation = state.detail?.hasOwnCancellation() == true || pendingCancellation != null
        var confirmRevert by rememberSaveable { mutableStateOf(false) }
        var revertError by rememberSaveable { mutableStateOf<String?>(null) }
        var reverting by remember { mutableStateOf(false) }
        val cancellationScope = rememberCoroutineScope()
        if (confirmRevert) FlightSubmissionConfirmation(stringResource(R.string.cancellation_revert),
            stringResource(R.string.cancellation_revert_message), stringResource(R.string.cancellation_revert), onConfirm = {
                confirmRevert = false
                state.detail?.let { detail ->
                    reverting = true; revertError = null
                    cancellationScope.launch {
                        try {
                            val reviewed = if (pendingCancellation != null && pendingCancellation.status != "pending") viewModel.reviewArrival(detail.id) else detail
                            viewModel.revertCancellation(reviewed)
                        } catch (cancelled: kotlinx.coroutines.CancellationException) { throw cancelled }
                        catch (error: Exception) { revertError = error.userMessage() }
                        finally { reverting = false }
                    }
                }
            }, onDismiss = { confirmRevert = false })
        if (revertError != null) androidx.compose.material3.AlertDialog(onDismissRequest = { revertError = null },
            title = { Text(stringResource(R.string.cancellation_revert_failed)) }, text = { Text(revertError.orEmpty()) },
            confirmButton = { TextButton(onClick = { revertError = null }) { Text(stringResource(R.string.flights_cancel)) } })
        var recordingForm by rememberSaveable { mutableStateOf<String?>(null) }
        // A reopened form starts from durable data; only rotation restores the same editor session.
        var recordingFormSessionId by rememberSaveable { mutableStateOf(UUID.randomUUID().toString()) }
        // The open form owns a stable snapshot, including across activity recreation.
        // Background detail updates must not replace the form's original snapshot.
        var recordingFlightSnapshot by rememberSaveable { mutableStateOf<String?>(null) }
        var recordingInitialNavigationKey by rememberSaveable { mutableStateOf<String?>(null) }
        val recordingFlight = remember(recordingFlightSnapshot) {
            recordingFlightSnapshot?.let { Json.decodeFromString<FlightDetail>(it) }
        }
        val recordingFormState = rememberSaveableStateHolder()
        fun openRecordingForm(kind: String, initialNavigationKey: String? = null) {
            recordingInitialNavigationKey = initialNavigationKey
            recordingFlightSnapshot = state.detail?.let { Json.encodeToString(it) }
            recordingFormSessionId = UUID.randomUUID().toString()
            recordingForm = kind
        }
        LaunchedEffect(state.detail, pendingNavigationFlightId, pendingNavigationKey) {
            if (state.detail?.id == pendingNavigationFlightId && pendingNavigationKey != null) {
                val target = pendingNavigationKey
                openRecordingForm(if (target == "cancellation") "cancellation" else "operations", target)
                pendingNavigationFlightId = null
                pendingNavigationKey = null
            }
        }
        val sheetListItem = state.items.firstOrNull { it.id == state.selectedFlightId }
        val sheetWork = remember(state.detail, pendingServices, pendingTasks, sheetCatalogs) {
            state.detail?.let { detail ->
                flightSheetWorkItems(detail, pendingServices, pendingTasks, sheetCatalogs.forStation(detail.stationId))
            }.orEmpty()
        }
        val sheetReportStatus = state.detail?.formCardSummary(sheetListItem, emptyList())?.cardReportStatus(
            localArrival = pendingArrivals.any { it.flightId == state.selectedFlightId },
            localDeparture = pendingDepartures.any { it.flightId == state.selectedFlightId },
            localCancellation = pendingCancellation?.keepsCancellationOnCard() == true,
            now = sheetNow,
        ) ?: FlightCardReportStatus.Scheduled
        FlightDetailsSheet(state = state, actuals = actuals, reportStatus = sheetReportStatus, listItem = sheetListItem, workItems = sheetWork,
            showCancellation = !ownsWork && !ownsCancellation && state.detail?.cancellationEditable() == true,
            showOperations = !ownsCancellation,
            showRevertCancellation = !reverting && state.detail?.canSaveCancellation == true &&
                (state.detail?.ownCancellation() != null || pendingCancellation?.operation == "save") &&
                (pendingCancellation?.operation != "revert" || pendingCancellation.status != "pending"),
            showReviewCancellation = pendingCancellation?.operation == "save" && pendingCancellation.status != "pending",
            onRevertCancellation = { confirmRevert = true }, onClose = ::closeFlight,
            onRetry = { state.selectedFlightId?.let(viewModel::openFlight) },
            onInvite = viewModel::openInvitations,
            onOperations = { openRecordingForm("operations") },
            onCanceled = { openRecordingForm("cancellation") },
            loadCustomerLogo = viewModel::customerLogo)
        // An ordinary silent detail refresh must not replace an open form or discard its edits.
        val flight = recordingFlight ?: state.detail
        val activeForm = recordingForm
        if (activeForm != null && flight != null) {
            val activeFormKey = "$activeForm:$recordingFormSessionId"
            recordingFormState.SaveableStateProvider(activeFormKey) {
                val closeForm = {
                    recordingForm = null
                    recordingFlightSnapshot = null
                    recordingInitialNavigationKey = null
                    recordingFormState.removeState(activeFormKey)
                }
                if (activeForm == "operations") {
                    val catalogs by viewModel.operationsCatalogs.collectAsStateWithLifecycle(initialValue = FlightOperationsCatalogs())
                    if ((state.detail ?: flight).isAdHocFlight && viewModel.adHocRepository != null) {
                        AdHocFlightEditor(viewModel, state.detail ?: flight, closeForm,
                            initialNavigationKey = recordingInitialNavigationKey, progressStatus = progressStatus)
                    } else FlightOperationsForm(state.detail ?: flight, aircraftTypes, viewModel::customerLogo, onClose = closeForm, catalogs = catalogs.forStation(flight.stationId),
                        listItem = state.items.firstOrNull { it.id == flight.id },
                        initialNavigationKey = recordingInitialNavigationKey,
                        pending = pendingArrivals.firstOrNull { it.flightId == flight.id },
                        pendingDeparture = pendingDepartures.firstOrNull { it.flightId == flight.id },
                        pendingServices = pendingServices.filter { it.flightId == flight.id },
                        serviceFormDraft = serviceFormDrafts.firstOrNull { it.flightId == flight.id },
                        onSaveServices = viewModel::saveServices,
                        pendingTasks = pendingTasks.filter { it.flightId == flight.id },
                        onSaveTasks = viewModel::saveTasks,
                        onReviewTasks = { viewModel.reviewFlightInput(flight.id) },
                        loadTaskAttachment = viewModel::taskAttachment,
                        loadPendingTaskAttachment = viewModel::pendingTaskAttachment,
                        onSaveBatch = viewModel::saveOperationBatch,
                        onReviewServices = { viewModel.reviewFlightInput(flight.id) },
                        loadServiceAttachment = viewModel::serviceAttachment,
                        loadPendingServiceAttachment = viewModel::pendingServiceAttachment,
                        progressStatus = progressStatus,
                        loadSignature = viewModel::departureSignature,
                        onSaveArrival = { detail, input -> checkNotNull(viewModel.arrivals).save(detail, input) },
                        onReviewArrival = { viewModel.reviewFlightInput(flight.id) },
                        onSaveDeparture = viewModel::saveDeparture,
                        onSaveReviewed = viewModel::saveReviewed,
                        onDiscardReviewed = { target -> viewModel.discardReviewed(flight.id, target) })
                } else {
                    FlightCancellationForm(state.detail ?: flight, viewModel::customerLogo, onClose = closeForm, progressStatus = progressStatus,
                        pending = pendingCancellation, onSave = viewModel::saveCancellation,
                        onReview = { viewModel.reviewArrival(flight.id) })
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
    bottomContentPadding: Dp,
    active: Boolean,
    onPendingClick: (flightId: String, navigationKey: String) -> Unit,
    discoverySource: FlightDiscoverySource? = null,
) {
    // Account-scoped durable input updates card badges immediately, without detail HTTP reads.
    val savedArrivals by (viewModel.arrivals?.changes ?: kotlinx.coroutines.flow.flowOf(emptyList()))
        .collectAsStateWithLifecycle(initialValue = emptyList())
    val savedDepartures by (viewModel.departures?.changes ?: kotlinx.coroutines.flow.flowOf(emptyList()))
        .collectAsStateWithLifecycle(initialValue = emptyList())
    val savedCancellations by (viewModel.cancellations?.changes ?: kotlinx.coroutines.flow.flowOf(emptyList()))
        .collectAsStateWithLifecycle(initialValue = emptyList())
    val savedServices by (viewModel.services?.changes ?: kotlinx.coroutines.flow.flowOf(emptyList()))
        .collectAsStateWithLifecycle(initialValue = emptyList())
    val savedTasks by (viewModel.tasks?.changes ?: kotlinx.coroutines.flow.flowOf(emptyList()))
        .collectAsStateWithLifecycle(initialValue = emptyList())
    val savedAdHoc by (viewModel.adHocRepository?.pending ?: kotlinx.coroutines.flow.flowOf(emptyList()))
        .collectAsStateWithLifecycle(initialValue = emptyList())
    val adHocById = remember(savedAdHoc) { savedAdHoc.associateBy { it.flightId } }
    val workNeedsReviewIds = remember(savedServices, savedTasks) {
        (savedServices.filter { it.status != "pending" }.map { it.flightId } + savedTasks.filter { it.status != "pending" }.map { it.flightId }).toSet()
    }
    val pendingWorkIds = remember(savedServices, savedTasks) { (savedServices.map { it.flightId } + savedTasks.map { it.flightId }).toSet() }
    val arrivalsById = remember(savedArrivals) { savedArrivals.associateBy { it.flightId } }
    val departuresById = remember(savedDepartures) { savedDepartures.associateBy { it.flightId } }
    val cancellationsById = remember(savedCancellations) { savedCancellations.associateBy { it.flightId } }
    val cancellationIds = remember(savedCancellations) {
        savedCancellations.filter { it.keepsCancellationOnCard() }.mapTo(mutableSetOf()) { it.flightId }
    }
    var cardNow by remember { mutableStateOf(Instant.now()) }
    val lifecycleOwner = LocalLifecycleOwner.current
    LaunchedEffect(active, lifecycleOwner) {
        if (!active) return@LaunchedEffect
        lifecycleOwner.lifecycle.repeatOnLifecycle(Lifecycle.State.RESUMED) {
            while (true) {
                cardNow = Instant.now()
                delay(60_000L - cardNow.toEpochMilli() % 60_000L)
            }
        }
    }
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
    val feedRows = remember(state.items, discoverySource, state.search, state.totalCount) {
        flightDiscoveryRows(state.items, discoverySource == FlightDiscoverySource.Search &&
            state.search.isNotBlank() && (state.totalCount > 1 || state.items.size > 1))
    }
    PullToRefreshBox(isRefreshing = state.showRefreshIndicator, onRefresh = onRefresh,
        modifier = if (active) Modifier.fillMaxSize() else Modifier.fillMaxSize().clearAndSetSemantics { }) {
        when {
            discoverySource == FlightDiscoverySource.Search && state.search.isBlank() && state.items.isEmpty() ->
                Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp)) {
                    if (state.permissionDenied || state.isOnline) state.error?.let {
                        InlineNotice(it, kind = InlineNoticeKind.Error, onRetry = onRefresh)
                    }
                }
            !state.hasLoadedList && state.items.isEmpty() && (state.isLoading || state.isRefreshing ||
                (state.hasMore && state.error == null && state.loadMoreError == null)) ->
                FlightListSkeleton(Modifier.fillMaxSize())
            state.error != null && state.items.isEmpty() -> Box(Modifier.fillMaxSize().verticalScroll(rememberScrollState()), contentAlignment = Alignment.Center) {
                InlineNotice(message = if (state.permissionDenied) stringResource(R.string.flights_view_forbidden) else state.error,
                    modifier = Modifier.padding(16.dp), kind = InlineNoticeKind.Error, onRetry = onRefresh)
            }
            state.items.isEmpty() && state.loadMoreError != null -> Box(Modifier.fillMaxSize().verticalScroll(rememberScrollState()), contentAlignment = Alignment.Center) {
                InlineNotice(message = state.loadMoreError, modifier = Modifier.padding(16.dp),
                    kind = InlineNoticeKind.Error, onRetry = onLoadMore)
            }
            state.items.isEmpty() -> Box(Modifier.fillMaxSize().verticalScroll(rememberScrollState()), contentAlignment = Alignment.Center) {
                EmptyState(icon = Icons.Default.FlightTakeoff,
                    title = stringResource(if (state.search.isNotBlank()) R.string.flights_no_matches
                        else if (discoverySource == FlightDiscoverySource.History) R.string.flight_history_empty
                        else when (state.category) {
                            FlightCategory.PerLanding -> R.string.flights_per_landing_empty
                            FlightCategory.AdHoc -> R.string.flights_adhoc_empty
                            FlightCategory.MyFlights -> R.string.flights_empty
                        }),
                    message = stringResource(when {
                        discoverySource == FlightDiscoverySource.History && state.search.isBlank() -> R.string.flight_history_empty_hint
                        discoverySource == FlightDiscoverySource.Search -> R.string.flight_exact_search_empty_hint
                        state.search.isBlank() -> R.string.flights_empty_hint
                        else -> R.string.flights_search_hint
                    }))
            }
            else -> LazyColumn(
                state = listState,
                contentPadding = PaddingValues(start = 16.dp, top = 16.dp, end = 16.dp, bottom = bottomContentPadding),
                verticalArrangement = Arrangement.spacedBy(8.dp),
                modifier = Modifier.fillMaxSize(),
            ) {
                state.error?.let { error ->
                    item(key = "list-error") {
                        InlineNotice(error, kind = InlineNoticeKind.Error, onRetry = onRefresh)
                    }
                }
                if (discoverySource == FlightDiscoverySource.Search && state.search.isBlank()) item(key = "recently-viewed") {
                    FeedDateHeading(stringResource(R.string.flight_recently_viewed), horizontalPadding = 0.dp)
                }
                items(feedRows, key = { it.key }, contentType = { if (it is FlightDiscoveryFeedRow.Heading) "heading" else "flight" }) { row ->
                    if (row is FlightDiscoveryFeedRow.Heading) {
                        val today = cardNow.atZone(ZoneId.of(row.stationTimeZoneId)).toLocalDate()
                        val label = when (row.date) {
                            null -> stringResource(R.string.feed_earlier)
                            today -> stringResource(R.string.feed_today)
                            today.minusDays(1) -> stringResource(R.string.feed_yesterday)
                            else -> row.date.format(DateTimeFormatter.ofPattern(
                                if (row.date.year == today.year) "MMMM d" else "MMMM d, yyyy", LocalLocale.current.platformLocale))
                        }
                        FeedDateHeading(label, horizontalPadding = 0.dp)
                    } else {
                    val flight = (row as FlightDiscoveryFeedRow.Flight).flight
                    val pendingProgress = flight.cardPendingProgress(arrivalsById[flight.id], departuresById[flight.id], cancellationsById[flight.id])
                    val needsReview = pendingProgress == FlightProgressStatus.ArrivalNeedsReview ||
                        pendingProgress == FlightProgressStatus.DepartureNeedsReview || pendingProgress == FlightProgressStatus.CancellationNeedsReview ||
                        flight.id in workNeedsReviewIds || adHocById[flight.id]?.status?.let { it != "pending" } == true
                    val pendingLabel = when {
                        needsReview -> stringResource(R.string.flight_card_needs_review)
                        pendingProgress == FlightProgressStatus.CancellationRevertPending -> stringResource(R.string.flight_card_revert_pending)
                        pendingProgress != null || flight.id in pendingWorkIds || flight.id in adHocById -> stringResource(R.string.flight_card_pending_sync)
                        else -> null
                    }
                    val flightServices = savedServices.filter { it.flightId == flight.id }
                    val flightTasks = savedTasks.filter { it.flightId == flight.id }
                    val navigationTarget = (if (needsReview) flightReviewTargets(arrivalsById[flight.id], departuresById[flight.id],
                        flightServices, flightTasks) else flightPendingTargets(arrivalsById[flight.id], departuresById[flight.id],
                        flightServices, flightTasks)).firstOrNull()?.navigationKey()
                        ?: cancellationsById[flight.id]?.let { "cancellation" }
                        ?: adHocById[flight.id]?.let { "adhoc" }
                    val navigateToPending: (() -> Unit)? = navigationTarget?.let { target -> {
                        if (active) onPendingClick(flight.id, target)
                    } }
                    FlightCard(flight = flight,
                        now = cardNow,
                        reportStatus = flight.cardReportStatus(flight.id in arrivalsById, flight.id in departuresById, flight.id in cancellationIds, cardNow),
                        pendingSyncLabel = pendingLabel,
                        hasSyncError = needsReview,
                        onReviewClick = navigateToPending,
                        onPendingClick = navigateToPending,
                        cancellationAtUtc = cancellationsById[flight.id]?.cancellationAtUtcForCard() ?: flight.latestCancellationAtUtc,
                        onClick = {
                        if (active) {
                            keyboard?.hide()
                            focusManager.clearFocus()
                            viewModel.openFlight(flight.id)
                        }
                    },
                        loadCustomerLogo = viewModel::customerLogo)
                    }
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
                        InlineNotice(error, kind = InlineNoticeKind.Error, onRetry = onLoadMore)
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
