package com.nags.operations.ui.sync

import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
import androidx.compose.material3.HorizontalDivider
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.material.icons.filled.ExpandMore
import androidx.compose.material.icons.filled.ExpandLess
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.semantics.Role
import com.nags.operations.ui.account.ProfilePageHeader
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Bolt
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.CloudOff
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Sync
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.TextButton
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.draw.clip
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.nags.operations.BuildConfig
import com.nags.operations.R
import com.nags.operations.data.SharedCatalog
import com.nags.operations.data.sync.SyncTarget
import com.nags.operations.data.realtime.RealtimeChannelState
import com.nags.operations.ui.components.InlineNotice
import com.nags.operations.ui.components.InlineNoticeKind
import java.text.SimpleDateFormat
import java.util.Date

/** The established Sync Center layout for basic flight and shared catalog cache diagnostics. */
@Composable
fun SyncCenterScreen(
    viewModel: SyncCenterViewModel,
    onBack: () -> Unit,
    onOpenAdHoc: (String) -> Unit = {},
    onReviewFlight: (String, String) -> Unit = { _, _ -> },
) {
    val pendingAdHoc by viewModel.pendingAdHoc.collectAsStateWithLifecycle()
    val adHocActionError by viewModel.adHocActionError.collectAsStateWithLifecycle()
    var discardAdHocId by rememberSaveable { mutableStateOf<String?>(null) }
    if (discardAdHocId != null) AlertDialog(onDismissRequest = { discardAdHocId = null },
        title = { Text(stringResource(R.string.sync_hub_discard_adhoc)) },
        text = { Text(stringResource(if (pendingAdHoc.firstOrNull { it.flightId == discardAdHocId }?.materializedDetailJson != null)
            R.string.sync_hub_discard_planning_message else R.string.sync_hub_discard_new_flight_message)) },
        confirmButton = { TextButton(onClick = { discardAdHocId?.let(viewModel::discardAdHoc); discardAdHocId = null }) { Text(stringResource(R.string.flight_form_discard)) } },
        dismissButton = { TextButton(onClick = { discardAdHocId = null }) { Text(stringResource(R.string.flights_cancel)) } })
    val pendingArrivals by viewModel.pendingArrivals.collectAsStateWithLifecycle()
    val arrivalActionError by viewModel.arrivalActionError.collectAsStateWithLifecycle()
    val pendingCancellations by viewModel.pendingCancellations.collectAsStateWithLifecycle()
    val cancellationActionFailed by viewModel.cancellationActionFailed.collectAsStateWithLifecycle()
    var discardCancellationId by rememberSaveable { mutableStateOf<String?>(null) }
    if (discardCancellationId != null) AlertDialog(onDismissRequest = { discardCancellationId = null },
        title = { Text(stringResource(R.string.cancellation_sync_discard)) },
        text = { Text(stringResource(R.string.cancellation_sync_discard_message)) },
        confirmButton = { TextButton(onClick = { discardCancellationId?.let(viewModel::discardCancellation); discardCancellationId = null }) {
            Text(stringResource(R.string.cancellation_sync_discard))
        } }, dismissButton = { TextButton(onClick = { discardCancellationId = null }) { Text(stringResource(R.string.flights_cancel)) } })
    val pendingServices by viewModel.pendingServices.collectAsStateWithLifecycle()
    val serviceActionFailed by viewModel.serviceActionFailed.collectAsStateWithLifecycle()
    var discardServiceFlightId by rememberSaveable { mutableStateOf<String?>(null) }
    var discardServiceRecordId by rememberSaveable { mutableStateOf<String?>(null) }
    if (discardServiceFlightId != null && discardServiceRecordId != null) AlertDialog(
        onDismissRequest = { discardServiceFlightId = null; discardServiceRecordId = null },
        title = { Text(stringResource(R.string.service_sync_discard)) },
        text = { Text(stringResource(R.string.service_sync_discard_message)) },
        confirmButton = { TextButton(onClick = {
            viewModel.discardService(checkNotNull(discardServiceFlightId), checkNotNull(discardServiceRecordId))
            discardServiceFlightId = null; discardServiceRecordId = null
        }) { Text(stringResource(R.string.service_sync_discard)) } },
        dismissButton = { TextButton(onClick = { discardServiceFlightId = null; discardServiceRecordId = null }) {
            Text(stringResource(R.string.flights_cancel))
        } })
    val pendingTasks by viewModel.pendingTasks.collectAsStateWithLifecycle()
    val taskActionFailed by viewModel.taskActionFailed.collectAsStateWithLifecycle()
    var discardTaskFlightId by rememberSaveable { mutableStateOf<String?>(null) }
    var discardTaskRecordId by rememberSaveable { mutableStateOf<String?>(null) }
    if (discardTaskFlightId != null && discardTaskRecordId != null) AlertDialog(
        onDismissRequest = { discardTaskFlightId = null; discardTaskRecordId = null },
        title = { Text(stringResource(R.string.task_sync_discard)) },
        text = { Text(stringResource(R.string.task_sync_discard_message)) },
        confirmButton = { TextButton(onClick = {
            viewModel.discardTask(checkNotNull(discardTaskFlightId), checkNotNull(discardTaskRecordId))
            discardTaskFlightId = null; discardTaskRecordId = null
        }) { Text(stringResource(R.string.task_sync_discard)) } },
        dismissButton = { TextButton(onClick = { discardTaskFlightId = null; discardTaskRecordId = null }) {
            Text(stringResource(R.string.flights_cancel))
        } })
    val pendingDepartures by viewModel.pendingDepartures.collectAsStateWithLifecycle()
    val departureActionFailed by viewModel.departureActionFailed.collectAsStateWithLifecycle()
    var discardDepartureId by rememberSaveable { mutableStateOf<String?>(null) }
    if (discardDepartureId != null) AlertDialog(onDismissRequest = { discardDepartureId = null },
        title = { Text(stringResource(R.string.departure_sync_discard)) },
        text = { Text(stringResource(R.string.departure_sync_discard_message)) },
        confirmButton = { TextButton(onClick = { discardDepartureId?.let(viewModel::discardDeparture); discardDepartureId = null }) {
            Text(stringResource(R.string.departure_sync_discard))
        } },
        dismissButton = { TextButton(onClick = { discardDepartureId = null }) { Text(stringResource(R.string.flights_cancel)) } })
    var discardArrivalId by rememberSaveable { mutableStateOf<String?>(null) }
    if (discardArrivalId != null) AlertDialog(onDismissRequest = { discardArrivalId = null },
        title = { Text(stringResource(R.string.arrival_sync_discard)) },
        text = { Text(stringResource(R.string.arrival_sync_discard_message)) },
        confirmButton = { TextButton(onClick = { discardArrivalId?.let(viewModel::discardArrival); discardArrivalId = null }) {
            Text(stringResource(R.string.arrival_sync_discard))
        } },
        dismissButton = { TextButton(onClick = { discardArrivalId = null }) { Text(stringResource(R.string.flights_cancel)) } })
    val rows by viewModel.rows.collectAsStateWithLifecycle()
    val offlineSearch by viewModel.offlineSearchStatus.collectAsStateWithLifecycle()
    val isSyncing by viewModel.isSyncing.collectAsStateWithLifecycle()
    val isOnline by viewModel.isOnline.collectAsStateWithLifecycle()
    val realtimeState by viewModel.realtimeState.collectAsStateWithLifecycle()
    val lastRealtimeEventAt by viewModel.lastRealtimeEventAt.collectAsStateWithLifecycle()
    val now by viewModel.nowTick.collectAsStateWithLifecycle()

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.background),
    ) {
        ProfilePageHeader(stringResource(R.string.profile_sync_hub), onBack) {
            ConnectivityPill(isOnline)
        }

        LazyColumn(
            modifier = Modifier
                .fillMaxSize()
                .navigationBarsPadding(),
            contentPadding = PaddingValues(horizontal = 16.dp, vertical = 16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            item(key = "sync-overview") {
                val pendingStatuses = pendingAdHoc.map { it.status } + pendingArrivals.map { it.status } +
                    pendingServices.map { it.status } + pendingTasks.map { it.status } +
                    pendingDepartures.map { it.status } + pendingCancellations.map { it.status }
                SyncOverview(rows, offlineSearch, pendingStatuses, isSyncing, isOnline, viewModel::refreshNow)
            }
            item(key = "connection") { ConnectionDetails(isOnline, realtimeState, lastRealtimeEventAt, now) }
            if (pendingAdHoc.isNotEmpty()) {
                item { SyncSectionTitle(stringResource(R.string.sync_hub_adhoc)) }
                items(pendingAdHoc, key = { "adhoc:${it.flightId}" }) { row ->
                    Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(MaterialTheme.colorScheme.surface)
                        .padding(14.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        val input = androidx.compose.runtime.remember(row.inputJson) {
                            com.nags.operations.data.api.HttpClientFactory.json.decodeFromString<com.nags.operations.data.AdHocFlightInput>(row.inputJson)
                        }
                        Text(input.flightNumber, fontWeight = FontWeight.SemiBold, style = MaterialTheme.typography.bodyMedium)
                        InlineNotice(row.error ?: stringResource(R.string.adhoc_pending_details),
                            kind = if (row.status == "pending") InlineNoticeKind.Info else InlineNoticeKind.Warning)
                        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            TextButton(onClick = { onOpenAdHoc(row.flightId) }) { Text(stringResource(R.string.flight_review_action)) }
                            TextButton(onClick = { viewModel.retryAdHoc(row.flightId) }, enabled = isOnline) { Text(stringResource(R.string.arrival_sync_retry)) }
                            TextButton(onClick = { discardAdHocId = row.flightId }, enabled = row.attemptInputJson == null || row.attemptRejected) { Text(stringResource(R.string.flight_form_discard)) }
                        }
                    }
                }
            }
            adHocActionError?.let { message -> item { InlineNotice(message, kind = InlineNoticeKind.Error) } }
            if (pendingArrivals.isNotEmpty()) {
                item { SyncSectionTitle(stringResource(R.string.arrival_sync_title)) }
                items(pendingArrivals, key = { "arrival:${it.flightId}" }) { row ->
                    Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(MaterialTheme.colorScheme.surface)
                        .padding(14.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text(stringResource(R.string.arrival_sync_item, row.flightNumber), fontWeight = FontWeight.SemiBold, style = MaterialTheme.typography.bodyMedium)
                        InlineNotice(
                            message = row.error ?: stringResource(if (row.status == "pending") R.string.arrival_pending else R.string.arrival_sync_review),
                            kind = when {
                                row.error != null -> InlineNoticeKind.Error
                                row.status == "pending" -> InlineNoticeKind.Info
                                else -> InlineNoticeKind.Warning
                            },
                        )
                        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            if (syncNeedsReview(row.status)) TextButton(onClick = { onReviewFlight(row.flightId, "arrival") }) {
                                Text(stringResource(R.string.flight_review_action), color = MaterialTheme.colorScheme.error)
                            }
                            TextButton(onClick = { viewModel.retryArrival(row.flightId) }, enabled = isOnline) {
                                Text(stringResource(R.string.arrival_sync_retry))
                            }
                            TextButton(onClick = { discardArrivalId = row.flightId }) {
                                Text(stringResource(R.string.arrival_sync_discard))
                            }
                        }
                    }
                }
            }
            arrivalActionError?.let { message -> item { InlineNotice(message, kind = InlineNoticeKind.Error) } }
            if (pendingServices.isNotEmpty()) {
                item { SyncSectionTitle(stringResource(R.string.service_sync_title)) }
                items(pendingServices, key = { "service:${it.flightId}:${it.serviceRecordId}" }) { row ->
                    Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(MaterialTheme.colorScheme.surface)
                        .padding(14.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text(stringResource(R.string.arrival_sync_item, row.flightNumber), fontWeight = FontWeight.SemiBold, style = MaterialTheme.typography.bodyMedium)
                        InlineNotice(
                            message = row.error ?: stringResource(if (row.status == "pending") R.string.service_pending else R.string.service_sync_review),
                            kind = when {
                                row.error != null -> InlineNoticeKind.Error
                                row.status == "pending" -> InlineNoticeKind.Info
                                else -> InlineNoticeKind.Warning
                            },
                        )
                        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            if (syncNeedsReview(row.status)) TextButton(onClick = { onReviewFlight(row.flightId, "service:${row.serviceRecordId}") }) {
                                Text(stringResource(R.string.flight_review_action), color = MaterialTheme.colorScheme.error)
                            }
                            TextButton(onClick = { viewModel.retryService(row.flightId, row.serviceRecordId) }, enabled = isOnline) {
                                Text(stringResource(R.string.arrival_sync_retry))
                            }
                            TextButton(onClick = { discardServiceFlightId = row.flightId; discardServiceRecordId = row.serviceRecordId },
                                enabled = row.attemptOperation == null || row.status != "pending") {
                                Text(stringResource(R.string.service_sync_discard))
                            }
                        }
                    }
                }
            }
            if (serviceActionFailed) item { InlineNotice(stringResource(R.string.service_sync_action_failed), kind = InlineNoticeKind.Error) }
            if (pendingTasks.isNotEmpty()) {
                item { SyncSectionTitle(stringResource(R.string.task_sync_title)) }
                items(pendingTasks, key = { "task:${it.flightId}:${it.taskRecordId}" }) { row ->
                    Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(MaterialTheme.colorScheme.surface)
                        .padding(14.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text(stringResource(R.string.arrival_sync_item, row.flightNumber), fontWeight = FontWeight.SemiBold, style = MaterialTheme.typography.bodyMedium)
                        InlineNotice(
                            message = row.error ?: stringResource(if (row.status == "pending") R.string.task_pending else R.string.task_sync_review),
                            kind = when {
                                row.error != null -> InlineNoticeKind.Error
                                row.status == "pending" -> InlineNoticeKind.Info
                                else -> InlineNoticeKind.Warning
                            },
                        )
                        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            if (syncNeedsReview(row.status)) TextButton(onClick = { onReviewFlight(row.flightId, "task:${row.taskRecordId}") }) {
                                Text(stringResource(R.string.flight_review_action), color = MaterialTheme.colorScheme.error)
                            }
                            TextButton(onClick = { viewModel.retryTask(row.flightId, row.taskRecordId) }, enabled = isOnline) {
                                Text(stringResource(R.string.arrival_sync_retry))
                            }
                            TextButton(onClick = { discardTaskFlightId = row.flightId; discardTaskRecordId = row.taskRecordId },
                                enabled = row.attemptOperation == null || row.status != "pending") {
                                Text(stringResource(R.string.task_sync_discard))
                            }
                        }
                    }
                }
            }
            if (taskActionFailed) item { InlineNotice(stringResource(R.string.task_sync_action_failed), kind = InlineNoticeKind.Error) }
            if (pendingDepartures.isNotEmpty()) {
                item { SyncSectionTitle(stringResource(R.string.departure_sync_title)) }
                items(pendingDepartures, key = { "departure:${it.flightId}" }) { row ->
                    Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(MaterialTheme.colorScheme.surface)
                        .padding(14.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text(stringResource(R.string.departure_sync_item, row.flightNumber), fontWeight = FontWeight.SemiBold, style = MaterialTheme.typography.bodyMedium)
                        InlineNotice(
                            message = row.error ?: stringResource(if (row.status == "pending") R.string.departure_pending else R.string.departure_sync_review),
                            kind = when {
                                row.error != null -> InlineNoticeKind.Error
                                row.status == "pending" -> InlineNoticeKind.Info
                                else -> InlineNoticeKind.Warning
                            },
                        )
                        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            if (syncNeedsReview(row.status)) TextButton(onClick = { onReviewFlight(row.flightId, "departure") }) {
                                Text(stringResource(R.string.flight_review_action), color = MaterialTheme.colorScheme.error)
                            }
                            TextButton(onClick = { viewModel.retryDeparture(row.flightId) }, enabled = isOnline) {
                                Text(stringResource(R.string.departure_sync_retry))
                            }
                            TextButton(onClick = { discardDepartureId = row.flightId }) {
                                Text(stringResource(R.string.departure_sync_discard))
                            }
                        }
                    }
                }
            }
            if (departureActionFailed) item { InlineNotice(stringResource(R.string.departure_sync_action_failed), kind = InlineNoticeKind.Error) }
            if (pendingCancellations.isNotEmpty()) {
                item { SyncSectionTitle(stringResource(R.string.cancellation_sync_title)) }
                items(pendingCancellations, key = { "cancellation:${it.flightId}" }) { row ->
                    Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(MaterialTheme.colorScheme.surface)
                        .padding(14.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text(stringResource(R.string.arrival_sync_item, row.flightNumber), fontWeight = FontWeight.SemiBold, style = MaterialTheme.typography.bodyMedium)
                        InlineNotice(
                            message = row.error ?: stringResource(when {
                                row.status != "pending" -> R.string.cancellation_sync_review
                                row.operation == "revert" -> R.string.cancellation_revert_pending_message
                                else -> R.string.cancellation_pending_message
                            }),
                            kind = when {
                                row.error != null -> InlineNoticeKind.Error
                                row.status == "pending" -> InlineNoticeKind.Info
                                else -> InlineNoticeKind.Warning
                            },
                        )
                        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            if (syncNeedsReview(row.status)) TextButton(onClick = { onReviewFlight(row.flightId, "cancellation") }) {
                                Text(stringResource(R.string.flight_review_action), color = MaterialTheme.colorScheme.error)
                            }
                            TextButton(onClick = { viewModel.retryCancellation(row.flightId) }, enabled = isOnline) { Text(stringResource(R.string.arrival_sync_retry)) }
                            TextButton(onClick = { discardCancellationId = row.flightId }, enabled = row.attemptOperation == null || row.status != "pending") {
                                Text(stringResource(R.string.cancellation_sync_discard))
                            }
                        }
                    }
                }
            }
            if (cancellationActionFailed) item { InlineNotice(stringResource(R.string.cancellation_sync_action_failed), kind = InlineNoticeKind.Error) }
            item(key = "offline-heading") { SyncSectionTitle(stringResource(R.string.sync_hub_offline_data)) }
            item(key = "other-station-flights") {
                Column(Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 2.dp),
                    verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Text(stringResource(R.string.sync_hub_other_flights), style = MaterialTheme.typography.bodyMedium,
                        fontWeight = FontWeight.SemiBold)
                    Text(when {
                        offlineSearch.permissionDenied -> stringResource(R.string.catalog_sync_denied)
                        offlineSearch.lastSuccessUtc == null -> stringResource(R.string.sync_hub_search_prepare)
                        else -> pluralStringResource(R.plurals.sync_hub_searchable, offlineSearch.itemCount, offlineSearch.itemCount)
                    }, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
            items(rows, key = { it.target.storageKey }) { row ->
                SyncRowCard(row)
            }
            item(key = "app-version") {
                AppVersionFooter(
                    versionName = BuildConfig.VERSION_NAME,
                    versionCode = BuildConfig.VERSION_CODE,
                )
            }
        }
    }

}

@Composable
private fun SyncSectionTitle(title: String) {
    Text(title, Modifier.fillMaxWidth().padding(start = 12.dp, top = 8.dp, bottom = 2.dp),
        style = MaterialTheme.typography.bodySmall, fontWeight = FontWeight.SemiBold,
        color = MaterialTheme.colorScheme.onSurfaceVariant)
}

@Composable
private fun SyncOverview(rows: List<SyncRowState>, offlineSearch: com.nags.operations.data.FlightSyncStatus,
    pendingStatuses: List<String>, syncing: Boolean,
    online: Boolean, onRefresh: () -> Unit) {
    val failed = rows.any { it.lastError != null || it.permissionDenied }
    val hasSavedData = rows.any { it.lastSyncedAt != null }
    val prepared = offlineDataPrepared(rows, offlineSearch)
    val reviewCount = pendingStatuses.count(::syncNeedsReview)
    val waitingCount = pendingStatuses.size - reviewCount
    Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(MaterialTheme.colorScheme.surface)
        .padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text(stringResource(when {
            syncing -> R.string.sync_hub_syncing
            failed -> R.string.sync_hub_attention
            prepared -> R.string.sync_hub_prepared
            hasSavedData -> R.string.sync_hub_saved_available
            else -> R.string.sync_hub_prepare
        }), style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
        Text(stringResource(if (online) R.string.sync_hub_overview_online else R.string.sync_hub_overview_offline),
            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        if (pendingStatuses.isNotEmpty()) FlowRow(horizontalArrangement = Arrangement.spacedBy(16.dp),
            verticalArrangement = Arrangement.spacedBy(6.dp)) {
            if (waitingCount > 0) SummaryCount(Icons.Default.Sync,
                pluralStringResource(R.plurals.sync_hub_waiting, waitingCount, waitingCount), false)
            if (reviewCount > 0) SummaryCount(Icons.Default.ErrorOutline,
                pluralStringResource(R.plurals.sync_hub_review, reviewCount, reviewCount), true)
        } else Text(stringResource(R.string.sync_hub_no_saved_work), style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant)
        RefreshNowButton(syncing, online, onRefresh)
    }
}

@Composable
private fun SummaryCount(icon: androidx.compose.ui.graphics.vector.ImageVector, text: String, error: Boolean) {
    val color = if (error) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        Icon(icon, null, Modifier.size(16.dp), tint = color)
        Text(text, style = MaterialTheme.typography.bodySmall, color = color, fontWeight = FontWeight.SemiBold)
    }
}

@Composable
private fun ConnectionDetails(online: Boolean, state: RealtimeChannelState, lastEventAt: Long?, now: Long) {
    var expanded by rememberSaveable { mutableStateOf(false) }
    val label = stringResource(when (state) {
        is RealtimeChannelState.Connected -> R.string.sync_center_connected
        is RealtimeChannelState.Connecting -> R.string.sync_center_connecting
        is RealtimeChannelState.Reconnecting -> R.string.sync_center_reconnecting
        is RealtimeChannelState.Disconnected -> R.string.sync_center_disconnected
    })
    Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(MaterialTheme.colorScheme.surface)) {
        Row(Modifier.fillMaxWidth().heightIn(min = 52.dp).clickable(role = Role.Button) { expanded = !expanded }
            .padding(horizontal = 14.dp, vertical = 10.dp), verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Icon(Icons.Default.Bolt, null, Modifier.size(18.dp), tint = MaterialTheme.colorScheme.onSurfaceVariant)
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text(stringResource(R.string.sync_hub_live_updates), style = MaterialTheme.typography.bodyMedium,
                    fontWeight = FontWeight.SemiBold)
                Text(label, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            Icon(if (expanded) Icons.Default.ExpandLess else Icons.Default.ExpandMore,
                stringResource(if (expanded) R.string.sync_hub_collapse else R.string.sync_hub_expand), Modifier.size(18.dp))
        }
        if (expanded) Column(Modifier.padding(start = 40.dp, end = 14.dp, bottom = 14.dp),
            verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Text(stringResource(R.string.sync_hub_last_live_update, formatLastEvent(lastEventAt, now)),
                style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            Text(stringResource(if (online) R.string.sync_hub_live_fallback else R.string.sync_hub_offline_waiting),
                style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

@Composable
private fun ConnectivityPill(isOnline: Boolean) {
    val foreground = if (isOnline) MaterialTheme.colorScheme.tertiary else MaterialTheme.colorScheme.onSurfaceVariant
    Text(stringResource(if (isOnline) R.string.sync_center_online else R.string.sync_center_offline),
        Modifier.padding(end = 8.dp).background(foreground.copy(alpha = 0.1f), RoundedCornerShape(8.dp))
            .padding(horizontal = 10.dp, vertical = 6.dp),
        style = MaterialTheme.typography.bodySmall, fontWeight = FontWeight.SemiBold, color = foreground)
}

@Composable
private fun RefreshNowButton(isSyncing: Boolean, isOnline: Boolean, onClick: () -> Unit) {
    Button(onClick, enabled = !isSyncing && isOnline, modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp),
        shape = RoundedCornerShape(12.dp)) {
        if (isSyncing) CircularProgressIndicator(Modifier.size(18.dp), color = MaterialTheme.colorScheme.onPrimary, strokeWidth = 2.dp)
        else Icon(Icons.Default.Refresh, null, Modifier.size(18.dp))
        Text(stringResource(when {
            isSyncing -> R.string.sync_center_refreshing
            isOnline -> R.string.sync_hub_sync_now
            else -> R.string.sync_center_refresh_offline
        }), Modifier.padding(start = 8.dp), fontWeight = FontWeight.SemiBold)
    }
}

@Composable
private fun SyncRowCard(row: SyncRowState) {
    var expanded by rememberSaveable(row.target.storageKey) { mutableStateOf(row.lastError != null || row.permissionDenied) }
    LaunchedEffect(row.lastError, row.permissionDenied) { if (row.lastError != null || row.permissionDenied) expanded = true }
    val error = row.lastError != null || row.permissionDenied
    Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(MaterialTheme.colorScheme.surface)) {
        Row(Modifier.fillMaxWidth().heightIn(min = 58.dp).clickable(role = Role.Button) { expanded = !expanded }
            .padding(horizontal = 14.dp, vertical = 10.dp), verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Icon(when {
                error -> Icons.Default.ErrorOutline
                row.lastSyncedAt != null -> Icons.Default.CheckCircle
                else -> Icons.Default.CloudOff
            }, null, Modifier.size(18.dp), tint = if (error) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant)
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                Text(stringResource(targetName(row.target)), style = MaterialTheme.typography.bodyMedium,
                    fontWeight = FontWeight.SemiBold)
                Text(when {
                    error -> stringResource(R.string.sync_hub_attention)
                    row.lastSyncedAt == null -> stringResource(R.string.sync_center_never)
                    else -> stringResource(R.string.sync_hub_updated, formatClock(row.lastSyncedAt))
                }, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            Text(row.rowCount.toString(), style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.SemiBold,
                color = MaterialTheme.colorScheme.onSurfaceVariant)
            Icon(if (expanded) Icons.Default.ExpandLess else Icons.Default.ExpandMore,
                stringResource(if (expanded) R.string.sync_hub_collapse else R.string.sync_hub_expand), Modifier.size(18.dp))
        }
        if (expanded) Column(Modifier.padding(start = 14.dp, end = 14.dp, bottom = 14.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp)) {
            HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.5f))
            Text(pluralStringResource(R.plurals.sync_center_rows_cached, row.rowCount, row.rowCount),
                style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            Text(stringResource(R.string.sync_hub_last_success, formatLastSync(row)), style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant)
            row.lastDurationMs?.let { Text(stringResource(R.string.sync_center_duration_ms, it),
                style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant) }
            if (error) InlineNotice(if (row.permissionDenied) stringResource(R.string.catalog_sync_denied)
                else row.lastError ?: stringResource(R.string.catalog_sync_failed), kind = InlineNoticeKind.Error)
        }
    }
}

@Composable
private fun AppVersionFooter(versionName: String, versionCode: Int) {
    Text(stringResource(R.string.sync_center_version, versionName, versionCode),
        Modifier.fillMaxWidth().padding(vertical = 12.dp), style = MaterialTheme.typography.labelSmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant, textAlign = androidx.compose.ui.text.style.TextAlign.Center)
}

@Composable
private fun formatClock(timestamp: Long): String {
    val locale = LocalConfiguration.current.locales[0]
    return remember(timestamp, locale) { SimpleDateFormat("MMM d, HH:mm", locale).format(Date(timestamp)) }
}

@Composable
private fun formatLastSync(row: SyncRowState): String {
    val timestamp = row.lastSyncedAt ?: return stringResource(R.string.sync_center_never)
    return "${formatClock(timestamp)} · ${humanizeAge(row.ageMs ?: 0L)}"
}

@Composable
private fun formatLastEvent(lastEventAt: Long?, now: Long): String = if (lastEventAt == null)
    stringResource(R.string.sync_center_awaiting_event) else "${formatClock(lastEventAt)} · ${humanizeAge((now - lastEventAt).coerceAtLeast(0L))}"

@Composable
private fun humanizeAge(ms: Long): String = when {
    ms < 60_000 -> stringResource(R.string.sync_center_just_now)
    ms < 3_600_000 -> stringResource(R.string.sync_center_minutes_ago, ms / 60_000)
    ms < 86_400_000 -> stringResource(R.string.sync_center_hours_ago, ms / 3_600_000)
    else -> stringResource(R.string.sync_center_days_ago, ms / 86_400_000)
}

private fun targetName(target: SyncTarget): Int = when (target) {
    SyncTarget.Flights -> R.string.flights_title
    is SyncTarget.Catalog -> catalogName(target.catalog)
}

private fun catalogName(kind: SharedCatalog): Int = when (kind) {
    SharedCatalog.AircraftTypes -> R.string.catalog_aircraft_types
    SharedCatalog.Customers -> R.string.catalog_customers
    SharedCatalog.Services -> R.string.catalog_services
    SharedCatalog.Tools -> R.string.catalog_tools
    SharedCatalog.Materials -> R.string.catalog_materials
    SharedCatalog.GeneralSupports -> R.string.catalog_general_supports
    SharedCatalog.AtaChapters -> R.string.catalog_ata_chapters
    SharedCatalog.StationStaff -> R.string.catalog_station_staff
}

/** A creation dependency is waiting, rather than a rejected submission needing user correction. */
private fun syncNeedsReview(status: String): Boolean = status != "pending" && status != "waiting-creation"
