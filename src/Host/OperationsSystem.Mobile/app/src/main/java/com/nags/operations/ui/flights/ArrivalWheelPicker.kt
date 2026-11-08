package com.nags.operations.ui.flights

import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.snapping.SnapPosition
import androidx.compose.foundation.gestures.snapping.rememberSnapFlingBehavior
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshotFlow
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.MotionDurationScale
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.CompositingStrategy
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.nestedscroll.NestedScrollConnection
import androidx.compose.ui.input.nestedscroll.NestedScrollSource
import androidx.compose.ui.input.nestedscroll.nestedScroll
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.platform.LocalLocale
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.ProgressBarRangeInfo
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.progressBarRangeInfo
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.setProgress
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.text.style.TextMotion
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.Velocity
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nags.operations.R
import java.text.NumberFormat
import java.time.LocalDateTime
import java.time.Month
import java.time.YearMonth
import java.time.format.TextStyle
import kotlin.math.abs
import kotlin.math.roundToInt
import kotlin.math.sin
import androidx.compose.ui.text.TextStyle as ComposeTextStyle

/** Five-row date and 24-hour time wheels for the form's compact ATA sheet. */
@Composable
internal fun ArrivalWheelPicker(
    value: LocalDateTime,
    referenceYear: Int,
    onValueChange: (LocalDateTime) -> Unit,
) {
    val locale = LocalLocale.current.platformLocale
    val density = LocalDensity.current
    val numbers = remember(locale) { NumberFormat.getIntegerInstance(locale).apply { isGroupingUsed = false } }
    val timeNumbers = remember(locale) { NumberFormat.getIntegerInstance(locale).apply {
        isGroupingUsed = false
        minimumIntegerDigits = 2
    } }
    val years = remember(referenceYear, locale) { (referenceYear - 1..referenceYear + 1).map(numbers::format) }
    val months = remember(locale) { Month.values().map { it.getDisplayName(TextStyle.SHORT, locale) } }
    val days = remember(value.toLocalDate().lengthOfMonth(), locale) {
        (1..value.toLocalDate().lengthOfMonth()).map(numbers::format)
    }
    val hours = remember(locale) { (0..23).map(timeNumbers::format) }
    val minutes = remember(locale) { (0..59).map(timeNumbers::format) }
    val textStyle = MaterialTheme.typography.bodyLarge.copy(fontSize = 17.sp, lineHeight = 22.sp,
        letterSpacing = 0.sp, fontWeight = FontWeight.Bold, fontFeatureSettings = "tnum",
        textMotion = TextMotion.Animated)
    // Five readable rows remain compact at normal text sizes and grow with accessibility text.
    val rowHeight = maxOf(32.dp, with(density) { 22.sp.toDp() } + 4.dp)
    val columnWidths = listOf(years, months, days, hours, minutes).map { arrivalWheelMinimumWidth(it, textStyle) }
    val groupGap = 12.dp
    val colonWidth = arrivalWheelMinimumWidth(listOf(":"), textStyle, minimumWidth = 0.dp, horizontalPadding = 0.dp)
    val minimumWidth = columnWidths.fold(0.dp) { width, column -> width + column } + groupGap + colonWidth
    // Keep date and time ordering stable while retaining localized month names and digits.
    CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Ltr) {
        Box(Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
            // Keep the groups close even on a wide sheet, without shrinking their 48dp targets.
            // Only unusually large text needs horizontal scrolling.
            Box(Modifier.widthIn(max = minimumWidth).horizontalScroll(rememberScrollState())) {
                Row(Modifier.width(minimumWidth), verticalAlignment = Alignment.CenterVertically) {
                    ArrivalWheelGroup(rowHeight) {
                        ArrivalNumberWheel(stringResource(R.string.arrival_year), years,
                            (value.year - referenceYear + 1).coerceIn(years.indices), rowHeight, textStyle,
                            onSelect = { onValueChange(arrivalDateInMonth(value, referenceYear - 1 + it, value.monthValue)) },
                            modifier = Modifier.width(columnWidths[0]))
                        ArrivalNumberWheel(stringResource(R.string.arrival_month), months, value.monthValue - 1,
                            rowHeight, textStyle,
                            onSelect = { onValueChange(arrivalDateInMonth(value, value.year, it + 1)) },
                            modifier = Modifier.width(columnWidths[1]))
                        ArrivalNumberWheel(stringResource(R.string.arrival_day), days, value.dayOfMonth - 1,
                            rowHeight, textStyle, onSelect = { onValueChange(value.withDayOfMonth(it + 1)) },
                            modifier = Modifier.width(columnWidths[2]))
                    }
                    Spacer(Modifier.width(groupGap))
                    ArrivalWheelGroup(rowHeight) {
                        ArrivalNumberWheel(stringResource(R.string.arrival_hour), hours, value.hour,
                            rowHeight, textStyle, onSelect = { onValueChange(value.withHour(it)) },
                            modifier = Modifier.width(columnWidths[3]), contentAlignment = Alignment.CenterEnd)
                        Box(Modifier.width(colonWidth).height(rowHeight * 5), contentAlignment = Alignment.Center) {
                            Text(":", Modifier.clearAndSetSemantics {}, style = textStyle,
                                color = MaterialTheme.colorScheme.onSurface)
                        }
                        ArrivalNumberWheel(stringResource(R.string.arrival_minute), minutes, value.minute,
                            rowHeight, textStyle, onSelect = { onValueChange(value.withMinute(it)) },
                            modifier = Modifier.width(columnWidths[4]), contentAlignment = Alignment.CenterStart)
                    }
                }
            }
        }
    }
}

@Composable
private fun ArrivalWheelGroup(rowHeight: Dp, content: @Composable () -> Unit) {
    Box(contentAlignment = Alignment.Center) {
        Box(Modifier.matchParentSize(), contentAlignment = Alignment.Center) {
            Box(Modifier.fillMaxWidth().height(rowHeight)
                .background(MaterialTheme.colorScheme.onSurface.copy(alpha = 0.045f), RoundedCornerShape(8.dp)))
        }
        Row(verticalAlignment = Alignment.CenterVertically) { content() }
    }
}

@Composable
private fun arrivalWheelMinimumWidth(
    values: List<String>,
    textStyle: ComposeTextStyle,
    minimumWidth: Dp = 48.dp,
    horizontalPadding: Dp = 8.dp,
): Dp {
    val measurer = rememberTextMeasurer()
    val density = LocalDensity.current
    val textWidth = remember(values, textStyle, measurer) {
        values.maxOf { measurer.measure(it, style = textStyle).size.width }
    }
    return maxOf(minimumWidth, with(density) { textWidth.toDp() } + horizontalPadding)
}

@Composable
private fun ArrivalNumberWheel(
    label: String,
    values: List<String>,
    selectedIndex: Int,
    rowHeight: Dp,
    textStyle: ComposeTextStyle,
    onSelect: (Int) -> Unit,
    modifier: Modifier,
    contentAlignment: Alignment = Alignment.Center,
) {
    val motionDurationScale = rememberCoroutineScope().coroutineContext[MotionDurationScale]
    val rowPixels = with(LocalDensity.current) { rowHeight.toPx() }
    val currentOnSelect by rememberUpdatedState(onSelect)
    val currentSelectedIndex by rememberUpdatedState(selectedIndex)
    val listState = rememberLazyListState(initialFirstVisibleItemIndex = selectedIndex)
    val centeredIndex by remember(listState, values.size) {
        derivedStateOf {
            val layout = listState.layoutInfo
            val center = (layout.viewportStartOffset + layout.viewportEndOffset) / 2
            (layout.visibleItemsInfo.minByOrNull { abs(it.offset + it.size / 2 - center) }?.index
                ?: listState.firstVisibleItemIndex).coerceIn(values.indices)
        }
    }
    var aligningExternalValue by remember { mutableStateOf(false) }
    LaunchedEffect(selectedIndex, values.size) {
        if (!listState.isScrollInProgress && centeredIndex != selectedIndex) {
            aligningExternalValue = true
            try {
                listState.scrollToItem(selectedIndex)
            } finally {
                aligningExternalValue = false
            }
        }
    }
    LaunchedEffect(listState, values.size) {
        var trackingUserMotion = false
        snapshotFlow { Triple(listState.isScrollInProgress, centeredIndex, aligningExternalValue) }
            .collect { (scrolling, index, aligning) ->
                if (aligning) {
                    trackingUserMotion = false
                } else {
                    if (scrolling) trackingUserMotion = true
                    // Feed the form while dragging and flinging, without fighting its external updates.
                    if (trackingUserMotion && index != currentSelectedIndex) currentOnSelect(index)
                    if (!scrolling) trackingUserMotion = false
                }
            }
    }
    val keepWheelGesture = remember {
        object : NestedScrollConnection {
            // A wheel at its limit should stop there rather than pull the surrounding modal down.
            override fun onPostScroll(consumed: Offset, available: Offset, source: NestedScrollSource): Offset =
                Offset(0f, available.y)

            override suspend fun onPostFling(consumed: Velocity, available: Velocity): Velocity =
                Velocity(0f, available.y)
        }
    }
    LazyColumn(state = listState,
        modifier = modifier.widthIn(min = 48.dp).height(rowHeight * 5).nestedScroll(keepWheelGesture)
            .semantics {
                contentDescription = label
                stateDescription = values[centeredIndex]
                progressBarRangeInfo = ProgressBarRangeInfo(centeredIndex.toFloat(), 0f..values.lastIndex.toFloat(),
                    steps = (values.size - 2).coerceAtLeast(0))
                setProgress { requested ->
                    val index = requested.roundToInt().coerceIn(values.indices)
                    if (index != currentSelectedIndex) currentOnSelect(index)
                    true
                }
            },
        contentPadding = PaddingValues(vertical = rowHeight * 2),
        flingBehavior = rememberSnapFlingBehavior(listState, SnapPosition.Center),
        horizontalAlignment = Alignment.CenterHorizontally) {
        itemsIndexed(values) { index, text ->
            Box(Modifier.fillMaxWidth().height(rowHeight).clearAndSetSemantics {}
                .graphicsLayer {
                    // Read scroll position in the layer: transforms follow the finger each frame
                    // without recomposing or laying out the text for every fractional movement.
                    val layout = listState.layoutInfo
                    val item = layout.visibleItemsInfo.firstOrNull { it.index == index }
                    val center = (layout.viewportStartOffset + layout.viewportEndOffset) / 2f
                    val distance = if (item == null) (index - centeredIndex).toFloat()
                        else (item.offset + item.size / 2f - center) / rowPixels
                    val absoluteDistance = abs(distance)
                    val edgeFade = ((3f - absoluteDistance) / 0.5f).coerceIn(0f, 1f)
                    alpha = (1f - 0.175f * absoluteDistance).coerceIn(0.65f, 1f) * edgeFade
                    compositingStrategy = CompositingStrategy.ModulateAlpha
                    if (motionDurationScale?.scaleFactor == 0f) {
                        // Remove animations keeps direct scrolling and readable emphasis, without 3D.
                        rotationX = 0f
                        translationY = 0f
                        scaleX = 1f
                        scaleY = 1f
                    } else {
                        val radiansPerRow = (Math.PI / 9).toFloat()
                        val angle = distance.coerceIn(-2.5f, 2.5f) * radiansPerRow
                        rotationX = -angle * (180f / Math.PI.toFloat())
                        translationY = sin(angle) * (rowPixels / radiansPerRow) - distance * rowPixels
                        scaleX = 1f - 0.055f * absoluteDistance.coerceAtMost(2f)
                        scaleY = 1f
                        cameraDistance = rowPixels * 8f
                    }
                }, contentAlignment = contentAlignment) {
                // Fixed bold glyphs avoid a weight jump as a row crosses the selection band.
                Text(text, Modifier.padding(horizontal = 4.dp), style = textStyle,
                    maxLines = 1, softWrap = false, overflow = TextOverflow.Clip,
                    color = MaterialTheme.colorScheme.onSurface)
            }
        }
    }
}

/** Retain time and the chosen day when possible; clamp to the target month's final day. */
internal fun arrivalDateInMonth(value: LocalDateTime, year: Int, month: Int): LocalDateTime {
    val target = YearMonth.of(year, month)
    return target.atDay(value.dayOfMonth.coerceAtMost(target.lengthOfMonth())).atTime(value.toLocalTime())
}
