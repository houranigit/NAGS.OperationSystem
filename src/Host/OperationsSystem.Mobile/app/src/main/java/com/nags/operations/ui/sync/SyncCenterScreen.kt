package com.nags.operations.ui.sync

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Bolt
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.CloudOff
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material.icons.filled.PowerOff
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Sync
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.TextButton
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
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
import com.nags.operations.ui.theme.BrandRed
import com.nags.operations.ui.theme.BrandRedDark
import com.nags.operations.ui.theme.BrandRedLight
import java.text.SimpleDateFormat
import java.util.Date

/** The established Sync Center layout for basic flight and shared catalog cache diagnostics. */
@Composable
fun SyncCenterScreen(
    viewModel: SyncCenterViewModel,
    onBack: () -> Unit,
    onOpenAdHoc: (String) -> Unit = {},
) {
    val pendingAdHoc by viewModel.pendingAdHoc.collectAsStateWithLifecycle()
    val adHocActionError by viewModel.adHocActionError.collectAsStateWithLifecycle()
    var discardAdHocId by rememberSaveable { mutableStateOf<String?>(null) }
    if (discardAdHocId != null) AlertDialog(onDismissRequest = { discardAdHocId = null },
        title = { Text("Discard saved AdHoc input?") },
        text = { Text("Remove this device's retained Flight details and dependent operational input, including its local media?") },
        confirmButton = { TextButton(onClick = { discardAdHocId?.let(viewModel::discardAdHoc); discardAdHocId = null }) { Text("Discard") } },
        dismissButton = { TextButton(onClick = { discardAdHocId = null }) { Text("Keep") } })
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
        SyncCenterHeader(onBack = onBack, isOnline = isOnline)

        LazyColumn(
            modifier = Modifier
                .fillMaxSize()
                .navigationBarsPadding(),
            contentPadding = PaddingValues(horizontal = 20.dp, vertical = 16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            item {
                LiveChannelCard(
                    state = realtimeState,
                    lastEventAt = lastRealtimeEventAt,
                    now = now,
                )
            }
            item {
                RefreshNowButton(
                    isSyncing = isSyncing,
                    isOnline = isOnline,
                    onClick = { viewModel.refreshNow() },
                )
            }
            if (pendingAdHoc.isNotEmpty()) {
                item { Text("AdHoc flights", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold) }
                items(pendingAdHoc, key = { "adhoc:${it.flightId}" }) { row ->
                    Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp)).background(MaterialTheme.colorScheme.surface)
                        .padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        val input = androidx.compose.runtime.remember(row.inputJson) {
                            com.nags.operations.data.api.HttpClientFactory.json.decodeFromString<com.nags.operations.data.AdHocFlightInput>(row.inputJson)
                        }
                        Text(input.flightNumber, fontWeight = FontWeight.Bold)
                        InlineNotice(row.error ?: "Saved on this device. Waiting to synchronize Flight details.",
                            kind = if (row.status == "pending") InlineNoticeKind.Info else InlineNoticeKind.Warning)
                        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            TextButton(onClick = { onOpenAdHoc(row.flightId) }) { Text("Review") }
                            TextButton(onClick = { viewModel.retryAdHoc(row.flightId) }, enabled = isOnline) { Text("Retry") }
                            TextButton(onClick = { discardAdHocId = row.flightId }, enabled = row.attemptInputJson == null || row.attemptRejected) { Text("Discard") }
                        }
                    }
                }
            }
            adHocActionError?.let { message -> item { InlineNotice(message, kind = InlineNoticeKind.Error) } }
            if (pendingArrivals.isNotEmpty()) {
                item { Text(stringResource(R.string.arrival_sync_title), style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold) }
                items(pendingArrivals, key = { "arrival:${it.flightId}" }) { row ->
                    Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp)).background(MaterialTheme.colorScheme.surface)
                        .padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text(stringResource(R.string.arrival_sync_item, row.flightNumber), fontWeight = FontWeight.Bold)
                        InlineNotice(
                            message = row.error ?: stringResource(if (row.status == "pending") R.string.arrival_pending else R.string.arrival_sync_review),
                            kind = when {
                                row.error != null -> InlineNoticeKind.Error
                                row.status == "pending" -> InlineNoticeKind.Info
                                else -> InlineNoticeKind.Warning
                            },
                        )
                        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
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
                item { Text(stringResource(R.string.service_sync_title), style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold) }
                items(pendingServices, key = { "service:${it.flightId}:${it.serviceRecordId}" }) { row ->
                    Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp)).background(MaterialTheme.colorScheme.surface)
                        .padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text(stringResource(R.string.arrival_sync_item, row.flightNumber), fontWeight = FontWeight.Bold)
                        InlineNotice(
                            message = row.error ?: stringResource(if (row.status == "pending") R.string.service_pending else R.string.service_sync_review),
                            kind = when {
                                row.error != null -> InlineNoticeKind.Error
                                row.status == "pending" -> InlineNoticeKind.Info
                                else -> InlineNoticeKind.Warning
                            },
                        )
                        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
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
                item { Text(stringResource(R.string.task_sync_title), style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold) }
                items(pendingTasks, key = { "task:${it.flightId}:${it.taskRecordId}" }) { row ->
                    Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp)).background(MaterialTheme.colorScheme.surface)
                        .padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text(stringResource(R.string.arrival_sync_item, row.flightNumber), fontWeight = FontWeight.Bold)
                        InlineNotice(
                            message = row.error ?: stringResource(if (row.status == "pending") R.string.task_pending else R.string.task_sync_review),
                            kind = when {
                                row.error != null -> InlineNoticeKind.Error
                                row.status == "pending" -> InlineNoticeKind.Info
                                else -> InlineNoticeKind.Warning
                            },
                        )
                        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
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
                item { Text(stringResource(R.string.departure_sync_title), style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold) }
                items(pendingDepartures, key = { "departure:${it.flightId}" }) { row ->
                    Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp)).background(MaterialTheme.colorScheme.surface)
                        .padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text(stringResource(R.string.departure_sync_item, row.flightNumber), fontWeight = FontWeight.Bold)
                        InlineNotice(
                            message = row.error ?: stringResource(if (row.status == "pending") R.string.departure_pending else R.string.departure_sync_review),
                            kind = when {
                                row.error != null -> InlineNoticeKind.Error
                                row.status == "pending" -> InlineNoticeKind.Info
                                else -> InlineNoticeKind.Warning
                            },
                        )
                        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
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
                item { Text(stringResource(R.string.cancellation_sync_title), style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold) }
                items(pendingCancellations, key = { "cancellation:${it.flightId}" }) { row ->
                    Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp)).background(MaterialTheme.colorScheme.surface)
                        .padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text(stringResource(R.string.arrival_sync_item, row.flightNumber), fontWeight = FontWeight.Bold)
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
                            TextButton(onClick = { viewModel.retryCancellation(row.flightId) }, enabled = isOnline) { Text(stringResource(R.string.arrival_sync_retry)) }
                            TextButton(onClick = { discardCancellationId = row.flightId }, enabled = row.attemptOperation == null || row.status != "pending") {
                                Text(stringResource(R.string.cancellation_sync_discard))
                            }
                        }
                    }
                }
            }
            if (cancellationActionFailed) item { InlineNotice(stringResource(R.string.cancellation_sync_action_failed), kind = InlineNoticeKind.Error) }
            item {
                Text(
                    text = stringResource(R.string.sync_center_cached_data),
                    style = MaterialTheme.typography.titleLarge,
                    fontWeight = FontWeight.Bold,
                    color = MaterialTheme.colorScheme.onBackground,
                    modifier = Modifier.padding(top = 8.dp),
                )
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
private fun AppVersionFooter(
    versionName: String,
    versionCode: Int,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(top = 8.dp, bottom = 4.dp),
        horizontalArrangement = Arrangement.Center,
    ) {
        Row(
            modifier = Modifier
                .clip(RoundedCornerShape(50))
                .background(MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.65f))
                .padding(horizontal = 14.dp, vertical = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.Center,
        ) {
            Text(
                text = stringResource(R.string.sync_center_version, versionName, versionCode),
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

/**
 * Surfaces the SignalR push channel so operators can tell at a glance whether
 * they're actually receiving live updates. Three pieces of information:
 *
 *  • Coarse session state ([RealtimeChannelState]) as the headline word + dot.
 *  • Wall-clock and "Xs ago" for the most recent received catalog event — the proof
 *    that the channel isn't just _claiming_ to be connected.
 *  • A short subtitle calling out what the channel is for (so the surrounding
 *    rows are obviously the safety-net polling layer underneath).
 */
@Composable
private fun LiveChannelCard(
    state: RealtimeChannelState,
    lastEventAt: Long?,
    now: Long,
) {
    val (label, dotColor) = when (state) {
        is RealtimeChannelState.Connected -> stringResource(R.string.sync_center_connected) to Color(0xFF35C271)
        is RealtimeChannelState.Connecting -> stringResource(R.string.sync_center_connecting) to Color(0xFFE0A93B)
        is RealtimeChannelState.Reconnecting -> stringResource(R.string.sync_center_reconnecting) to Color(0xFFE0A93B)
        is RealtimeChannelState.Disconnected -> stringResource(R.string.sync_center_disconnected) to Color(0xFFC44848)
    }
    val icon = when (state) {
        is RealtimeChannelState.Connected -> Icons.Default.Bolt
        is RealtimeChannelState.Connecting,
        is RealtimeChannelState.Reconnecting -> Icons.Default.Sync
        is RealtimeChannelState.Disconnected -> Icons.Default.PowerOff
    }
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(18.dp))
            .background(MaterialTheme.colorScheme.surface)
            .border(
                1.dp,
                MaterialTheme.colorScheme.outline.copy(alpha = 0.3f),
                RoundedCornerShape(18.dp),
            )
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Box(
                modifier = Modifier
                    .size(40.dp)
                    .clip(CircleShape)
                    .background(dotColor.copy(alpha = 0.12f)),
                contentAlignment = Alignment.Center,
            ) {
                Icon(icon, contentDescription = null, tint = dotColor, modifier = Modifier.size(22.dp))
            }
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text(
                    text = stringResource(R.string.sync_center_live_channel),
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.SemiBold,
                    color = MaterialTheme.colorScheme.onSurface,
                )
                Text(
                    text = stringResource(R.string.sync_center_live_hint),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            // Pill mirrors the connectivity pill in the header so the two
            // statuses read as a paired "transport / push" diagnostic.
            Row(
                modifier = Modifier
                    .clip(RoundedCornerShape(50))
                    .background(dotColor.copy(alpha = 0.16f))
                    .padding(horizontal = 12.dp, vertical = 6.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(6.dp),
            ) {
                Box(
                    modifier = Modifier
                        .size(8.dp)
                        .clip(CircleShape)
                        .background(dotColor),
                )
                Text(
                    label,
                    color = dotColor,
                    style = MaterialTheme.typography.labelMedium,
                    fontWeight = FontWeight.SemiBold,
                )
            }
        }

        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            MetadataPair(stringResource(R.string.sync_center_last_event), formatLastEvent(lastEventAt, now))
            MetadataPair(stringResource(R.string.sync_center_source), "SignalR")
        }
    }
}

/**
 * Hybrid display matching [formatLastSync] — absolute clock + relative age —
 * so the live channel and per-table rows read consistently.
 */
@Composable
private fun formatLastEvent(lastEventAt: Long?, now: Long): String {
    val ts = lastEventAt ?: return stringResource(R.string.sync_center_awaiting_event)
    val absolute = SimpleDateFormat("HH:mm:ss", LocalConfiguration.current.locales[0]).format(Date(ts))
    val age = (now - ts).coerceAtLeast(0)
    return "$absolute · ${humanizeAge(age)}"
}

@Composable
private fun SyncCenterHeader(onBack: () -> Unit, isOnline: Boolean) {
    val brandGradient = Brush.verticalGradient(
        colors = listOf(BrandRedDark, BrandRed, BrandRedLight),
    )
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(bottomStart = 28.dp, bottomEnd = 28.dp))
            .background(brandGradient),
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .statusBarsPadding()
                .padding(horizontal = 8.dp, vertical = 12.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            IconButton(onClick = onBack) {
                Icon(
                    Icons.AutoMirrored.Filled.ArrowBack,
                    contentDescription = stringResource(R.string.account_back),
                    tint = Color.White,
                )
            }
            Column(
                modifier = Modifier
                    .weight(1f)
                    .padding(start = 4.dp),
            ) {
                Text(
                    text = stringResource(R.string.sync_center_title),
                    style = MaterialTheme.typography.titleLarge,
                    fontWeight = FontWeight.Bold,
                    color = Color.White,
                )
                Text(
                    text = stringResource(R.string.sync_center_subtitle),
                    style = MaterialTheme.typography.bodySmall,
                    color = Color.White.copy(alpha = 0.85f),
                )
            }
            ConnectivityPill(isOnline = isOnline)
        }
    }
}

@Composable
private fun ConnectivityPill(isOnline: Boolean) {
    val (label, bg) = if (isOnline) stringResource(R.string.sync_center_online) to Color.White.copy(alpha = 0.22f)
    else stringResource(R.string.sync_center_offline) to Color.Black.copy(alpha = 0.28f)
    Row(
        modifier = Modifier
            .padding(end = 8.dp)
            .clip(RoundedCornerShape(50))
            .background(bg)
            .padding(horizontal = 12.dp, vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        Box(
            modifier = Modifier
                .size(8.dp)
                .clip(CircleShape)
                .background(if (isOnline) Color(0xFF8AFFA0) else Color(0xFFFFD0D0)),
        )
        Text(label, color = Color.White, style = MaterialTheme.typography.labelMedium)
    }
}

@Composable
private fun RefreshNowButton(
    isSyncing: Boolean,
    isOnline: Boolean,
    onClick: () -> Unit,
) {
    Button(
        onClick = onClick,
        enabled = !isSyncing && isOnline,
        modifier = Modifier
            .fillMaxWidth()
            .height(52.dp),
        shape = RoundedCornerShape(14.dp),
        colors = ButtonDefaults.buttonColors(
            containerColor = BrandRed,
            contentColor = Color.White,
        ),
    ) {
        if (isSyncing) {
            CircularProgressIndicator(
                modifier = Modifier.size(20.dp),
                color = Color.White,
                strokeWidth = 2.dp,
            )
            Text(
                stringResource(R.string.sync_center_refreshing),
                modifier = Modifier.padding(start = 12.dp),
                fontWeight = FontWeight.SemiBold,
            )
        } else {
            Icon(Icons.Default.Refresh, contentDescription = null)
            Text(
                stringResource(if (isOnline) R.string.sync_center_refresh_now else R.string.sync_center_refresh_offline),
                modifier = Modifier.padding(start = 8.dp),
                fontWeight = FontWeight.SemiBold,
            )
        }
    }
}

@Composable
private fun SyncRowCard(row: SyncRowState) {
    val borderColor =
        if (row.lastError != null || row.permissionDenied) MaterialTheme.colorScheme.error.copy(alpha = 0.5f)
        else MaterialTheme.colorScheme.outline.copy(alpha = 0.3f)

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(18.dp))
            .background(MaterialTheme.colorScheme.surface)
            .border(1.dp, borderColor, RoundedCornerShape(18.dp))
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            StatusBadge(row)
            Column(
                modifier = Modifier.weight(1f),
                verticalArrangement = Arrangement.spacedBy(2.dp),
            ) {
                Text(
                    text = stringResource(targetName(row.target)),
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.SemiBold,
                    color = MaterialTheme.colorScheme.onSurface,
                )
                Text(
                    text = pluralStringResource(R.plurals.sync_center_rows_cached, row.rowCount, row.rowCount),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }

        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            MetadataPair(stringResource(R.string.sync_center_last_sync), formatLastSync(row))
            MetadataPair(
                stringResource(R.string.sync_center_duration),
                row.lastDurationMs?.let { stringResource(R.string.sync_center_duration_ms, it) } ?: "—",
            )
        }

        if (row.lastError != null || row.permissionDenied) {
            InlineNotice(
                message = if (row.permissionDenied) stringResource(R.string.catalog_sync_denied)
                    else row.lastError ?: stringResource(if (row.target == SyncTarget.Flights)
                        R.string.flights_sync_failed else R.string.catalog_sync_failed),
                kind = InlineNoticeKind.Error,
            )
        }
    }
}

@Composable
private fun StatusBadge(row: SyncRowState) {
    val (icon, tint, bg) = when {
        row.lastError != null || row.permissionDenied -> Triple(
            Icons.Default.ErrorOutline,
            MaterialTheme.colorScheme.error,
            MaterialTheme.colorScheme.errorContainer.copy(alpha = 0.7f),
        )
        row.lastSyncedAt == null -> Triple(
            Icons.Default.CloudOff,
            MaterialTheme.colorScheme.onSurfaceVariant,
            MaterialTheme.colorScheme.surfaceVariant,
        )
        else -> Triple(
            Icons.Default.CheckCircle,
            BrandRed,
            BrandRed.copy(alpha = 0.12f),
        )
    }
    Box(
        modifier = Modifier
            .size(40.dp)
            .clip(CircleShape)
            .background(bg),
        contentAlignment = Alignment.Center,
    ) {
        Icon(icon, contentDescription = null, tint = tint, modifier = Modifier.size(22.dp))
    }
}

@Composable
private fun MetadataPair(label: String, value: String) {
    Column(verticalArrangement = Arrangement.spacedBy(1.dp)) {
        Text(
            text = label,
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Text(
            text = value,
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurface,
        )
    }
}

/**
 * Hybrid display: shows the absolute clock for the most recent sync plus a
 * coarse "X ago" suffix that ticks once per second courtesy of the ViewModel's
 * ticker flow.
 */
@Composable
private fun formatLastSync(row: SyncRowState): String {
    val ts = row.lastSyncedAt ?: return stringResource(R.string.sync_center_never)
    val absolute = SimpleDateFormat("HH:mm:ss", LocalConfiguration.current.locales[0]).format(Date(ts))
    val age = row.ageMs ?: return absolute
    val relative = humanizeAge(age)
    return "$absolute · $relative"
}

@Composable
private fun humanizeAge(ms: Long): String = when {
    ms < 5_000 -> stringResource(R.string.sync_center_just_now)
    ms < 60_000 -> stringResource(R.string.sync_center_seconds_ago, ms / 1000)
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
