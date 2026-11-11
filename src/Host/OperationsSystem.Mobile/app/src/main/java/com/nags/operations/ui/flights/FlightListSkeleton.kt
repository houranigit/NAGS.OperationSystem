package com.nags.operations.ui.flights

import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.nags.operations.R

/** Loading placeholders follow the existing card geometry without replacing visible cards on refresh. */
@Composable
internal fun FlightListSkeleton(modifier: Modifier = Modifier) {
    val description = stringResource(R.string.flights_skeleton_loading)
    Column(modifier.padding(start = 16.dp, top = 16.dp, end = 16.dp, bottom = 80.dp)
        .clearAndSetSemantics { contentDescription = description; liveRegion = LiveRegionMode.Polite },
        verticalArrangement = Arrangement.spacedBy(8.dp)) {
        repeat(3) { FlightCardSkeleton() }
    }
}

@Composable
internal fun FlightCardSkeleton(modifier: Modifier = Modifier) {
    val transition = rememberInfiniteTransition(label = "Flight skeleton")
    val progress by transition.animateFloat(0f, 1f,
        animationSpec = infiniteRepeatable(tween(1300, easing = LinearEasing), RepeatMode.Restart),
        label = "Flight skeleton shimmer")
    val wave = with(LocalDensity.current) { 360.dp.toPx() }
    val brush = Brush.linearGradient(
        colors = listOf(Color(0xFFE6E9ED), Color(0xFFF3F5F7), Color(0xFFE6E9ED)),
        start = Offset((progress * 2f - 1f) * wave, 0f),
        end = Offset(progress * 2f * wave, wave),
    )
    val description = stringResource(R.string.flights_skeleton_loading)
    val shape = RoundedCornerShape(16.dp)
    Column(modifier.fillMaxWidth()
        .background(MaterialTheme.colorScheme.surface, shape)
        .border(1.dp, MaterialTheme.colorScheme.onSurface.copy(alpha = 0.08f), shape)
        .semantics { contentDescription = description }
        .padding(horizontal = 12.dp, vertical = 10.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Row(Modifier.fillMaxWidth().height(38.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
            SkeletonBlock(brush, Modifier.size(32.dp), corner = 10)
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                SkeletonBlock(brush, Modifier.width(66.dp).height(15.dp))
                SkeletonBlock(brush, Modifier.fillMaxWidth(0.8f).height(11.dp))
            }
            SkeletonBlock(brush, Modifier.width(66.dp).height(22.dp), corner = 12)
        }
        Row(Modifier.fillMaxWidth().height(16.dp),
            horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
            SkeletonBlock(brush, Modifier.width(91.dp).height(11.dp))
            SkeletonBlock(brush, Modifier.width(1.dp).height(14.dp), corner = 0)
            SkeletonBlock(brush, Modifier.width(94.dp).height(11.dp))
        }
        SkeletonBlock(brush, Modifier.width(132.dp).height(12.dp))
        HorizontalDivider(color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.05f))
        Row(Modifier.fillMaxWidth().height(16.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
            Box(Modifier.weight(1f)) { SkeletonBlock(brush, Modifier.width(86.dp).height(11.dp)) }
            Row(Modifier.weight(1.4f), horizontalArrangement = Arrangement.spacedBy(8.dp, Alignment.End)) {
                SkeletonBlock(brush, Modifier.weight(1f).height(11.dp))
                SkeletonBlock(brush, Modifier.weight(1f).height(11.dp))
            }
        }
    }
}

@Composable
private fun SkeletonBlock(brush: Brush, modifier: Modifier, corner: Int = 4) {
    Box(modifier.background(brush, RoundedCornerShape(corner.dp)))
}
