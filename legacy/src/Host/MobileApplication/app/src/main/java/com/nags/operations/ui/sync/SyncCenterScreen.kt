package com.nags.operations.ui.sync

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
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
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.nags.operations.data.realtime.RealtimeChannelState
import com.nags.operations.ui.theme.BrandRed
import com.nags.operations.ui.theme.BrandRedDark
import com.nags.operations.ui.theme.BrandRedLight
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * "Sync Center" screen. One card per logical table showing row count, last
 * sync timestamp, last sync duration, and the latest error (if any). A single
 * "Refresh now" button at the top runs every sync in parallel through the
 * [com.nags.operations.data.sync.SyncCoordinator].
 *
 * The header strip mirrors the app header on Home so the user immediately
 * knows where they are. The connectivity pill reflects [SyncCenterViewModel.isOnline]
 * so the user has a quick answer to "why hasn't anything synced?".
 */
@Composable
fun SyncCenterScreen(
    viewModel: SyncCenterViewModel,
    onBack: () -> Unit,
) {
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
            items(rows, key = { it.table.storageKey }) { row ->
                SyncRowCard(row)
            }
        }
    }
}

/**
 * Surfaces the SignalR push channel so operators can tell at a glance whether
 * they're actually receiving live updates. Three pieces of information:
 *
 *  • Coarse session state ([RealtimeChannelState]) as the headline word + dot.
 *  • Wall-clock and "Xs ago" for the most recent applied event — the proof
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
        is RealtimeChannelState.Connected -> "Connected" to Color(0xFF35C271)
        is RealtimeChannelState.Connecting -> "Connecting…" to Color(0xFFE0A93B)
        is RealtimeChannelState.Reconnecting -> "Reconnecting…" to Color(0xFFE0A93B)
        is RealtimeChannelState.Disconnected -> "Disconnected" to Color(0xFFC44848)
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
                    text = "Live channel",
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.SemiBold,
                    color = MaterialTheme.colorScheme.onSurface,
                )
                Text(
                    text = "Real-time push for flights, assignments, and lookups.",
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
            MetadataPair("Last event", formatLastEvent(lastEventAt, now))
            MetadataPair("Source", "SignalR")
        }
    }
}

/**
 * Hybrid display matching [formatLastSync] — absolute clock + relative age —
 * so the live channel and per-table rows read consistently.
 */
private fun formatLastEvent(lastEventAt: Long?, now: Long): String {
    val ts = lastEventAt ?: return "Awaiting first event"
    val absolute = SimpleDateFormat("HH:mm:ss", Locale.getDefault()).format(Date(ts))
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
                    contentDescription = "Back",
                    tint = Color.White,
                )
            }
            Column(
                modifier = Modifier
                    .weight(1f)
                    .padding(start = 4.dp),
            ) {
                Text(
                    text = "Sync Center",
                    style = MaterialTheme.typography.titleLarge,
                    fontWeight = FontWeight.Bold,
                    color = Color.White,
                )
                Text(
                    text = "Local cache that keeps the app working offline.",
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
    val (label, bg) = if (isOnline) "Online" to Color.White.copy(alpha = 0.22f)
    else "Offline" to Color.Black.copy(alpha = 0.28f)
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
                "Refreshing…",
                modifier = Modifier.padding(start = 12.dp),
                fontWeight = FontWeight.SemiBold,
            )
        } else {
            Icon(Icons.Default.Refresh, contentDescription = null)
            Text(
                if (isOnline) "Refresh now" else "Refresh now (offline)",
                modifier = Modifier.padding(start = 8.dp),
                fontWeight = FontWeight.SemiBold,
            )
        }
    }
}

@Composable
private fun SyncRowCard(row: SyncRowState) {
    val borderColor =
        if (row.lastError != null) MaterialTheme.colorScheme.error.copy(alpha = 0.5f)
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
                    text = row.table.displayName,
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.SemiBold,
                    color = MaterialTheme.colorScheme.onSurface,
                )
                Text(
                    text = "${row.rowCount} ${if (row.rowCount == 1) "row" else "rows"} cached",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }

        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            MetadataPair("Last sync", formatLastSync(row))
            MetadataPair(
                "Duration",
                row.lastDurationMs?.let { "${it} ms" } ?: "—",
            )
        }

        if (row.lastError != null) {
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(10.dp))
                    .background(MaterialTheme.colorScheme.errorContainer.copy(alpha = 0.6f))
                    .padding(12.dp),
            ) {
                Text(
                    text = row.lastError,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onErrorContainer,
                    maxLines = 3,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }
    }
}

@Composable
private fun StatusBadge(row: SyncRowState) {
    val (icon, tint, bg) = when {
        row.lastError != null -> Triple(
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
private fun formatLastSync(row: SyncRowState): String {
    val ts = row.lastSyncedAt ?: return "Never"
    val absolute = SimpleDateFormat("HH:mm:ss", Locale.getDefault()).format(Date(ts))
    val age = row.ageMs ?: return absolute
    val relative = humanizeAge(age)
    return "$absolute · $relative"
}

private fun humanizeAge(ms: Long): String = when {
    ms < 5_000 -> "just now"
    ms < 60_000 -> "${ms / 1000}s ago"
    ms < 3_600_000 -> "${ms / 60_000}m ago"
    ms < 86_400_000 -> "${ms / 3_600_000}h ago"
    else -> "${ms / 86_400_000}d ago"
}
