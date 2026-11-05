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
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
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
    val foreground = Color(0xFF17181B)
    val muted = Color(0xFF6B7076)
    val controlFill = Color(0xFFF2F3F5)
    val friendlyName = displayName?.trim()?.takeIf { it.isNotEmpty() }
        ?.split(Regex("\\s+"))?.firstOrNull()
    val greeting = if (friendlyName == null) stringResource(R.string.home_greeting_fallback)
        else stringResource(R.string.home_greeting, friendlyName)
    val notificationDescription = if (unreadNotifications > 0) {
        pluralStringResource(R.plurals.notifications_bell_with_count, unreadNotifications, unreadNotifications)
    } else {
        stringResource(R.string.notifications_bell)
    }

    Column(modifier.fillMaxWidth().background(Color.White).statusBarsPadding()) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(4.dp),
        ) {
            IconButton(onClick = onProfileClick, modifier = Modifier.size(48.dp)) {
                Box(Modifier.size(40.dp).background(controlFill, CircleShape),
                    contentAlignment = Alignment.Center) {
                    Icon(Icons.Default.AccountCircle,
                        contentDescription = stringResource(R.string.account_open),
                        tint = foreground, modifier = Modifier.size(28.dp))
                }
            }
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text(stringResource(R.string.home_welcome_back),
                    style = MaterialTheme.typography.bodySmall.copy(fontSize = 12.sp, lineHeight = 16.sp),
                    color = muted, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(greeting,
                    style = MaterialTheme.typography.headlineSmall.copy(fontSize = 20.sp, lineHeight = 24.sp),
                    fontWeight = FontWeight.Bold, color = foreground,
                    maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            Box(
                modifier = Modifier.widthIn(min = 74.dp).heightIn(min = 48.dp)
                    .clip(RoundedCornerShape(10.dp)).clickable(role = Role.Button, onClick = onAdHocClick),
                contentAlignment = Alignment.Center,
            ) {
                Box(
                    modifier = Modifier.widthIn(min = 74.dp).heightIn(min = 32.dp)
                        .background(controlFill, RoundedCornerShape(8.dp))
                        .padding(horizontal = 12.dp, vertical = 6.dp),
                    contentAlignment = Alignment.Center,
                ) {
                    Text(stringResource(R.string.home_adhoc),
                        style = MaterialTheme.typography.labelLarge.copy(fontSize = 14.sp,
                            lineHeight = 20.sp, letterSpacing = 0.sp),
                        fontWeight = FontWeight.SemiBold, color = foreground, maxLines = 1)
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
                        tint = foreground, modifier = Modifier.size(24.dp))
                }
            }
        }
        HorizontalDivider(thickness = 0.5.dp, color = Color(0xFFEDEFF2))
    }
}
