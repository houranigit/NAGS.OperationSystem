package com.nags.operations.ui.components

import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Logout
import androidx.compose.material.icons.filled.CloudDone
import androidx.compose.material.icons.filled.CloudOff
import androidx.compose.material.icons.filled.Sync
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.nags.operations.ui.theme.BrandRed
import com.nags.operations.ui.theme.BrandRedDark
import com.nags.operations.ui.theme.BrandRedLight

/**
 * Branded top header shown on every authenticated screen. Renders a
 * brand-red gradient panel, either an initials avatar or a tappable sync
 * status control, a two-line greeting, and a single logout affordance on the
 * right.
 *
 * The gradient draws **behind** the status bar; icons stay readable via the
 * activity's light status-bar appearance. Bottom corners match Sync Center.
 *
 * @param displayName the signed-in employee's name (or login handle). When
 *   null / blank the header gracefully falls back to "Hello, there".
 * @param onLogout invoked when the user taps the logout pill.
 * @param subtitle optional eyebrow above the greeting — defaults to
 *   "Welcome back", which reads correctly on a returning home surface but
 *   can be swapped per screen (e.g. "Today's flights").
 * @param onSyncCenterClick when non-null, replaces the initials avatar with a
 *   sync-status icon that navigates to the Sync Center when tapped.
 */
@Composable
fun AppHeader(
    displayName: String?,
    onLogout: () -> Unit,
    modifier: Modifier = Modifier,
    subtitle: String = "Welcome back",
    onSyncCenterClick: (() -> Unit)? = null,
    isOnline: Boolean = true,
    isSyncing: Boolean = false,
) {
    val brandGradient = Brush.verticalGradient(
        colors = listOf(BrandRedDark, BrandRed, BrandRedLight),
    )

    Box(
        modifier = modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(bottomStart = 28.dp, bottomEnd = 28.dp))
            .background(brandGradient),
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .statusBarsPadding()
                .padding(horizontal = 20.dp, vertical = 12.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            if (onSyncCenterClick != null) {
                SyncStatusHeaderButton(
                    isOnline = isOnline,
                    isSyncing = isSyncing,
                    onClick = onSyncCenterClick,
                )
            } else {
                InitialsAvatar(displayName = displayName)
            }

            Column(
                modifier = Modifier.weight(1f),
                verticalArrangement = Arrangement.spacedBy(2.dp),
            ) {
                Text(
                    text = subtitle,
                    style = MaterialTheme.typography.labelMedium,
                    color = Color.White.copy(alpha = 0.78f),
                )
                Text(
                    text = "Hello, ${friendlyOf(displayName)}",
                    style = MaterialTheme.typography.headlineSmall,
                    fontWeight = FontWeight.Bold,
                    color = Color.White,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }

            LogoutButton(onClick = onLogout)
        }
    }
}

@Composable
private fun SyncStatusHeaderButton(
    isOnline: Boolean,
    isSyncing: Boolean,
    onClick: () -> Unit,
) {
    Box(
        modifier = Modifier
            .size(52.dp)
            .clickable(onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Box(
            modifier = Modifier
                .fillMaxSize()
                .clip(CircleShape)
                .background(Color.White.copy(alpha = 0.18f))
                .border(
                    width = 1.dp,
                    color = Color.White.copy(alpha = 0.32f),
                    shape = CircleShape,
                ),
            contentAlignment = Alignment.Center,
        ) {
            val iconModifier = Modifier.size(26.dp)
            when {
                isSyncing -> {
                    val infiniteTransition = rememberInfiniteTransition(label = "syncHeaderIcon")
                    val rotation by infiniteTransition.animateFloat(
                        initialValue = 0f,
                        targetValue = 360f,
                        animationSpec = infiniteRepeatable(
                            animation = tween(1100, easing = LinearEasing),
                            repeatMode = RepeatMode.Restart,
                        ),
                        label = "rotation",
                    )
                    Icon(
                        imageVector = Icons.Default.Sync,
                        contentDescription = "Open Sync Center",
                        tint = Color.White,
                        modifier = iconModifier.rotate(rotation),
                    )
                }
                else -> {
                    Icon(
                        imageVector = if (!isOnline) Icons.Default.CloudOff else Icons.Default.CloudDone,
                        contentDescription = "Open Sync Center",
                        tint = Color.White,
                        modifier = iconModifier,
                    )
                }
            }
        }
        Box(
            modifier = Modifier
                .align(Alignment.TopEnd)
                .padding(4.dp)
                .size(10.dp)
                .clip(CircleShape)
                .background(if (isOnline) Color(0xFF22B07D) else Color(0xFF94A3B8)),
        )
    }
}

@Composable
private fun InitialsAvatar(displayName: String?) {
    Box(
        modifier = Modifier
            .size(52.dp)
            .clip(CircleShape)
            .background(Color.White.copy(alpha = 0.18f))
            .border(
                width = 1.dp,
                color = Color.White.copy(alpha = 0.32f),
                shape = CircleShape,
            ),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = initialsOf(displayName),
            style = MaterialTheme.typography.titleMedium,
            fontWeight = FontWeight.Bold,
            color = Color.White,
        )
    }
}

@Composable
private fun LogoutButton(onClick: () -> Unit) {
    IconButton(
        onClick = onClick,
        modifier = Modifier
            .size(44.dp)
            .clip(CircleShape)
            .background(Color.White.copy(alpha = 0.16f))
            .border(
                width = 1.dp,
                color = Color.White.copy(alpha = 0.24f),
                shape = CircleShape,
            ),
    ) {
        Icon(
            imageVector = Icons.AutoMirrored.Filled.Logout,
            contentDescription = "Sign out",
            tint = Color.White,
            modifier = Modifier.size(20.dp),
        )
    }
}

/**
 * The big greeting prefers the first name ("Ahmed" out of "Ahmed Mohamed Al
 * Saud") because it reads as friendlier in a one-line headline. When all we
 * have is a single-word handle (e.g. the JWT username before /me has been
 * fetched), we use it as-is so the avatar and greeting agree.
 */
private fun friendlyOf(name: String?): String {
    val cleaned = name?.trim().orEmpty()
    if (cleaned.isEmpty()) return "there"
    return cleaned.split(Regex("\\s+")).firstOrNull()?.takeIf { it.isNotBlank() } ?: "there"
}

/**
 * Two-letter monogram for the avatar: first letter of the first word + first
 * letter of the last word ("Ahmed Saud" -> "AS"). Single-word names collapse
 * to a single letter so the avatar never feels crowded.
 */
private fun initialsOf(name: String?): String {
    val cleaned = name?.trim().orEmpty()
    if (cleaned.isEmpty()) return "?"
    val words = cleaned.split(Regex("\\s+")).filter { it.isNotBlank() }
    return when (words.size) {
        0 -> "?"
        1 -> words[0].first().uppercaseChar().toString()
        else -> "${words.first().first().uppercaseChar()}${words.last().first().uppercaseChar()}"
    }
}
