package com.nags.operations.ui.notifications

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Archive
import androidx.compose.material.icons.filled.DoneAll
import androidx.compose.material.icons.filled.FlightTakeoff
import androidx.compose.material.icons.filled.MoreVert
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.AssistChip
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.nags.operations.R
import com.nags.operations.data.notifications.NotificationDto
import com.nags.operations.data.notifications.NotificationOpenRequest
import com.nags.operations.ui.components.EmptyState
import com.nags.operations.ui.components.ErrorState
import com.nags.operations.ui.theme.BrandRed
import com.nags.operations.ui.util.formatIsoForDisplay
import java.util.Locale

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun NotificationsScreen(
    viewModel: NotificationsViewModel,
    onBack: () -> Unit,
    onOpenFlight: (NotificationOpenRequest) -> Unit,
    onOpenSchedule: (NotificationOpenRequest) -> Unit = {},
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    var menuExpanded by remember { mutableStateOf(false) }
    var confirmClear by remember { mutableStateOf(false) }

    Scaffold(
        topBar = {
            TopAppBar(
                title = {
                    Column {
                        Text(stringResource(R.string.notifications_title), fontWeight = FontWeight.Bold)
                        if (state.unreadCount > 0) {
                            Text(
                                pluralStringResource(
                                    R.plurals.notifications_unread_count,
                                    state.unreadCount,
                                    state.unreadCount,
                                ),
                                style = MaterialTheme.typography.labelMedium,
                            )
                        }
                    }
                },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = null)
                    }
                },
                actions = {
                    Box {
                        IconButton(onClick = { menuExpanded = true }) {
                            Icon(Icons.Default.MoreVert, contentDescription = null)
                        }
                        DropdownMenu(expanded = menuExpanded, onDismissRequest = { menuExpanded = false }) {
                            DropdownMenuItem(
                                text = { Text(stringResource(R.string.notifications_mark_all_read)) },
                                leadingIcon = { Icon(Icons.Default.DoneAll, contentDescription = null) },
                                enabled = state.unreadCount > 0,
                                onClick = {
                                    menuExpanded = false
                                    viewModel.markAllRead()
                                },
                            )
                            DropdownMenuItem(
                                text = { Text(stringResource(R.string.notifications_clear_all)) },
                                leadingIcon = { Icon(Icons.Default.Archive, contentDescription = null) },
                                enabled = state.items.isNotEmpty(),
                                onClick = {
                                    menuExpanded = false
                                    confirmClear = true
                                },
                            )
                        }
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = BrandRed,
                    titleContentColor = Color.White,
                    navigationIconContentColor = Color.White,
                    actionIconContentColor = Color.White,
                ),
            )
        },
    ) { padding ->
        Column(modifier = Modifier.fillMaxSize().padding(padding)) {
            Row(
                horizontalArrangement = Arrangement.spacedBy(8.dp),
                modifier = Modifier.padding(horizontal = 16.dp, vertical = 12.dp),
            ) {
                FilterChip(
                    selected = !state.unreadOnly,
                    onClick = { viewModel.setUnreadOnly(false) },
                    label = { Text(stringResource(R.string.notifications_all)) },
                )
                FilterChip(
                    selected = state.unreadOnly,
                    onClick = { viewModel.setUnreadOnly(true) },
                    label = { Text(stringResource(R.string.notifications_unread)) },
                )
            }

            PullToRefreshBox(
                isRefreshing = state.isRefreshing,
                onRefresh = { viewModel.refresh(userInitiated = true) },
                modifier = Modifier.fillMaxSize(),
            ) {
                when {
                    state.isLoading && state.items.isEmpty() -> Box(
                        Modifier.fillMaxSize(), contentAlignment = Alignment.Center,
                    ) { CircularProgressIndicator() }
                    state.error != null && state.items.isEmpty() -> Box(
                        Modifier.fillMaxSize(), contentAlignment = Alignment.Center,
                    ) {
                        ErrorState(
                            title = stringResource(R.string.notifications_load_error),
                            message = state.error!!,
                            onRetry = { viewModel.refresh(userInitiated = true) },
                        )
                    }
                    state.items.isEmpty() -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                        EmptyState(
                            icon = Icons.Default.DoneAll,
                            title = stringResource(R.string.notifications_empty_title),
                            message = stringResource(
                                if (state.unreadOnly) R.string.notifications_empty_unread_body
                                else R.string.notifications_empty_body,
                            ),
                        )
                    }
                    else -> LazyColumn(
                        contentPadding = PaddingValues(horizontal = 16.dp, vertical = 4.dp),
                        verticalArrangement = Arrangement.spacedBy(10.dp),
                    ) {
                        items(state.items, key = { it.id }) { notification ->
                            NotificationCard(
                                notification = notification,
                                onClick = {
                                    viewModel.open(notification, onOpenFlight, onOpenSchedule)
                                },
                                onArchive = { viewModel.archive(notification) },
                            )
                        }
                        if (state.hasMore) {
                            item {
                                Button(
                                    onClick = viewModel::loadMore,
                                    enabled = !state.isLoadingMore,
                                    modifier = Modifier.fillMaxWidth().padding(vertical = 8.dp),
                                ) {
                                    if (state.isLoadingMore) {
                                        CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp)
                                        Spacer(Modifier.width(8.dp))
                                    }
                                    Text(stringResource(R.string.notifications_load_more))
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    if (confirmClear) {
        AlertDialog(
            onDismissRequest = { confirmClear = false },
            title = { Text(stringResource(R.string.notifications_clear_confirm_title)) },
            text = { Text(stringResource(R.string.notifications_clear_confirm_body)) },
            confirmButton = {
                TextButton(onClick = {
                    confirmClear = false
                    viewModel.archiveAll()
                }) { Text(stringResource(R.string.notifications_clear_confirm_action)) }
            },
            dismissButton = {
                TextButton(onClick = { confirmClear = false }) {
                    Text(stringResource(R.string.notifications_cancel))
                }
            },
        )
    }
}

@Composable
private fun NotificationCard(
    notification: NotificationDto,
    onClick: () -> Unit,
    onArchive: () -> Unit,
) {
    val arabic = Locale.getDefault().language.equals("ar", ignoreCase = true)
    val title = if (arabic) notification.titleAr else notification.titleEn
    val body = if (arabic) notification.bodyAr else notification.bodyEn
    Card(
        colors = CardDefaults.cardColors(
            containerColor = if (notification.isRead) MaterialTheme.colorScheme.surface
            else MaterialTheme.colorScheme.primaryContainer.copy(alpha = 0.34f),
        ),
        elevation = CardDefaults.cardElevation(defaultElevation = if (notification.isRead) 1.dp else 3.dp),
        modifier = Modifier.fillMaxWidth().clickable(onClick = onClick),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(14.dp),
            verticalAlignment = Alignment.Top,
        ) {
            Box(
                modifier = Modifier.size(44.dp).background(
                    MaterialTheme.colorScheme.primary.copy(alpha = 0.12f), CircleShape,
                ),
                contentAlignment = Alignment.Center,
            ) {
                Icon(Icons.Default.FlightTakeoff, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
            }
            Spacer(Modifier.width(12.dp))
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(
                        title,
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = if (notification.isRead) FontWeight.SemiBold else FontWeight.Bold,
                        maxLines = 2,
                        overflow = TextOverflow.Ellipsis,
                        modifier = Modifier.weight(1f),
                    )
                    if (!notification.isRead) {
                        Box(Modifier.padding(start = 8.dp).size(8.dp).background(BrandRed, CircleShape))
                    }
                }
                Text(
                    body,
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 3,
                    overflow = TextOverflow.Ellipsis,
                )
                Row(verticalAlignment = Alignment.CenterVertically) {
                    notification.payload["flightNumber"]?.let { number ->
                        AssistChip(onClick = onClick, label = { Text(number) })
                        Spacer(Modifier.width(8.dp))
                    }
                    Text(
                        formatIsoForDisplay(notification.createdAtUtc),
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.weight(1f),
                    )
                    IconButton(onClick = onArchive, modifier = Modifier.size(36.dp)) {
                        Icon(
                            Icons.Default.Archive,
                            contentDescription = stringResource(R.string.notifications_clear_all),
                            modifier = Modifier.size(18.dp),
                        )
                    }
                }
            }
        }
    }
}
