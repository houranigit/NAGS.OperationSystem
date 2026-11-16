package com.nags.operations.ui.components

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.layout.Layout
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.layout.positionInWindow
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextDirection
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Constraints
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nags.operations.R
import com.nags.operations.ui.flights.FlightCardReportStatus

internal fun flightTypeBadgeLabel(isPerLanding: Boolean, isAdHoc: Boolean): String = when {
    isAdHoc -> "AH"
    isPerLanding -> "PL"
    else -> "S"
}

@Composable
internal fun flightTypeBadgeTextStyle(): TextStyle = MaterialTheme.typography.labelSmall.copy(
    fontSize = 9.sp, lineHeight = 11.sp, letterSpacing = 0.sp,
    fontWeight = FontWeight.Black, textDirection = TextDirection.Ltr,
)

internal val FlightTypeBadgeOverlap = 1.dp

@Composable
internal fun flightTypeBadgeDiameter(): Dp {
    val label = rememberTextMeasurer().measure("AH", flightTypeBadgeTextStyle(), maxLines = 1, softWrap = false)
    return with(LocalDensity.current) { maxOf(16.dp, maxOf(label.size.width, label.size.height).toDp() + 4.dp) }
}

/** Reserves the floating badge's bounds while anchoring it to the measured, possibly truncated number. */
@Composable
internal fun FlightNumberWithTypeBadge(number: String, isPerLanding: Boolean, isAdHoc: Boolean,
    style: TextStyle, modifier: Modifier = Modifier, onNumberPositioned: ((Float, Int) -> Unit)? = null) {
    Layout(modifier = modifier, content = {
        Text(number, Modifier.onGloballyPositioned {
            onNumberPositioned?.invoke(it.positionInWindow().y, it.size.height)
        }, style = style, maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis)
        FlightTypeBadge(isPerLanding, isAdHoc)
    }) { measurables, constraints ->
        val badge = measurables[1].measure(Constraints())
        val overlap = FlightTypeBadgeOverlap.roundToPx()
        val extraWidth = (badge.width - overlap).coerceAtLeast(0)
        val numberPlaceable = measurables[0].measure(constraints.copy(minWidth = 0, minHeight = 0,
            maxWidth = (constraints.maxWidth - extraWidth).coerceAtLeast(0)))
        val topInset = badge.height / 3
        val width = (numberPlaceable.width + extraWidth).coerceIn(constraints.minWidth, constraints.maxWidth)
        val height = maxOf(numberPlaceable.height + topInset, badge.height)
            .coerceIn(constraints.minHeight, constraints.maxHeight)
        layout(width, height) {
            numberPlaceable.place(0, topInset)
            badge.place((numberPlaceable.width - overlap).coerceAtLeast(0), 0)
        }
    }
}

/** Circular flight designation with its full name for accessibility. */
@Composable
internal fun FlightTypeBadge(isPerLanding: Boolean, isAdHoc: Boolean, modifier: Modifier = Modifier) {
    val description = stringResource(when {
        isAdHoc -> R.string.flights_tab_adhoc
        isPerLanding -> R.string.flights_per_landing
        else -> R.string.flights_scheduled
    })
    val dark = MaterialTheme.colorScheme.surface.luminance() < 0.5f
    val scheduledColors = flightStatusColors(FlightCardReportStatus.Scheduled)
    val (background, foreground) = when {
        isAdHoc -> if (dark) Color(0xFF482A2B) to Color(0xFFFFB4AE)
            else Color(0xFFFDECEB) to Color(0xFFA12622)
        isPerLanding -> if (dark) Color(0xFF49361D) to Color(0xFFFFD18E)
            else Color(0xFFFFF1DE) to Color(0xFF9A4500)
        else -> scheduledColors.background to scheduledColors.foreground
    }
    Surface(modifier.size(flightTypeBadgeDiameter()).clearAndSetSemantics { contentDescription = description }, shape = CircleShape,
        color = background, contentColor = foreground) {
        Box(contentAlignment = Alignment.Center) {
            Text(flightTypeBadgeLabel(isPerLanding, isAdHoc), style = flightTypeBadgeTextStyle(), maxLines = 1, softWrap = false)
        }
    }
}
