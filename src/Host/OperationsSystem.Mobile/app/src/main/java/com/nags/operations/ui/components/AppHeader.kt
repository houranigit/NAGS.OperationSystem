package com.nags.operations.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.outlined.LocationOn
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.text
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.text.style.TextDirection
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.nags.operations.R
import com.nags.operations.ui.theme.BrandRed
import com.nags.operations.ui.home.MainHeaderState
import com.nags.operations.ui.home.MainHeaderSyncStatus
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.flow
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

/** Station context stays above the feed; the clock and compact action share a right-hand column. */
@Composable
internal fun AppHeader(
    state: MainHeaderState,
    onProfileClick: () -> Unit,
    onCreateAdHoc: () -> Unit,
    onSyncClick: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = MaterialTheme.colorScheme
    val profileDescription = stringResource(R.string.profile_menu_open)
    val createDescription = stringResource(R.string.home_create_adhoc)

    BoxWithConstraints(modifier.fillMaxWidth().background(colors.surface).statusBarsPadding()
        .padding(start = 16.dp, end = 16.dp, top = 10.dp)) {
        val density = LocalDensity.current
        val compact = maxWidth < 328.dp || density.fontScale > 1.3f
        val headingStyle = MaterialTheme.typography.headlineMedium.copy(
            fontSize = if (compact) 24.sp else 28.sp, fontWeight = FontWeight.SemiBold,
            fontFeatureSettings = "tnum", textDirection = TextDirection.Ltr)
        val actionStyle = MaterialTheme.typography.labelLarge.copy(fontSize = 12.sp, fontWeight = FontWeight.SemiBold)
        val actionLabel = stringResource(R.string.home_adhoc)
        val textMeasurer = rememberTextMeasurer()
        val clockSize = textMeasurer.measure(AnnotatedString("00:00"), headingStyle).size
        val actionSize = textMeasurer.measure(AnnotatedString(actionLabel), actionStyle).size
        val clockWidth = maxOf(82.dp, with(density) { clockSize.width.toDp() } + 8.dp,
            with(density) { actionSize.width.toDp() } + 33.dp)
        Box(Modifier.fillMaxWidth()) {
            Column(Modifier.fillMaxWidth().padding(end = clockWidth + 16.dp, bottom = 8.dp),
                verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    NagsLogo(Modifier.size(if (compact) 28.dp else 32.dp), tint = colors.primary)
                    Text(state.stationCode ?: stringResource(R.string.home_station_unavailable),
                        style = headingStyle.copy(fontWeight = FontWeight.Black), color = colors.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
                Row(Modifier.fillMaxWidth()
                    .clickable(role = Role.Button, onClick = onProfileClick)
                    .semantics { contentDescription = profileDescription },
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    AppProfileAvatar(state.displayName, size = 30.dp)
                    Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(1.dp)) {
                        HeaderEmployeeIdentity(state.displayName, state.employeeNumber)
                        state.manpowerTypeName?.let { manpower ->
                            Text(manpower, style = MaterialTheme.typography.bodySmall.copy(fontSize = 10.sp, lineHeight = 12.sp),
                                color = colors.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        }
                    }
                }
                HeaderSyncStatus(state, onSyncClick)
            }
            // The overlay follows the left rows' height instead of contributing its own minimum height.
            Box(Modifier.matchParentSize(), contentAlignment = Alignment.TopEnd) {
                Column(Modifier.width(clockWidth), horizontalAlignment = Alignment.CenterHorizontally) {
                    HeaderStationClock(state.stationTimeZoneId, headingStyle)
                    state.stationCity?.let { city ->
                        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.Center) {
                            Icon(Icons.Outlined.LocationOn, contentDescription = null,
                                tint = colors.primary, modifier = Modifier.size(13.dp))
                            Spacer(Modifier.width(3.dp))
                            Text(city, style = MaterialTheme.typography.bodySmall.copy(fontSize = 12.sp, lineHeight = 14.sp),
                                color = colors.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        }
                    }
                    // Match the clock/city's optical gap without moving either text row.
                    Spacer(Modifier.height(with(density) { 6.sp.toDp() }))
                    Box(Modifier.fillMaxWidth()
                        .clickable(role = Role.Button, onClick = onCreateAdHoc)
                        .semantics { contentDescription = createDescription }, contentAlignment = Alignment.BottomCenter) {
                        Row(Modifier.fillMaxWidth().heightIn(min = 32.dp)
                            .background(colors.primary, RoundedCornerShape(10.dp)).padding(horizontal = 6.dp, vertical = 5.dp),
                            verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.Center) {
                            Icon(Icons.Default.Add, contentDescription = null, tint = colors.onPrimary, modifier = Modifier.size(14.dp))
                            Spacer(Modifier.width(3.dp))
                            Text(actionLabel, modifier = Modifier.clearAndSetSemantics {},
                                style = actionStyle,
                                color = colors.onPrimary, maxLines = 1)
                        }
                    }
                }
            }
        }
    }
}

/** Prefer the complete identity, then the first name, then a modest type reduction before truncating. */
@Composable
private fun HeaderEmployeeIdentity(displayName: String?, employeeNumber: String?) {
    val employeeName = displayName?.trim()?.takeIf { it.isNotEmpty() } ?: stringResource(R.string.account_title)
    val firstName = employeeName.takeWhile { !it.isWhitespace() }
    val fullIdentity = if (employeeNumber == null) employeeName
        else stringResource(R.string.home_employee_identity, employeeNumber, employeeName)
    val shortIdentity = if (employeeNumber == null) firstName
        else stringResource(R.string.home_employee_identity, employeeNumber, firstName)
    val baseStyle = MaterialTheme.typography.bodyMedium.copy(
        fontSize = 15.sp, lineHeight = 18.sp, fontWeight = FontWeight.SemiBold)
    val measurer = rememberTextMeasurer()
    BoxWithConstraints(Modifier.fillMaxWidth()) {
        fun fits(value: String, style: TextStyle): Boolean = !measurer.measure(
            AnnotatedString(value), style = style, softWrap = false, maxLines = 1,
            constraints = constraints.copy(minWidth = 0, minHeight = 0),
        ).didOverflowWidth

        val fullFits = fits(fullIdentity, baseStyle)
        val value = if (fullFits) fullIdentity else shortIdentity
        val style = when {
            fullFits || fits(shortIdentity, baseStyle) -> baseStyle
            fits(shortIdentity, baseStyle.copy(fontSize = 14.sp)) -> baseStyle.copy(fontSize = 14.sp)
            else -> baseStyle.copy(fontSize = 13.sp)
        }
        Text(value, modifier = Modifier.clearAndSetSemantics { text = AnnotatedString(fullIdentity) },
            style = style, color = MaterialTheme.colorScheme.onSurface,
            maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis)
    }
}

@Composable
private fun HeaderStationClock(zoneId: String?, style: TextStyle) {
    val unavailable = stringResource(R.string.home_time_unavailable)
    val clock = remember(zoneId, unavailable) {
        flow {
            val zone = zoneId?.let { runCatching { ZoneId.of(it) }.getOrNull() }
            if (zone == null) emit(unavailable) else {
                val formatter = DateTimeFormatter.ofPattern("HH:mm", Locale.ROOT).withZone(zone)
                while (true) {
                    val now = Instant.now()
                    emit(formatter.format(now))
                    delay(60_000L - now.toEpochMilli() % 60_000L)
                }
            }
        }
    }
    val time by clock.collectAsStateWithLifecycle(initialValue = unavailable)
    val description = stringResource(R.string.home_station_local_time, time)
    Text(time, modifier = Modifier.semantics { contentDescription = description }, style = style,
        color = MaterialTheme.colorScheme.onSurface, maxLines = 1)
}

@Composable
private fun HeaderSyncStatus(state: MainHeaderState, onClick: () -> Unit) {
    val colors = MaterialTheme.colorScheme
    val connection = stringResource(if (state.isOnline) R.string.sync_center_online else R.string.sync_center_offline)
    val detail = when (state.syncStatus) {
        MainHeaderSyncStatus.Loading -> stringResource(R.string.home_status_loading)
        MainHeaderSyncStatus.Syncing -> stringResource(R.string.home_status_syncing)
        MainHeaderSyncStatus.NeedsReview -> stringResource(R.string.home_status_review, state.reviewCount)
        MainHeaderSyncStatus.Pending -> stringResource(R.string.home_status_pending, state.pendingCount)
        MainHeaderSyncStatus.Offline -> if (state.pendingCount > 0)
            stringResource(R.string.home_status_pending, state.pendingCount) else null
        MainHeaderSyncStatus.SyncError -> stringResource(R.string.home_status_sync_error)
        MainHeaderSyncStatus.Synced -> stringResource(R.string.home_status_synced)
        MainHeaderSyncStatus.Online -> null
    }
    val text = if (detail == null) connection else "$connection · $detail"
    Row(Modifier.fillMaxWidth().clickable(role = Role.Button, onClick = onClick),
        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        HeaderStatusIndicator(state.syncStatus, state.isOnline)
        Text(text, style = MaterialTheme.typography.bodySmall.copy(fontSize = 11.sp),
            color = colors.onSurfaceVariant, maxLines = 2, overflow = TextOverflow.Ellipsis)
    }
}

/** The same restrained initials treatment identifies the operator in the header and side menu. */
@Composable
fun AppProfileAvatar(displayName: String?, modifier: Modifier = Modifier, size: Dp = 36.dp) {
    Box(modifier.size(size).background(BrandRed, CircleShape),
        contentAlignment = Alignment.Center) {
        Text(avatarInitials(displayName.orEmpty()), modifier = Modifier.clearAndSetSemantics {}, style = MaterialTheme.typography.titleSmall.copy(
            fontSize = if (size >= 48.dp) 20.sp else 13.sp, fontWeight = FontWeight.SemiBold),
            color = Color.White, maxLines = 1, softWrap = false)
    }
}
