package com.nags.operations.ui.home

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
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
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
    val foreground = Color(0xFF17181B)
    val muted = Color(0xFF858991)
    val fill = Color.White.copy(alpha = 0.94f)
    val edge = Color(0xFFD8DCE2).copy(alpha = 0.85f)
    val icons = listOf(Icons.Default.Flight, Icons.AutoMirrored.Outlined.CompareArrows,
        Icons.Outlined.EmojiEvents, Icons.Outlined.AccountBalanceWallet)

    Row(
        modifier = modifier.fillMaxWidth().padding(horizontal = 16.dp),
        horizontalArrangement = Arrangement.spacedBy(8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Row(
            modifier = Modifier.weight(1f).heightIn(min = 54.dp)
                .shadow(5.dp, CircleShape, clip = false,
                    ambientColor = Color.Black.copy(alpha = 0.08f),
                    spotColor = Color.Black.copy(alpha = 0.08f))
                .clip(CircleShape).background(fill, CircleShape).border(0.75.dp, edge, CircleShape)
                .selectableGroup().padding(horizontal = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            icons.forEachIndexed { index, icon ->
                val description = when (index) {
                    0 -> stringResource(R.string.home_nav_flights)
                    1 -> stringResource(R.string.home_nav_hand_over)
                    else -> stringResource(R.string.home_nav_page, index + 1)
                }
                Box(
                    modifier = Modifier.weight(1f).heightIn(min = 54.dp)
                        .selectable(selected = selectedPage == index, role = Role.Tab,
                            onClick = { onSelectPage(index) })
                        .semantics { contentDescription = description },
                    contentAlignment = Alignment.Center,
                ) {
                    Icon(icon, contentDescription = null,
                        tint = if (selectedPage == index) foreground else muted,
                        modifier = Modifier.size(24.dp))
                }
            }
        }
        IconButton(
            onClick = onSearch,
            modifier = Modifier.size(54.dp)
                .shadow(5.dp, CircleShape, clip = false,
                    ambientColor = Color.Black.copy(alpha = 0.08f),
                    spotColor = Color.Black.copy(alpha = 0.08f))
                .background(fill, CircleShape).border(0.75.dp, edge, CircleShape),
        ) {
            Icon(Icons.Default.Search, contentDescription = stringResource(R.string.home_search),
                tint = foreground, modifier = Modifier.size(24.dp))
        }
    }
}
