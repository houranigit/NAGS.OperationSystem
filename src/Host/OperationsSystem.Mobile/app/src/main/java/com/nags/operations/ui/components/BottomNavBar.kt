package com.nags.operations.ui.components

import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.navigationBars
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.layout.wrapContentSize
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.automirrored.filled.Assignment
import androidx.compose.material.icons.filled.CalendarMonth
import androidx.compose.material.icons.filled.Flight
import androidx.compose.material.icons.automirrored.filled.Note
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.PlatformTextStyle
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.LineHeightStyle
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/**
 * Floating icon-only bottom bar matching OperationsApplication styling.
 * **My flights**, **Per Landing**, centered **Create** (FAB), **Ad Hoc**, and **Drafts** —
 * aligned with the OperationsApplication bottom bar pattern.
 */
@Composable
fun BottomNavBar(
    selected: BottomNavDestination,
    onSelected: (BottomNavDestination) -> Unit,
    modifier: Modifier = Modifier,
) {
    Surface(
        modifier = modifier
            .fillMaxWidth()
            .windowInsetsPadding(WindowInsets.navigationBars)
            .padding(horizontal = 12.dp, vertical = 10.dp)
            .shadow(
                elevation = 18.dp,
                shape = RoundedCornerShape(28.dp),
                clip = false,
                ambientColor = MaterialTheme.colorScheme.primary.copy(alpha = 0.12f),
                spotColor = MaterialTheme.colorScheme.primary.copy(alpha = 0.20f),
            ),
        color = MaterialTheme.colorScheme.surface,
        contentColor = MaterialTheme.colorScheme.onSurface,
        shape = RoundedCornerShape(28.dp),
        tonalElevation = 4.dp,
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .heightIn(min = 72.dp)
                .padding(horizontal = 8.dp, vertical = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween,
        ) {
            BottomNavDestination.entries.forEach { destination ->
                if (destination == BottomNavDestination.Create) {
                    BottomNavCreateButton(
                        isSelected = false,
                        onClick = { onSelected(destination) },
                        modifier = Modifier.weight(1f),
                    )
                } else {
                    BottomNavItem(
                        destination = destination,
                        isSelected = destination == selected,
                        onClick = { if (destination != selected) onSelected(destination) },
                        modifier = Modifier.weight(1f),
                    )
                }
            }
        }
    }
}

@Composable
private fun BottomNavCreateButton(
    isSelected: Boolean,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val accent = MaterialTheme.colorScheme.primary
    Box(
        modifier = modifier
            .wrapContentSize(Alignment.Center)
            .semantics {
                role = Role.Tab
                selected = isSelected
                contentDescription = BottomNavDestination.Create.label
            },
        contentAlignment = Alignment.Center,
    ) {
        Box(
            modifier = Modifier
                .size(56.dp)
                .shadow(
                    elevation = 12.dp,
                    shape = RoundedCornerShape(50),
                    ambientColor = accent.copy(alpha = 0.30f),
                    spotColor = accent.copy(alpha = 0.45f),
                )
                .clip(RoundedCornerShape(50))
                .background(accent, RoundedCornerShape(50))
                .clickable(onClick = onClick),
            contentAlignment = Alignment.Center,
        ) {
            Icon(
                imageVector = Icons.Filled.Add,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.onPrimary,
                modifier = Modifier.size(28.dp),
            )
        }
    }
}

@Composable
private fun BottomNavItem(
    destination: BottomNavDestination,
    isSelected: Boolean,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val activeColor = MaterialTheme.colorScheme.primary
    val inactiveColor = MaterialTheme.colorScheme.onSurfaceVariant

    val pillBg by animateColorAsState(
        targetValue = if (isSelected) activeColor.copy(alpha = 0.14f) else Color.Transparent,
        animationSpec = tween(durationMillis = 220),
        label = "pillBg",
    )
    val iconTint by animateColorAsState(
        targetValue = if (isSelected) activeColor else inactiveColor,
        animationSpec = tween(durationMillis = 220),
        label = "iconTint",
    )

    Box(
        modifier = modifier
            .clip(RoundedCornerShape(20.dp))
            .clickable(onClick = onClick)
            .semantics {
                role = Role.Tab
                selected = isSelected
                contentDescription = destination.label
            },
        contentAlignment = Alignment.Center,
    ) {
        Box(
            modifier = Modifier
                .clip(RoundedCornerShape(20.dp))
                .background(pillBg)
                .padding(
                    horizontal = if (destination == BottomNavDestination.PerLanding) 10.dp else 18.dp,
                    vertical = if (destination == BottomNavDestination.PerLanding) 8.dp else 12.dp,
                ),
            contentAlignment = Alignment.Center,
        ) {
            if (destination == BottomNavDestination.PerLanding) {
                // Wordmark stands in for an icon; parent exposes the accessible label.
                Text(
                    text = "Per\nLanding",
                    color = iconTint,
                    style = TextStyle(
                        fontSize = 11.sp,
                        fontWeight = FontWeight.Bold,
                        lineHeight = 12.sp,
                        textAlign = TextAlign.Center,
                        letterSpacing = (-0.2).sp,
                        platformStyle = PlatformTextStyle(includeFontPadding = false),
                        lineHeightStyle = LineHeightStyle(
                            alignment = LineHeightStyle.Alignment.Center,
                            trim = LineHeightStyle.Trim.Both,
                        ),
                    ),
                    maxLines = 2,
                    softWrap = false,
                    modifier = Modifier.clearAndSetSemantics { },
                )
            } else {
                Icon(
                    imageVector = destination.icon,
                    contentDescription = null,
                    tint = iconTint,
                    modifier = Modifier.size(26.dp),
                )
            }
        }
    }
}

enum class BottomNavDestination(
    val route: String,
    val label: String,
    val icon: ImageVector,
) {
    MyFlights("flights", "My flights", Icons.Filled.CalendarMonth),
    PerLanding("per_landing_flights", "Per Landing", Icons.Filled.Flight),
    /** Center FAB — opens root create flow; not a real inner-nav tab destination. */
    Create("quick_create", "Create", Icons.Filled.Add),
    AdHoc("ad_hoc_flights", "Ad Hoc flights", Icons.AutoMirrored.Filled.Assignment),
    Drafts("work_order_drafts", "Drafts", Icons.AutoMirrored.Filled.Note),
}
