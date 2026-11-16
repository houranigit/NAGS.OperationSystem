package com.nags.operations.ui.components

import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.PriorityHigh
import androidx.compose.material.icons.filled.Sync
import androidx.compose.material3.Icon
import androidx.compose.runtime.Composable
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.MotionDurationScale
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.unit.dp
import com.nags.operations.ui.home.MainHeaderSyncStatus
import com.nags.operations.ui.theme.AccentRed

/** Decorative companion to the header's readable connection and sync status. */
@Composable
internal fun HeaderStatusIndicator(
    status: MainHeaderSyncStatus,
    isOnline: Boolean,
    modifier: Modifier = Modifier,
) {
    val orange = Color(0xFFAD6800)
    Box(modifier.size(12.dp), contentAlignment = Alignment.Center) {
        when (status) {
            MainHeaderSyncStatus.Syncing -> {
                val motionScale = rememberCoroutineScope().coroutineContext[MotionDurationScale]
                val rotationModifier = if (motionScale?.scaleFactor == 0f) Modifier else {
                    val rotation = rememberInfiniteTransition(label = "Header sync").animateFloat(
                        initialValue = 0f,
                        targetValue = 360f,
                        animationSpec = infiniteRepeatable(tween(1000, easing = LinearEasing)),
                        label = "Sync rotation",
                    )
                    Modifier.graphicsLayer { rotationZ = rotation.value }
                }
                Icon(Icons.Default.Sync, contentDescription = null, tint = orange,
                    modifier = rotationModifier.fillMaxSize())
            }
            MainHeaderSyncStatus.NeedsReview, MainHeaderSyncStatus.SyncError ->
                Icon(Icons.Default.PriorityHigh, contentDescription = null, tint = AccentRed,
                    modifier = Modifier.fillMaxSize())
            else -> {
                val color = when (status) {
                    MainHeaderSyncStatus.Pending -> orange
                    MainHeaderSyncStatus.Loading -> Color(0xFF808080)
                    else -> if (isOnline) Color(0xFF248A3D) else Color(0xFF808080)
                }
                Box(Modifier.size(7.dp).background(color, CircleShape))
            }
        }
    }
}
