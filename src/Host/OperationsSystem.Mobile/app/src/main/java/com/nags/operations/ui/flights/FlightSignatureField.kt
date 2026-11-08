package com.nags.operations.ui.flights

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.nags.operations.R
import kotlin.math.roundToInt

/** Temporary normalized ink only; this field never creates a file or submits a signature. */
@Composable
internal fun FlightSignatureField(value: String, onOpen: () -> Unit) {
    val ink = remember(value) { readSignature(value) }
    val hasSignature = ink.strokes.isNotEmpty()
    val title = stringResource(R.string.operations_signature_title)
    val description = stringResource(if (hasSignature) R.string.operations_signature_edit else R.string.operations_signature_title)
    val state = stringResource(if (hasSignature) R.string.operations_signature_captured else R.string.operations_signature_add)
    val inkColor = MaterialTheme.colorScheme.onSurface
    Surface(onClick = onOpen, color = MaterialTheme.colorScheme.surface,
        modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp).semantics {
            contentDescription = description
            stateDescription = state
        }) {
        BoxWithConstraints(Modifier.fillMaxWidth().padding(horizontal = 14.dp, vertical = 6.dp), contentAlignment = Alignment.Center) {
            val labelMaxWidth = maxWidth * 0.48f
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp),
                verticalAlignment = Alignment.CenterVertically) {
                Text(title, Modifier.widthIn(max = labelMaxWidth), style = MaterialTheme.typography.bodyMedium,
                    fontWeight = FontWeight.Bold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Row(Modifier.weight(1f), horizontalArrangement = Arrangement.End, verticalAlignment = Alignment.CenterVertically) {
                    if (hasSignature) {
                        Canvas(Modifier.weight(1f, fill = false).width(88.dp).height(32.dp).clearAndSetSemantics {}) {
                            drawSignature(ink.strokes, ink.aspectRatio, inkColor, 1.dp.toPx())
                        }
                    } else {
                        FlightFormValueText(stringResource(R.string.operations_signature_add),
                            Modifier.weight(1f), isPlaceholder = true)
                    }
                    Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, null, Modifier.padding(start = 4.dp).size(18.dp),
                        tint = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        }
    }
}

@Composable
internal fun FlightSignatureSheet(value: String, onValueChange: (String) -> Unit, onDismiss: () -> Unit) {
    val ink = remember(value) { readSignature(value) }
    val latestInk by rememberUpdatedState(ink)
    val latestOnValueChange by rememberUpdatedState(onValueChange)
    var activeStroke by remember { mutableStateOf<List<Offset>>(emptyList()) }
    var activeAspect by remember { mutableFloatStateOf(2f) }
    val title = stringResource(R.string.operations_signature_title)
    val drawDescription = stringResource(R.string.operations_signature_draw)
    val capturedDescription = stringResource(R.string.operations_signature_captured)
    val addDescription = stringResource(R.string.operations_signature_add)
    val inkColor = MaterialTheme.colorScheme.onSurface
    FlightFormModalSheet(title, onDismiss) { close ->
        Column(Modifier.fillMaxWidth().padding(bottom = 8.dp)) {
            Row(Modifier.fillMaxWidth().padding(horizontal = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                Spacer(Modifier.size(48.dp))
                Text(title, Modifier.weight(1f), style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold,
                    textAlign = TextAlign.Center, maxLines = 2, overflow = TextOverflow.Ellipsis)
                IconButton(onClick = close, modifier = Modifier.size(48.dp)) {
                    Box(Modifier.size(30.dp).background(MaterialTheme.colorScheme.onSurface.copy(alpha = 0.045f), CircleShape),
                        contentAlignment = Alignment.Center) {
                        Icon(Icons.Outlined.Close, stringResource(R.string.flights_cancel), Modifier.size(18.dp))
                    }
                }
            }
            BoxWithConstraints(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp), contentAlignment = Alignment.Center) {
                // Reserve the footer before choosing the pad height in a short landscape sheet.
                val clearHeight = maxOf(48.dp, with(LocalDensity.current) { MaterialTheme.typography.bodyMedium.lineHeight.toDp() } + 16.dp)
                val canvasHeight = (maxWidth * 0.55f).coerceIn(160.dp, 200.dp)
                    .coerceAtMost((maxHeight - clearHeight).coerceAtLeast(64.dp))
                val shape = RoundedCornerShape(12.dp)
                Box(Modifier.fillMaxWidth().height(canvasHeight).clip(shape)
                    .background(MaterialTheme.colorScheme.surface)
                    .border(1.dp, MaterialTheme.colorScheme.outlineVariant, shape)) {
                    Canvas(Modifier.fillMaxSize().semantics {
                        contentDescription = drawDescription
                        stateDescription = if (ink.strokes.isNotEmpty()) capturedDescription else addDescription
                    }.pointerInput(Unit) {
                        awaitEachGesture {
                            // Take the gesture on touch-down so writing vertically never drags the sheet.
                            val down = awaitFirstDown(requireUnconsumed = false, pass = PointerEventPass.Initial)
                            down.consume()
                            val current = latestInk
                            val canvasSize = Size(size.width.toFloat(), size.height.toFloat())
                            val aspect = canvasSize.width / canvasSize.height
                            val fitted = signatureBounds(canvasSize, current.aspectRatio)
                            // Fit previous ink without distortion, then rebase it into this full canvas.
                            // New strokes can follow the finger through space exposed by a resize.
                            val previousStrokes = current.strokes.map { stroke ->
                                stroke.map { point ->
                                    Offset((fitted.left + point.x * fitted.width) / canvasSize.width,
                                        (fitted.top + point.y * fitted.height) / canvasSize.height)
                                }
                            }
                            val bounds = Rect(Offset.Zero, canvasSize)
                            val points = mutableListOf(normalizeSignaturePoint(down.position, bounds))
                            activeAspect = aspect
                            activeStroke = points.toList()
                            var completed = false
                            try {
                                while (true) {
                                    val event = awaitPointerEvent(PointerEventPass.Initial)
                                    val change = event.changes.firstOrNull { it.id == down.id } ?: break
                                    val point = normalizeSignaturePoint(change.position, bounds)
                                    if ((point - points.last()).getDistance() >= 0.001f) {
                                        points += point
                                        activeStroke = points.toList()
                                    }
                                    event.changes.forEach { it.consume() }
                                    if (!change.pressed) {
                                        completed = true
                                        break
                                    }
                                }
                                if (completed) {
                                    latestOnValueChange(writeSignature(SignatureInk(aspect, previousStrokes + listOf(points.toList()))))
                                }
                            } finally {
                                activeStroke = emptyList()
                            }
                        }
                    }) {
                        drawSignature(ink.strokes, ink.aspectRatio, inkColor, 2.5.dp.toPx())
                        if (activeStroke.isNotEmpty()) drawSignature(listOf(activeStroke), activeAspect, inkColor, 2.5.dp.toPx())
                    }
                    if (ink.strokes.isEmpty() && activeStroke.isEmpty()) {
                        Text(stringResource(R.string.operations_signature_hint), Modifier.align(Alignment.Center).clearAndSetSemantics {},
                            style = MaterialTheme.typography.bodyMedium, fontStyle = FontStyle.Italic,
                            color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                }
            }
            Row(Modifier.fillMaxWidth().padding(horizontal = 8.dp), horizontalArrangement = Arrangement.End) {
                TextButton(onClick = { onValueChange("") }, enabled = ink.strokes.isNotEmpty() && activeStroke.isEmpty(),
                    modifier = Modifier.heightIn(min = 48.dp)) {
                    Text(stringResource(R.string.operations_signature_clear), style = MaterialTheme.typography.bodyMedium,
                        fontWeight = FontWeight.SemiBold)
                }
            }
        }
    }
}

private data class SignatureInk(val aspectRatio: Float, val strokes: List<List<Offset>>)

private fun readSignature(value: String): SignatureInk {
    val parts = value.split('|')
    val aspect = parts.firstOrNull()?.toFloatOrNull()?.takeIf { it.isFinite() && it > 0f } ?: 2f
    val strokes = parts.drop(1).mapNotNull { stroke ->
        stroke.split(';').mapNotNull { point ->
            val coordinates = point.split(',')
            val x = coordinates.getOrNull(0)?.toIntOrNull()
            val y = coordinates.getOrNull(1)?.toIntOrNull()
            if (x != null && y != null && x in 0..10000 && y in 0..10000) Offset(x / 10000f, y / 10000f) else null
        }.takeIf { it.isNotEmpty() }
    }
    return SignatureInk(aspect, strokes)
}

private fun writeSignature(ink: SignatureInk): String = buildString {
    append(ink.aspectRatio)
    for (stroke in ink.strokes) {
        append('|')
        append(stroke.joinToString(";") { point -> "${(point.x * 10000).roundToInt()},${(point.y * 10000).roundToInt()}" })
    }
}

private fun signatureBounds(size: Size, aspectRatio: Float): Rect {
    val width = minOf(size.width, size.height * aspectRatio)
    val height = width / aspectRatio
    val left = (size.width - width) / 2f
    val top = (size.height - height) / 2f
    return Rect(left, top, left + width, top + height)
}

private fun normalizeSignaturePoint(point: Offset, bounds: Rect): Offset = Offset(
    ((point.x - bounds.left) / bounds.width).coerceIn(0f, 1f),
    ((point.y - bounds.top) / bounds.height).coerceIn(0f, 1f),
)

private fun DrawScope.drawSignature(strokes: List<List<Offset>>, aspectRatio: Float, color: Color, width: Float) {
    val bounds = signatureBounds(size, aspectRatio)
    fun project(point: Offset) = Offset(bounds.left + point.x * bounds.width, bounds.top + point.y * bounds.height)
    for (stroke in strokes) {
        if (stroke.size == 1) {
            drawCircle(color, radius = width / 2f, center = project(stroke.first()))
        } else {
            val path = Path().apply {
                val first = project(stroke.first())
                moveTo(first.x, first.y)
                for (index in 1 until stroke.size) {
                    val previous = project(stroke[index - 1])
                    val current = project(stroke[index])
                    quadraticTo(previous.x, previous.y, (previous.x + current.x) / 2f, (previous.y + current.y) / 2f)
                }
                val last = project(stroke.last())
                lineTo(last.x, last.y)
            }
            drawPath(path, color, style = Stroke(width, cap = StrokeCap.Round, join = StrokeJoin.Round))
        }
    }
}
