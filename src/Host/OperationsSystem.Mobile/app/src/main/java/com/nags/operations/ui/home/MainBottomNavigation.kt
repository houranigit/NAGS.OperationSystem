package com.nags.operations.ui.home

import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.selectableGroup
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.ManageSearch
import androidx.compose.material.icons.filled.Flight
import androidx.compose.material.icons.outlined.History
import androidx.compose.material.icons.outlined.NotificationsNone
import androidx.compose.material.icons.outlined.Share
import androidx.compose.material3.Badge
import androidx.compose.material3.BadgedBox
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nags.operations.R

/** Five icon-only destinations; AdHoc creation belongs to the header. */
@Composable
internal fun MainBottomNavigation(
    selectedPage: Int,
    onSelectPage: (Int) -> Unit,
    modifier: Modifier = Modifier,
    unreadNotifications: Int = 0,
) {
    val colors = MaterialTheme.colorScheme
    val dockShape = RoundedCornerShape(32.dp)
    val icons = listOf(Icons.Default.Flight, Icons.Outlined.Share,
        Icons.AutoMirrored.Outlined.ManageSearch, Icons.Outlined.History, Icons.Outlined.NotificationsNone)
    val notificationDescription = if (unreadNotifications > 0) {
        pluralStringResource(R.plurals.notifications_bell_with_count, unreadNotifications, unreadNotifications)
    } else {
        stringResource(R.string.notifications_bell)
    }
    val descriptions = listOf(stringResource(R.string.home_nav_flights), stringResource(R.string.home_nav_invitations),
        stringResource(R.string.home_nav_search), stringResource(R.string.home_nav_my_work), notificationDescription)
    val highlightPosition by animateFloatAsState(
        targetValue = selectedPage.toFloat(),
        animationSpec = tween(240, easing = FastOutSlowInEasing),
        label = "Navigation highlight",
    )

    Row(
        modifier = modifier.fillMaxWidth().padding(horizontal = 12.dp).height(64.dp)
            .shadow(4.dp, dockShape, clip = false).clip(dockShape).background(colors.surface)
            .selectableGroup().padding(horizontal = 4.dp)
            .drawBehind {
                val itemWidth = size.width / icons.size
                val logicalCenter = itemWidth * (highlightPosition + 0.5f)
                val center = if (layoutDirection == LayoutDirection.Rtl) size.width - logicalCenter else logicalCenter
                drawCircle(color = colors.primaryContainer, radius = 20.dp.toPx(),
                    center = Offset(center, size.height / 2f))
            },
        verticalAlignment = Alignment.CenterVertically,
    ) {
        icons.forEachIndexed { index, icon ->
            val selected = selectedPage == index
            val foreground = if (selected) colors.primary else colors.onSurfaceVariant
            Box(
                modifier = Modifier.weight(1f).fillMaxHeight().widthIn(min = 48.dp)
                    .selectable(selected = selected, role = Role.Tab, onClick = { onSelectPage(index) })
                    .semantics { contentDescription = descriptions[index] },
                contentAlignment = Alignment.Center,
            ) {
                if (index == 4) {
                    BadgedBox(modifier = Modifier.clearAndSetSemantics {}, badge = {
                        if (unreadNotifications > 0) {
                            Badge(containerColor = colors.primary, contentColor = colors.onPrimary) {
                                Text(if (unreadNotifications > 99) "99+" else unreadNotifications.toString(),
                                    style = MaterialTheme.typography.labelSmall.copy(fontSize = 10.sp, lineHeight = 12.sp))
                            }
                        }
                    }) {
                        Icon(icon, contentDescription = null, tint = foreground, modifier = Modifier.size(26.dp))
                    }
                } else {
                    Icon(icon, contentDescription = null, tint = foreground, modifier = Modifier.size(26.dp))
                }
            }
        }
    }
}
