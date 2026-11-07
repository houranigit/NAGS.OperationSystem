package com.nags.operations.ui.home

import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.selectableGroup
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Flight
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.outlined.AccountBalanceWallet
import androidx.compose.material.icons.outlined.EmojiEvents
import androidx.compose.material.icons.automirrored.outlined.CompareArrows
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.dp
import com.nags.operations.R

/** Four page destinations share a capsule; search is a separate action. */
@Composable
internal fun MainBottomNavigation(
    selectedPage: Int,
    onSelectPage: (Int) -> Unit,
    onSearch: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = MaterialTheme.colorScheme
    val icons = listOf(Icons.Default.Flight, Icons.AutoMirrored.Outlined.CompareArrows,
        Icons.Outlined.EmojiEvents, Icons.Outlined.AccountBalanceWallet)
    val highlightPosition by animateFloatAsState(
        targetValue = selectedPage.toFloat(),
        animationSpec = tween(240, easing = FastOutSlowInEasing),
        label = "Navigation highlight",
    )

    Row(
        modifier = modifier.fillMaxWidth().padding(horizontal = 16.dp),
        horizontalArrangement = Arrangement.spacedBy(8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Row(
            modifier = Modifier.weight(1f).heightIn(min = 54.dp)
                .clip(CircleShape).background(colors.surface, CircleShape)
                .border(1.dp, colors.outline.copy(alpha = 0.65f), CircleShape)
                .selectableGroup().padding(horizontal = 8.dp)
                .drawBehind {
                    val itemWidth = size.width / icons.size
                    val logicalCenter = itemWidth * (highlightPosition + 0.5f)
                    val center = if (layoutDirection == LayoutDirection.Rtl) size.width - logicalCenter else logicalCenter
                    val width = 52.dp.toPx().coerceAtMost(itemWidth)
                    val height = 36.dp.toPx()
                    drawRoundRect(
                        color = colors.primaryContainer,
                        topLeft = Offset(center - width / 2f, (size.height - height) / 2f),
                        size = Size(width, height),
                        cornerRadius = CornerRadius(height / 2f),
                    )
                },
            verticalAlignment = Alignment.CenterVertically,
        ) {
            icons.forEachIndexed { index, icon ->
                val description = when (index) {
                    0 -> stringResource(R.string.home_nav_flights)
                    1 -> stringResource(R.string.home_nav_hand_over)
                    else -> stringResource(R.string.home_nav_page, index + 1)
                }
                val selected = selectedPage == index
                Box(
                    modifier = Modifier.weight(1f).heightIn(min = 54.dp).clip(CircleShape)
                        .selectable(selected = selected, role = Role.Tab,
                            onClick = { onSelectPage(index) })
                        .semantics { contentDescription = description },
                    contentAlignment = Alignment.Center,
                ) {
                    Icon(icon, contentDescription = null,
                        tint = if (selected) colors.primary else colors.onSurfaceVariant,
                        modifier = Modifier.size(24.dp))
                }
            }
        }
        IconButton(
            onClick = onSearch,
            modifier = Modifier.size(54.dp)
                .background(colors.surface, CircleShape)
                .border(1.dp, colors.outline.copy(alpha = 0.65f), CircleShape),
        ) {
            Icon(Icons.Default.Search, contentDescription = stringResource(R.string.home_search),
                tint = colors.onSurface, modifier = Modifier.size(24.dp))
        }
    }
}
