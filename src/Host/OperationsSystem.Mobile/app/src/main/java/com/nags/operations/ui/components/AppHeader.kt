package com.nags.operations.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Logout
import androidx.compose.material.icons.filled.Notifications
import androidx.compose.material.icons.filled.AccountCircle
import androidx.compose.material3.Badge
import androidx.compose.material3.BadgedBox
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import com.nags.operations.R
import com.nags.operations.ui.theme.BrandRed
import com.nags.operations.ui.theme.BrandRedDark
import com.nags.operations.ui.theme.BrandRedLight

/** Branded header for the flight list, with profile and notification shortcuts. */
@Composable
fun AppHeader(
    displayName: String?,
    onLogout: () -> Unit,
    modifier: Modifier = Modifier,
    subtitle: String = "Welcome back",
    onNotificationsClick: (() -> Unit)? = null,
    unreadNotifications: Int = 0,
    onProfileClick: (() -> Unit)? = null,
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
            InitialsAvatar(displayName = displayName)

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

            if (onNotificationsClick != null) {
                NotificationBellButton(
                    unreadCount = unreadNotifications,
                    onClick = onNotificationsClick,
                )
            }
            if (onProfileClick != null) {
                IconButton(
                    onClick = onProfileClick,
                    modifier = Modifier.size(44.dp).clip(CircleShape)
                        .background(Color.White.copy(alpha = 0.16f))
                        .border(1.dp, Color.White.copy(alpha = 0.24f), CircleShape),
                ) {
                    Icon(
                        Icons.Default.AccountCircle,
                        contentDescription = stringResource(R.string.account_open),
                        tint = Color.White,
                        modifier = Modifier.size(24.dp),
                    )
                }
            } else {
                LogoutButton(onClick = onLogout)
            }
        }
    }
}

@Composable
private fun NotificationBellButton(unreadCount: Int, onClick: () -> Unit) {
    val description = if (unreadCount > 0) {
        pluralStringResource(R.plurals.notifications_bell_with_count, unreadCount, unreadCount)
    } else {
        stringResource(R.string.notifications_bell)
    }
    IconButton(
        onClick = onClick,
        modifier = Modifier
            .size(44.dp)
            .clip(CircleShape)
            .background(Color.White.copy(alpha = 0.16f))
            .border(1.dp, Color.White.copy(alpha = 0.24f), CircleShape)
            .semantics { contentDescription = description },
    ) {
        BadgedBox(
            badge = {
                if (unreadCount > 0) {
                    Badge(containerColor = Color.White, contentColor = BrandRedDark) {
                        Text(if (unreadCount > 99) "99+" else unreadCount.toString())
                    }
                }
            },
        ) {
            Icon(
                Icons.Default.Notifications,
                contentDescription = null,
                tint = Color.White,
                modifier = Modifier.size(21.dp),
            )
        }
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
