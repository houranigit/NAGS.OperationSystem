package com.nags.operations.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import java.text.Normalizer

private val AvatarPalette = listOf(
    Color(0xFFE7F1FF) to Color(0xFF285A91),
    Color(0xFFE4F3EF) to Color(0xFF326958),
    Color(0xFFEDF0F5) to Color(0xFF4F5B6B),
    Color(0xFFE8EEF8) to Color(0xFF4D6284),
)
private val NameSeparators = Regex("[\\p{Z}\\s]+")

/** Compact visual summary; callers can expose the full roster or service list on expansion. */
@Composable
fun InitialsAvatarStack(
    names: List<String>,
    modifier: Modifier = Modifier,
    maxVisible: Int = 4,
    avatarSize: Dp = 32.dp,
) {
    if (names.isEmpty()) return
    val visibleNames = names.take(maxVisible.coerceAtLeast(1))
    val remaining = names.size - visibleNames.size
    val size = avatarSize.coerceAtLeast(20.dp)
    val outline = MaterialTheme.colorScheme.surface
    Row(
        modifier = modifier.clearAndSetSemantics {
            contentDescription = names.joinToString(", ") { it.trim().ifEmpty { "?" } }
        },
        horizontalArrangement = Arrangement.spacedBy(-size * 0.28f),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        visibleNames.forEach { name ->
            val (background, foreground) = AvatarPalette[Math.floorMod(name.trim().hashCode(), AvatarPalette.size)]
            InitialsAvatar(avatarInitials(name), size, background, foreground, outline)
        }
        if (remaining > 0) {
            InitialsAvatar("+$remaining", size, Color(0xFFEDF0F5), Color(0xFF4F5B6B), outline)
        }
    }
}

@Composable
private fun InitialsAvatar(text: String, size: Dp, background: Color, foreground: Color, outline: Color) {
    Box(
        Modifier.size(size).background(background, CircleShape).border(2.dp, outline, CircleShape),
        contentAlignment = Alignment.Center,
    ) {
        Text(text, color = foreground, fontWeight = FontWeight.SemiBold,
            fontSize = (size.value * if (text.codePointCount(0, text.length) > 3) 0.30f else 0.36f).sp,
            maxLines = 1, overflow = TextOverflow.Ellipsis)
    }
}

/** Uses Unicode code points so a supplementary letter never becomes half a surrogate pair. */
internal fun avatarInitials(name: String): String {
    val words = Normalizer.normalize(name.trim(), Normalizer.Form.NFC)
        .split(NameSeparators)
        .map { word -> word.codePoints().filter { Character.isLetterOrDigit(it) }.toArray() }
        .filter { it.isNotEmpty() }
    if (words.isEmpty()) return "?"
    val initials = if (words.size == 1) words.first().take(2)
        else listOf(words.first().first(), words.last().first())
    return buildString {
        initials.forEach { appendCodePoint(Character.toUpperCase(it)) }
    }
}
