package com.nags.operations.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.AccountCircle
import androidx.compose.material.icons.outlined.NotificationsNone
import androidx.compose.material3.Badge
import androidx.compose.material3.BadgedBox
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nags.operations.R

/** Compact home header; page tabs belong to the content below it. */
@Composable
fun AppHeader(
    displayName: String?,
    onProfileClick: () -> Unit,
    onNotificationsClick: () -> Unit,
    modifier: Modifier = Modifier,
    unreadNotifications: Int = 0,
    onAdHocClick: () -> Unit = {},
) {
    val foreground = MaterialTheme.colorScheme.onSurface
    val muted = MaterialTheme.colorScheme.onSurfaceVariant
    val friendlyName = displayName?.trim()?.takeIf { it.isNotEmpty() }
        ?.split(Regex("\\s+"))?.firstOrNull()
    val greeting = if (friendlyName == null) stringResource(R.string.home_greeting_fallback)
        else stringResource(R.string.home_greeting, friendlyName)
    val notificationDescription = if (unreadNotifications > 0) {
        pluralStringResource(R.plurals.notifications_bell_with_count, unreadNotifications, unreadNotifications)
    } else {
        stringResource(R.string.notifications_bell)
    }

    Column(modifier.fillMaxWidth().background(MaterialTheme.colorScheme.surface).statusBarsPadding()) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(start = 16.dp, end = 16.dp, bottom = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            IconButton(onClick = onProfileClick, modifier = Modifier.size(48.dp)) {
                Box(Modifier.size(36.dp).background(MaterialTheme.colorScheme.primaryContainer, CircleShape),
                    contentAlignment = Alignment.Center) {
                    Icon(Icons.Default.AccountCircle,
                        contentDescription = stringResource(R.string.account_open),
                        tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(26.dp))
                }
            }
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text(stringResource(R.string.home_welcome_back),
                    style = MaterialTheme.typography.bodySmall,
                    color = muted, maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis)
                Text(greeting,
                    style = MaterialTheme.typography.titleMedium,
                    color = foreground,
                    maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis)
            }
            Box(
                modifier = Modifier.widthIn(min = 68.dp).heightIn(min = 48.dp)
                    .clip(RoundedCornerShape(10.dp)).clickable(role = Role.Button, onClick = onAdHocClick),
                contentAlignment = Alignment.Center,
            ) {
                Box(
                    modifier = Modifier.widthIn(min = 68.dp).heightIn(min = 32.dp)
                        .background(MaterialTheme.colorScheme.primary, RoundedCornerShape(8.dp))
                        .padding(horizontal = 12.dp, vertical = 6.dp),
                    contentAlignment = Alignment.Center,
                ) {
                    Text(stringResource(R.string.home_adhoc),
                        style = MaterialTheme.typography.labelMedium.copy(lineHeight = 16.sp, letterSpacing = 0.sp),
                        fontWeight = FontWeight.SemiBold, color = MaterialTheme.colorScheme.onPrimary, maxLines = 1)
                }
            }
            IconButton(
                onClick = onNotificationsClick,
                modifier = Modifier.size(48.dp).semantics { contentDescription = notificationDescription },
            ) {
                BadgedBox(badge = {
                    if (unreadNotifications > 0) {
                        Badge(containerColor = MaterialTheme.colorScheme.primary,
                            contentColor = MaterialTheme.colorScheme.onPrimary) {
                            Text(if (unreadNotifications > 99) "99+" else unreadNotifications.toString(),
                                fontSize = 10.sp, lineHeight = 12.sp)
                        }
                    }
                }) {
                    Icon(Icons.Outlined.NotificationsNone, contentDescription = null,
                        tint = muted, modifier = Modifier.size(24.dp))
                }
            }
        }
    }
}
