package com.nags.operations.ui.notifications

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
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
import androidx.compose.material.icons.filled.GroupAdd
import androidx.compose.material.icons.filled.NotificationsActive
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.PrimaryTabRow
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Tab
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshotFlow
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.lerp
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.LifecycleResumeEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.nags.operations.R
import com.nags.operations.data.notifications.NotificationCategory
import com.nags.operations.data.notifications.NotificationDto
import com.nags.operations.data.notifications.NotificationOpenRequest
import com.nags.operations.ui.components.EmptyState
import com.nags.operations.ui.components.ErrorState
import com.nags.operations.ui.util.formatIsoForDisplay
import kotlinx.coroutines.flow.distinctUntilChanged

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
    LaunchedEffect(viewModel, state.category, state.items, state.visitId, state.isLoading, state.isRefreshing) {
        viewModel.acknowledgeLoadedCategory()
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(stringResource(R.string.notifications_title), fontWeight = FontWeight.Bold) },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = stringResource(R.string.notifications_back))
                    }
                },
                actions = {
                    IconButton(onClick = { confirmClear = true }, enabled = state.items.isNotEmpty()) {
                        Icon(Icons.Default.Archive, contentDescription = stringResource(R.string.notifications_clear_all))
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = MaterialTheme.colorScheme.surface,
                    titleContentColor = MaterialTheme.colorScheme.onSurface,
                    navigationIconContentColor = MaterialTheme.colorScheme.primary,
                    actionIconContentColor = MaterialTheme.colorScheme.primary,
                ),
            )
        },
        containerColor = MaterialTheme.colorScheme.background,
    ) { padding ->
        Column(Modifier.fillMaxSize().padding(padding)) {
            PrimaryTabRow(selectedTabIndex = NotificationCategory.entries.indexOf(state.category),
                containerColor = MaterialTheme.colorScheme.surface) {
                NotificationCategory.entries.forEach { category ->
                    Tab(selected = state.category == category, onClick = { viewModel.setCategory(category) },
                        text = {
                            Text(stringResource(category.labelResource()),
                                style = MaterialTheme.typography.bodyMedium.copy(fontSize = 13.sp),
                                fontWeight = FontWeight.Bold, maxLines = 1, softWrap = false,
                                overflow = TextOverflow.Ellipsis, textAlign = TextAlign.Center)
                        })
                }
            }
            key(state.category) {
                val listState = rememberLazyListState()
                NotificationList(state, viewModel, listState,
                    onOpen = { viewModel.open(it, onOpenFlight, onOpenSchedule) })
            }
        }
    }
    if (confirmClear) {
        AlertDialog(
            onDismissRequest = { confirmClear = false },
            title = { Text(stringResource(R.string.notifications_clear_confirm_title)) },
            text = { Text(stringResource(R.string.notifications_clear_confirm_body)) },
            confirmButton = {
                TextButton(onClick = { confirmClear = false; viewModel.archiveAll() }) {
                    Text(stringResource(R.string.notifications_clear_confirm_action))
                }
            },
            dismissButton = {
                TextButton(onClick = { confirmClear = false }) { Text(stringResource(R.string.notifications_cancel)) }
            },
        )
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
    val currentState by rememberUpdatedState(state)
    LaunchedEffect(viewModel, listState) {
        snapshotFlow {
            nextNotificationPageToLoad(currentState, listState.layoutInfo.visibleItemsInfo.lastOrNull()?.index ?: -1)
        }.distinctUntilChanged().collect { page -> if (page != null) viewModel.loadMore() }
    }
    Column(Modifier.fillMaxSize()) {
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
                    EmptyState(icon = Icons.Default.DoneAll, title = stringResource(state.category.emptyTitleResource()),
                        message = stringResource(state.category.emptyBodyResource()))
                }
                else -> LazyColumn(state = listState,
                    contentPadding = PaddingValues(start = 16.dp, top = 16.dp, end = 16.dp, bottom = 24.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxSize()) {
                    state.error?.let { message ->
                        item(key = "refresh-error") {
                            Text(message, color = MaterialTheme.colorScheme.error)
                            TextButton(onClick = { viewModel.refresh(userInitiated = true) }) {
                                Text(stringResource(R.string.notifications_retry))
                            }
                        }
                    }
                    items(state.items, key = { it.id }, contentType = { "notification" }) { notification ->
                        NotificationCard(notification, state.category, notification.id in state.newIds,
                            onClick = { onOpen(notification) }, onArchive = { viewModel.archive(notification) })
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

private fun NotificationCategory.labelResource(): Int = when (this) {
    NotificationCategory.Invitations -> R.string.notifications_tab_invitations
    NotificationCategory.Reminders -> R.string.notifications_tab_reminders
    NotificationCategory.FlightUpdates -> R.string.notifications_tab_flight_updates
}

private fun NotificationCategory.emptyTitleResource(): Int = when (this) {
    NotificationCategory.Invitations -> R.string.notifications_invitations_empty_title
    NotificationCategory.Reminders -> R.string.notifications_reminders_empty_title
    NotificationCategory.FlightUpdates -> R.string.notifications_flight_updates_empty_title
}

private fun NotificationCategory.emptyBodyResource(): Int = when (this) {
    NotificationCategory.Invitations -> R.string.notifications_invitations_empty_body
    NotificationCategory.Reminders -> R.string.notifications_reminders_empty_body
    NotificationCategory.FlightUpdates -> R.string.notifications_flight_updates_empty_body
}

@Composable
private fun NotificationCard(notification: NotificationDto, category: NotificationCategory, isNew: Boolean,
    onClick: () -> Unit, onArchive: () -> Unit) {
    val arabic = LocalConfiguration.current.locales[0].language.equals("ar", ignoreCase = true)
    val surface = MaterialTheme.colorScheme.surface
    Card(onClick = onClick, shape = RoundedCornerShape(16.dp), modifier = Modifier.fillMaxWidth(),
        colors = CardDefaults.cardColors(containerColor = if (isNew) lerp(surface, MaterialTheme.colorScheme.primary, 0.045f) else surface),
        border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline.copy(alpha = 0.32f))) {
        Row(Modifier.fillMaxWidth().padding(start = 14.dp, end = 8.dp, top = 12.dp, bottom = 8.dp),
            horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.Top) {
            Box(Modifier.size(36.dp).background(MaterialTheme.colorScheme.primaryContainer.copy(alpha = 0.45f),
                RoundedCornerShape(10.dp)), contentAlignment = Alignment.Center) {
                Icon(when (category) {
                    NotificationCategory.Invitations -> Icons.Default.GroupAdd
                    NotificationCategory.Reminders -> Icons.Default.NotificationsActive
                    NotificationCategory.FlightUpdates -> Icons.Default.FlightTakeoff
                }, contentDescription = null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(20.dp))
            }
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(5.dp)) {
                Text(if (arabic) notification.titleAr else notification.titleEn,
                    style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold,
                    maxLines = 2, overflow = TextOverflow.Ellipsis)
                Text(if (arabic) notification.bodyAr else notification.bodyEn,
                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 3, overflow = TextOverflow.Ellipsis)
                Row(verticalAlignment = Alignment.CenterVertically) {
                    notification.payload["flightNumber"]?.takeIf(String::isNotBlank)?.let { number ->
                        Text(number, style = MaterialTheme.typography.labelSmall, fontWeight = FontWeight.SemiBold,
                            modifier = Modifier.padding(end = 8.dp))
                    }
                    Text(formatIsoForDisplay(notification.createdAtUtc), style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.weight(1f),
                        maxLines = 2, overflow = TextOverflow.Ellipsis)
                    IconButton(onClick = onArchive, modifier = Modifier.size(48.dp)) {
                        Icon(Icons.Default.Archive, contentDescription = stringResource(R.string.notifications_archive),
                            modifier = Modifier.size(18.dp))
                    }
                }
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
