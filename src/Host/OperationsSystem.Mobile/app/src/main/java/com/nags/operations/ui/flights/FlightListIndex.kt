package com.nags.operations.ui.flights

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraintsScope
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Check
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.boundsInParent
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nags.operations.R

internal val FlightPickerActionGreen = Color(0xFF167A32)

/** Page ground sampled from the reference, with the matching native dark surface. */
@Composable
internal fun flightPickerPageBackground(): Color =
    if (MaterialTheme.colorScheme.surface.luminance() > 0.5f) Color(0xFFF1F1F1) else Color(0xFF1C1C1E)

/** Grouped choice cards remain distinct from the surrounding page in both themes. */
@Composable
internal fun flightPickerCardBackground(): Color =
    if (MaterialTheme.colorScheme.surface.luminance() > 0.5f) Color(0xFFFEFEFE) else Color(0xFF2C2C2E)

@Composable
internal fun FlightPickerSelectionMark(selected: Boolean, enabled: Boolean = true) {
    val selection = FlightPickerActionGreen.copy(alpha = if (enabled) 1f else 0.38f)
    val border = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = if (enabled) 0.75f else 0.38f)
    Box(Modifier.size(20.dp)
        .background(if (selected) selection else Color.Transparent, CircleShape)
        .then(if (selected) Modifier else Modifier.border(1.25.dp, border, CircleShape))
        .clearAndSetSemantics { }, contentAlignment = Alignment.Center) {
        if (selected) Icon(Icons.Outlined.Check, null, Modifier.size(14.dp), tint = Color.White)
    }
}

/** A direct-touch index overlays its list without reserving more than the existing end gutter. */
@Composable
internal fun BoxWithConstraintsScope.FlightListIndex(
    letters: List<String>,
    currentLetter: String?,
    enabled: Boolean = true,
    onJump: (String) -> Unit,
) {
    if (letters.size <= 1 || maxHeight < 13.dp) return
    val indexGreen = if (MaterialTheme.colorScheme.surface.luminance() > 0.5f) FlightPickerActionGreen else FlightFormGreen
    val letterBounds = remember(letters) { mutableMapOf<String, Rect>() }
    var touchedLetter by remember { mutableStateOf<String?>(null) }
    val haptics = LocalHapticFeedback.current
    val jump by rememberUpdatedState(onJump)
    val naturalRowHeight = maxOf(13.dp, with(LocalDensity.current) { 11.sp.toDp() })
    val rowHeight = minOf(naturalRowHeight, maxHeight / letters.size)
    val textScale = rowHeight / naturalRowHeight
    Column(Modifier.align(Alignment.CenterEnd).width(16.dp).height(rowHeight * letters.size)
        .pointerInput(enabled, letters) {
            if (!enabled) return@pointerInput
            awaitEachGesture {
                val down = awaitFirstDown(requireUnconsumed = false, pass = PointerEventPass.Initial)
                down.consume()
                var lastLetter: String? = null
                fun followFinger(y: Float) {
                    val letter = letters.minByOrNull { candidate ->
                        letterBounds[candidate]?.let { kotlin.math.abs(it.center.y - y) } ?: Float.POSITIVE_INFINITY
                    } ?: return
                    if (letter != lastLetter) {
                        lastLetter = letter
                        touchedLetter = letter
                        jump(letter)
                        haptics.performHapticFeedback(HapticFeedbackType.SegmentFrequentTick)
                    }
                }
                try {
                    followFinger(down.position.y)
                    do {
                        val event = awaitPointerEvent(PointerEventPass.Initial)
                        val change = event.changes.firstOrNull { it.id == down.id } ?: break
                        change.consume()
                        if (!change.pressed) break
                        followFinger(change.position.y)
                    } while (true)
                } finally {
                    touchedLetter = null
                }
            }
        }, horizontalAlignment = Alignment.CenterHorizontally) {
        letters.forEach { letter ->
            val label = stringResource(R.string.flight_share_jump_to_letter, letter)
            Box(Modifier.width(16.dp).weight(1f)
                .onGloballyPositioned { letterBounds[letter] = it.boundsInParent() }
                .clickable(enabled = enabled, role = Role.Button, onClickLabel = label) { jump(letter) }
                .semantics { contentDescription = label }, contentAlignment = Alignment.Center) {
                Text(letter, style = MaterialTheme.typography.labelSmall, fontSize = 10.sp * textScale,
                    lineHeight = 11.sp * textScale, maxLines = 1, softWrap = false, color = indexGreen,
                    fontWeight = if ((touchedLetter ?: currentLetter) == letter) FontWeight.Bold else FontWeight.Medium)
            }
        }
    }
    touchedLetter?.let { letter ->
        val bubbleSize = minOf(56.dp, maxHeight)
        val letterIndex = letters.indexOf(letter)
        val center = (maxHeight - rowHeight * letters.size) / 2 + rowHeight * (letterIndex + 0.5f)
        val bubbleTop = (center - bubbleSize / 2).coerceIn(0.dp, maxOf(0.dp, maxHeight - bubbleSize))
        Surface(Modifier.align(Alignment.TopEnd).offset(x = (-28).dp, y = bubbleTop)
            .size(bubbleSize).clearAndSetSemantics { }, shape = CircleShape,
            color = FlightPickerActionGreen, contentColor = Color.White, shadowElevation = 4.dp) {
            Box(contentAlignment = Alignment.Center) {
                Text(letter, style = MaterialTheme.typography.headlineMedium, fontSize = 28.sp,
                    lineHeight = 32.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, softWrap = false)
            }
        }
    }
}
