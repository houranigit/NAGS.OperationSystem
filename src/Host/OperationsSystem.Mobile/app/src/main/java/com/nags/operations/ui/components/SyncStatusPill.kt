package com.nags.operations.ui.components

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.CloudOff
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

/**
 * Compact sync affordance for flight screen toolbars — visually aligned with
 * OperationsApplication's pill; backend here is only coordinator/network flags.
 */
@Composable
fun SyncStatusPill(
    isSyncing: Boolean,
    isOnline: Boolean,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val (label, color, icon) = when {
        !isOnline -> Triple("Offline", Color(0xFFFFA000), Icons.Default.CloudOff)
        isSyncing -> Triple("Syncing…", Color(0xFF1976D2), Icons.Default.Refresh)
        else -> Triple("All synced", Color(0xFF2E7D32), Icons.Default.CheckCircle)
    }
    Surface(
        modifier = modifier.clickable(onClick = onClick),
        shape = RoundedCornerShape(50),
        color = color.copy(alpha = 0.12f),
        contentColor = color,
        border = BorderStroke(1.dp, color.copy(alpha = 0.4f)),
    ) {
        Row(
            modifier = Modifier.padding(horizontal = 10.dp, vertical = 6.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(6.dp),
        ) {
            if (isSyncing && isOnline) {
                CircularProgressIndicator(
                    modifier = Modifier.size(12.dp),
                    color = color,
                    strokeWidth = 1.5.dp,
                )
            } else {
                Icon(icon, contentDescription = null, modifier = Modifier.size(14.dp))
            }
            Text(
                label,
                style = MaterialTheme.typography.labelMedium,
                fontWeight = FontWeight.SemiBold,
            )
        }
    }
}
