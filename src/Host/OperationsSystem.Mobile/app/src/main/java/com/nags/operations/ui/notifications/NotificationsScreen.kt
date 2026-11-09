package com.nags.operations.ui.notifications

import androidx.compose.animation.core.animate
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.foundation.gestures.AnchoredDraggableState
import androidx.compose.foundation.gestures.DraggableAnchors
import androidx.compose.foundation.gestures.FlingBehavior
import androidx.compose.foundation.gestures.ScrollScope
import androidx.compose.foundation.gestures.Orientation
import androidx.compose.foundation.gestures.anchoredDraggable
import androidx.compose.foundation.gestures.animateTo
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Archive
import androidx.compose.material.icons.filled.DoneAll
import androidx.compose.material.icons.filled.FlightTakeoff
import androidx.compose.material.icons.filled.EventRepeat
import androidx.compose.material.icons.filled.GroupAdd
import androidx.compose.material.icons.filled.NotificationsActive
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshotFlow
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.RectangleShape
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.CustomAccessibilityAction
import androidx.compose.ui.semantics.customActions
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.LifecycleResumeEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.nags.operations.R
import com.nags.operations.ui.flights.FlightFormGround
import com.nags.operations.data.notifications.flightId
import com.nags.operations.data.notifications.isExpiredReminder
import com.nags.operations.data.notifications.NotificationKinds
import com.nags.operations.data.notifications.NotificationDto
import com.nags.operations.data.notifications.NotificationOpenRequest
import com.nags.operations.ui.components.EmptyState
import com.nags.operations.ui.components.ErrorState
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlin.math.roundToInt
import java.time.Instant

private val NotificationDeleteRed = Color(0xFFD92D3F)
private val NotificationInk = Color(0xFF111827)
private val NotificationSecondaryInk = Color(0xFF576170)
private val NotificationNewFill = Color(0xFFF0F6FF)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun NotificationsScreen(
    viewModel: NotificationsViewModel,
    onBack: () -> Unit,
    onOpenFlight: (NotificationOpenRequest) -> Unit,
    onOpenSchedule: (NotificationOpenRequest) -> Unit = {},
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    var confirmClear by remember { mutableStateOf(false) }
    LifecycleResumeEffect(viewModel) {
        viewModel.beginVisit()
        onPauseOrDispose { viewModel.endVisit() }
    }
    LaunchedEffect(viewModel, state.items, state.visitId, state.isLoading, state.isRefreshing) {
        viewModel.acknowledgeLoadedInbox()
    }

    CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Ltr) {
        Scaffold(
            topBar = {
                TopAppBar(
                    title = { Text(stringResource(R.string.notifications_title),
                        style = MaterialTheme.typography.titleMedium.copy(fontSize = 18.sp),
                        fontWeight = FontWeight.SemiBold) },
                    navigationIcon = {
                        IconButton(onClick = onBack) {
                            Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = stringResource(R.string.notifications_back))
                        }
                    },
                    actions = {
                        IconButton(onClick = { confirmClear = true }, enabled = state.items.isNotEmpty() && !state.isArchivingAll) {
                            Icon(Icons.Default.Archive, contentDescription = stringResource(R.string.notifications_clear_all))
                        }
                    },
                    colors = TopAppBarDefaults.topAppBarColors(
                        containerColor = Color.White,
                        scrolledContainerColor = Color.White,
                        titleContentColor = NotificationInk,
                        navigationIconContentColor = NotificationInk,
                        actionIconContentColor = NotificationDeleteRed,
                    ),
                )
            },
            containerColor = FlightFormGround,
        ) { padding ->
            Column(Modifier.fillMaxSize().padding(padding)) {
                HorizontalDivider(color = Color(0xFFE3E6EB))
                val listState = rememberLazyListState()
                NotificationList(state, viewModel, listState,
                    onOpen = { viewModel.open(it, onOpenFlight, onOpenSchedule) })
            }
        }
        if (confirmClear) {
            AlertDialog(
                onDismissRequest = { confirmClear = false },
                containerColor = Color.White,
                tonalElevation = 0.dp,
                title = { Text(stringResource(R.string.notifications_clear_confirm_title)) },
                text = { Text(stringResource(R.string.notifications_clear_confirm_body)) },
                confirmButton = {
                    TextButton(onClick = { confirmClear = false; viewModel.archiveAll() },
                        colors = ButtonDefaults.textButtonColors(contentColor = NotificationDeleteRed)) {
                        Text(stringResource(R.string.notifications_clear_confirm_action))
                    }
                },
                dismissButton = {
                    TextButton(onClick = { confirmClear = false }) { Text(stringResource(R.string.notifications_cancel)) }
                },
            )
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun NotificationList(
    state: NotificationsViewModel.UiState,
    viewModel: NotificationsViewModel,
    listState: LazyListState,
    onOpen: (NotificationDto) -> Unit,
) {
    var revealedId by remember { mutableStateOf<String?>(null) }
    var now by remember { mutableStateOf(Instant.now()) }
    LaunchedEffect(Unit) {
        while (true) {
            delay(60_000)
            now = Instant.now()
        }
    }
    val rows = remember(state.items, state.newIds, now) { notificationFeedRows(state.items, state.newIds, now) }
    val currentState by rememberUpdatedState(state)
    LaunchedEffect(viewModel, listState) {
        snapshotFlow {
            nextNotificationPageToLoad(currentState, listState.layoutInfo.visibleItemsInfo.lastOrNull()?.index ?: -1,
                listState.layoutInfo.totalItemsCount)
        }.distinctUntilChanged().collect { page -> if (page != null) viewModel.loadMore() }
    }
    Column(Modifier.fillMaxSize()) {
        state.actionError?.let { message ->
            Text(message, style = MaterialTheme.typography.bodySmall, color = NotificationDeleteRed,
                modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp))
        }
        if (state.isFromCache) {
            Text(stringResource(R.string.notifications_cached_list), style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 4.dp))
        }
        PullToRefreshBox(isRefreshing = state.isRefreshing,
            onRefresh = { viewModel.refresh(userInitiated = true) }, modifier = Modifier.weight(1f).fillMaxWidth()) {
            when {
                state.isLoading && state.items.isEmpty() -> NotificationListSkeleton()
                state.error != null && state.items.isEmpty() -> Box(
                    Modifier.fillMaxSize().verticalScroll(rememberScrollState()), contentAlignment = Alignment.Center,
                ) {
                    ErrorState(title = stringResource(R.string.notifications_load_error), message = state.error,
                        onRetry = { viewModel.refresh(userInitiated = true) })
                }
                state.items.isEmpty() -> Box(
                    Modifier.fillMaxSize().verticalScroll(rememberScrollState()), contentAlignment = Alignment.Center,
                ) {
                    EmptyState(icon = Icons.Default.DoneAll, title = stringResource(R.string.notifications_empty_title),
                        message = stringResource(R.string.notifications_empty_body))
                }
                else -> LazyColumn(state = listState,
                    contentPadding = PaddingValues(top = 4.dp, bottom = 24.dp),
                    verticalArrangement = Arrangement.spacedBy(0.dp), modifier = Modifier.fillMaxSize()) {
                    state.error?.let { message ->
                        item(key = "refresh-error") {
                            Text(message, color = MaterialTheme.colorScheme.error)
                            TextButton(onClick = { viewModel.refresh(userInitiated = true) }) {
                                Text(stringResource(R.string.notifications_retry))
                            }
                        }
                    }
                    items(rows, key = { it.key }, contentType = { if (it is NotificationFeedRow.Heading) "heading" else "notification" }) { row ->
                        when (row) {
                            is NotificationFeedRow.Heading -> Text(row.label,
                                style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.SemiBold,
                                modifier = Modifier.fillMaxWidth().padding(start = 16.dp, top = 18.dp, bottom = 10.dp))
                            is NotificationFeedRow.Item -> NotificationCard(row.notification, now,
                                modifier = Modifier.animateItem(fadeInSpec = null, fadeOutSpec = tween(160),
                                    placementSpec = spring(dampingRatio = 1f, stiffness = 450f)),
                                isNew = row.notification.id in state.newIds || !row.notification.isRead,
                                canOpen = row.notification.kind != NotificationKinds.FlightScheduleUpdated &&
                                    !row.notification.isExpiredReminder(now) &&
                                    row.notification.flightId() in state.openableFlightIds,
                                opening = row.notification.id in state.openingIds,
                                archiving = row.notification.id in state.archivingIds,
                                revealed = revealedId == row.notification.id,
                                onReveal = { revealedId = row.notification.id },
                                onClose = { if (revealedId == row.notification.id) revealedId = null },
                                onClick = { onOpen(row.notification) },
                                onArchive = { onFailure -> viewModel.archive(row.notification, onFailure) })
                        }
                    }
                    if (state.isLoadingMore) {
                        item(key = "paging") {
                            Box(Modifier.fillMaxWidth().padding(16.dp), contentAlignment = Alignment.Center) {
                                CircularProgressIndicator(Modifier.size(24.dp), strokeWidth = 2.dp)
                            }
                        }
                    }
                    state.loadMoreError?.let { message ->
                        item(key = "paging-error") {
                            Column(Modifier.fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally) {
                                Text(message, color = MaterialTheme.colorScheme.error)
                                TextButton(onClick = viewModel::loadMore) { Text(stringResource(R.string.notifications_retry)) }
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun NotificationCard(notification: NotificationDto, now: Instant, isNew: Boolean,
    modifier: Modifier = Modifier, canOpen: Boolean, opening: Boolean, archiving: Boolean,
    revealed: Boolean, onReveal: () -> Unit, onClose: () -> Unit,
    onClick: () -> Unit, onArchive: (() -> Unit) -> Unit) {
    val scope = rememberCoroutineScope()
    var deleteRequested by remember(notification.id) { mutableStateOf(false) }
    val deleteLabel = stringResource(R.string.notifications_delete_accessibility)
    BoxWithConstraints(modifier.fillMaxWidth().clip(RectangleShape)) {
        val width = constraints.maxWidth.toFloat()
        val actionWidth = with(LocalDensity.current) { 80.dp.toPx() }
        val swipe = remember(notification.id, width, actionWidth) {
            AnchoredDraggableState(NotificationSwipePosition.Closed, DraggableAnchors {
                NotificationSwipePosition.Closed at 0f
                NotificationSwipePosition.Revealed at -actionWidth
                NotificationSwipePosition.Committed at -width
            })
        }
        val fling = remember(swipe, width, actionWidth) {
            object : FlingBehavior {
                override suspend fun ScrollScope.performFling(initialVelocity: Float): Float {
                    val target = notificationSwipeTarget(swipe.requireOffset(), initialVelocity, width, actionWidth)
                    val targetOffset = swipe.anchors.positionOf(target)
                    animate(swipe.requireOffset(), targetOffset, initialVelocity,
                        animationSpec = spring(dampingRatio = 1f, stiffness = 450f)) { value, _ ->
                        scrollBy(value - swipe.requireOffset())
                    }
                    return 0f
                }
            }
        }
        val requestDelete: () -> Unit = {
            if (!deleteRequested && !archiving) {
                deleteRequested = true
                onReveal()
                scope.launch { swipe.animateTo(NotificationSwipePosition.Committed) }
                onArchive {
                    deleteRequested = false
                    scope.launch { swipe.animateTo(NotificationSwipePosition.Revealed) }
                }
            }
        }
        val latestDelete by rememberUpdatedState(requestDelete)
        LaunchedEffect(swipe.settledValue) {
            when (swipe.settledValue) {
                NotificationSwipePosition.Closed -> onClose()
                NotificationSwipePosition.Revealed -> onReveal()
                NotificationSwipePosition.Committed -> latestDelete()
            }
        }
        LaunchedEffect(revealed) {
            if (!revealed && !deleteRequested && !archiving) swipe.animateTo(NotificationSwipePosition.Closed)
        }
        val offset = swipe.requireOffset()
        val busy = deleteRequested || archiving
        Box(Modifier.matchParentSize().background(FlightFormGround), contentAlignment = Alignment.CenterEnd) {
            if (offset < -1f) Box(Modifier.width(80.dp), contentAlignment = Alignment.Center) {
                Button(onClick = requestDelete, enabled = !busy,
                    modifier = Modifier.width(64.dp).height(34.dp), shape = RoundedCornerShape(10.dp),
                    colors = ButtonDefaults.buttonColors(containerColor = NotificationDeleteRed, contentColor = Color.White,
                        disabledContainerColor = NotificationDeleteRed.copy(alpha = 0.65f), disabledContentColor = Color.White),
                    contentPadding = PaddingValues(horizontal = 6.dp, vertical = 0.dp)) {
                    if (busy) CircularProgressIndicator(Modifier.size(16.dp), color = Color.White, strokeWidth = 2.dp)
                    else Text(stringResource(R.string.notifications_delete), style = MaterialTheme.typography.bodySmall,
                        fontWeight = FontWeight.SemiBold, maxLines = 1)
                }
            }
        }
        Row(Modifier.fillMaxWidth()
            .offset { IntOffset(swipe.requireOffset().roundToInt(), 0) }
            .anchoredDraggable(swipe, orientation = Orientation.Horizontal, enabled = !busy,
                flingBehavior = fling)
            .semantics { customActions = listOf(CustomAccessibilityAction(deleteLabel) { requestDelete(); true }) }
            .background(if (isNew) NotificationNewFill else Color.White)
            .padding(horizontal = 16.dp, vertical = 12.dp),
            horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
            val accent = Color(notificationAccentArgb(notification.kind))
            Box(Modifier.size(40.dp).background(accent.copy(alpha = 0.1f), RoundedCornerShape(20.dp)),
                contentAlignment = Alignment.Center) {
                Icon(when (notification.kind) {
                    NotificationKinds.EmployeeInvitedToFlight -> Icons.Default.GroupAdd
                    NotificationKinds.FlightReminder -> Icons.Default.NotificationsActive
                    NotificationKinds.FlightScheduleUpdated -> Icons.Default.EventRepeat
                    else -> Icons.Default.FlightTakeoff
                }, contentDescription = null, tint = accent, modifier = Modifier.size(22.dp))
            }
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                Text(notification.titleEn, style = MaterialTheme.typography.titleSmall, color = NotificationInk,
                    fontWeight = if (isNew) FontWeight.SemiBold else FontWeight.Medium,
                    maxLines = 2, overflow = TextOverflow.Ellipsis)
                val age = relativeNotificationAge(notification.createdAtUtc, now)
                Text(buildAnnotatedString {
                    append(notification.bodyEn.trimEnd())
                    if (age.isNotEmpty()) {
                        append("  ")
                        withStyle(SpanStyle(fontWeight = FontWeight.Bold)) { append(age) }
                    }
                }, style = MaterialTheme.typography.bodySmall, color = NotificationSecondaryInk)
            }
            if (canOpen) Button(onClick = onClick, enabled = !busy && !opening,
                modifier = Modifier.width(64.dp).height(34.dp), shape = RoundedCornerShape(8.dp),
                contentPadding = PaddingValues(horizontal = 6.dp, vertical = 0.dp)) {
                if (opening) CircularProgressIndicator(Modifier.size(16.dp), color = MaterialTheme.colorScheme.onPrimary, strokeWidth = 2.dp)
                else Text(stringResource(R.string.notifications_open), style = MaterialTheme.typography.bodySmall,
                    fontWeight = FontWeight.SemiBold, maxLines = 1)
            }
        }
    }
}

@Composable
private fun NotificationListSkeleton() {
    Column(Modifier.fillMaxSize().padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        repeat(4) {
            Card(shape = RoundedCornerShape(16.dp), colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface)) {
                Column(Modifier.fillMaxWidth().padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    repeat(2) { line ->
                        Box(Modifier.fillMaxWidth(if (line == 0) 0.55f else 0.85f).height(12.dp)
                            .background(MaterialTheme.colorScheme.outline.copy(alpha = 0.22f), RoundedCornerShape(4.dp)))
                    }
                    Spacer(Modifier.height(8.dp))
                }
            }
        }
    }
}
