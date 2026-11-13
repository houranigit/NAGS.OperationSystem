package com.nags.operations.ui.flights

import android.graphics.BitmapFactory
import androidx.compose.foundation.Image
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.saveable.Saver
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import androidx.core.graphics.createBitmap
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
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
import androidx.compose.material.icons.automirrored.outlined.Redo
import androidx.compose.material.icons.automirrored.outlined.Undo
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.DeleteOutline
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
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
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.res.stringResource
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

/** Normalized ink stays in form state until the departure save encodes a PNG. */
@Composable
internal fun FlightSignatureField(value: String, hasSavedSignature: Boolean = false, savedPreview: ByteArray? = null,
    loading: Boolean = false, enabled: Boolean = true, onOpen: () -> Unit) {
    val ink = remember(value) { readSignature(value) }
    val preview = rememberSignatureBitmap(savedPreview)
    val hasSignature = ink.strokes.isNotEmpty() || hasSavedSignature
    val title = stringResource(R.string.operations_signature_title)
    val description = stringResource(if (hasSignature) R.string.operations_signature_edit else R.string.operations_signature_title)
    val state = stringResource(if (hasSignature) R.string.operations_signature_captured else R.string.operations_signature_add)
    val inkColor = MaterialTheme.colorScheme.onSurface
    FlightFormFieldSurface(onClick = onOpen, enabled = enabled,
        modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp).flightFormValidationFocus(enabled).semantics {
            contentDescription = description
            stateDescription = state
        }) {
        BoxWithConstraints(Modifier.fillMaxWidth().padding(horizontal = 14.dp, vertical = 6.dp), contentAlignment = Alignment.Center) {
            val labelMaxWidth = maxWidth * 0.48f
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp),
                verticalAlignment = Alignment.CenterVertically) {
                Text(title, Modifier.widthIn(max = labelMaxWidth), style = MaterialTheme.typography.bodyMedium,
                    fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 1, overflow = TextOverflow.Ellipsis)
                Row(Modifier.weight(1f), horizontalArrangement = Arrangement.End, verticalAlignment = Alignment.CenterVertically) {
                    if (ink.strokes.isNotEmpty()) {
                        Canvas(Modifier.weight(1f, fill = false).width(88.dp).height(32.dp).clearAndSetSemantics {}) {
                            drawSignature(ink.strokes, ink.aspectRatio, inkColor, 1.dp.toPx())
                        }
                    } else if (preview != null) {
                        Image(preview, null, Modifier.width(88.dp).height(32.dp).background(Color.White).clearAndSetSemantics {}, contentScale = ContentScale.Fit)
                    } else if (loading) {
                        CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp)
                    } else {
                        FlightFormValueText(stringResource(if (hasSavedSignature) R.string.operations_signature_captured else R.string.flight_form_required_field),
                            Modifier.weight(1f), isPlaceholder = !hasSavedSignature)
                    }
                    Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, null, Modifier.padding(start = 4.dp).size(18.dp),
                        tint = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        }
    }
}

private val SignatureSessionSaver = Saver<SignatureEditSession, String>(
    save = { Json.encodeToString(it) }, restore = { Json.decodeFromString<SignatureEditSession>(it) })

@Composable
internal fun rememberSignatureBitmap(bytes: ByteArray?): ImageBitmap? = remember(bytes) {
    if (bytes == null) null else runCatching {
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeByteArray(bytes, 0, bytes.size, bounds)
        if (bounds.outWidth <= 0 || bounds.outHeight <= 0) null else {
            val options = BitmapFactory.Options().apply {
                inSampleSize = signaturePreviewSampleSize(bounds.outWidth, bounds.outHeight)
            }
            BitmapFactory.decodeByteArray(bytes, 0, bytes.size, options)?.asImageBitmap()
        }
    }.getOrNull()
}

@Composable
internal fun FlightSignatureSheet(value: String, hasSavedSignature: Boolean, savedPreview: ByteArray?, loading: Boolean,
    onRetry: () -> Unit, onValueChange: (String) -> Unit, onDismiss: () -> Unit) {
    // Pin the original and keep all replacement strokes local until Save. Late image loading must
    // never replace the user's draft or reset Undo history.
    var session by rememberSaveable(stateSaver = SignatureSessionSaver) {
        mutableStateOf(SignatureEditSession(value, value.isNotBlank() || hasSavedSignature))
    }
    var originalPreview by remember { mutableStateOf(savedPreview) }
    LaunchedEffect(savedPreview) { if (originalPreview == null) originalPreview = savedPreview }
    val preview = rememberSignatureBitmap(originalPreview)
    val ink = remember(session.displayedInk) { readSignature(session.displayedInk) }
    val latestInk by rememberUpdatedState(ink)
    val latestDraw by rememberUpdatedState<(String) -> Unit>({ session = session.draw(it) })
    var activeStroke by remember { mutableStateOf<List<Offset>>(emptyList()) }
    var activeAspect by remember { mutableFloatStateOf(2f) }
    var committing by remember { mutableStateOf(false) }
    val title = stringResource(R.string.operations_signature_title)
    val drawDescription = stringResource(R.string.operations_signature_draw)
    val capturedDescription = stringResource(R.string.operations_signature_captured)
    val addDescription = stringResource(R.string.operations_signature_add)
    val inkColor = MaterialTheme.colorScheme.onSurface
    val baselineColor = MaterialTheme.colorScheme.outlineVariant
    FlightFormModalSheet(title, onDismiss) { close ->
        Column(Modifier.fillMaxWidth().verticalScroll(rememberScrollState()).padding(bottom = 8.dp)) {
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
            Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp).heightIn(min = 36.dp),
                horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                Text(stringResource(if (session.viewingOriginal) R.string.operations_signature_saved else R.string.operations_signature_instructions),
                    Modifier.weight(1f), style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant)
                if (session.viewingOriginal) TextButton(onClick = { session = session.replace() }, enabled = !committing,
                    contentPadding = PaddingValues(horizontal = 8.dp, vertical = 6.dp)) {
                    Text(stringResource(R.string.operations_signature_replace), style = MaterialTheme.typography.bodySmall,
                        fontWeight = FontWeight.Medium)
                }
            }
            BoxWithConstraints(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp), contentAlignment = Alignment.Center) {
                val canvasHeight = (maxWidth * 0.55f).coerceIn(160.dp, 200.dp)
                val shape = RoundedCornerShape(16.dp)
                Box(Modifier.fillMaxWidth().height(canvasHeight).clip(shape)
                    .background(MaterialTheme.colorScheme.surface)
                    .border(1.dp, MaterialTheme.colorScheme.outlineVariant, shape), contentAlignment = Alignment.Center) {
                    if (session.viewingOriginal && ink.strokes.isEmpty()) {
                        when {
                            preview != null -> Image(preview, null, Modifier.fillMaxSize().padding(8.dp).background(Color.White), contentScale = ContentScale.Fit)
                            loading -> CircularProgressIndicator(Modifier.size(28.dp))
                            else -> Column(Modifier.padding(16.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                                Text(stringResource(R.string.operations_signature_preview_unavailable),
                                    style = MaterialTheme.typography.bodyMedium, textAlign = TextAlign.Center,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant)
                                TextButton(onClick = onRetry) { Text(stringResource(R.string.operations_signature_retry)) }
                            }
                        }
                    } else {
                        Canvas(Modifier.fillMaxSize().semantics {
                            contentDescription = if (session.viewingOriginal) capturedDescription else drawDescription
                            stateDescription = if (ink.strokes.isNotEmpty()) capturedDescription else addDescription
                        }.pointerInput(session.viewingOriginal, committing) {
                            if (session.viewingOriginal || committing) return@pointerInput
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
                                        latestDraw(writeSignature(SignatureInk(aspect, previousStrokes + listOf(points.toList()))))
                                    }
                                } finally {
                                    activeStroke = emptyList()
                                }
                            }
                        }) {
                            drawLine(baselineColor, Offset(16.dp.toPx(), size.height - 28.dp.toPx()),
                                Offset(size.width - 16.dp.toPx(), size.height - 28.dp.toPx()), strokeWidth = 1.dp.toPx())
                            drawSignature(ink.strokes, ink.aspectRatio, inkColor, 2.5.dp.toPx())
                            if (activeStroke.isNotEmpty()) drawSignature(listOf(activeStroke), activeAspect, inkColor, 2.5.dp.toPx())
                        }
                        if (!session.viewingOriginal && ink.strokes.isEmpty() && activeStroke.isEmpty()) {
                            Text(stringResource(R.string.operations_signature_hint), Modifier.clearAndSetSemantics {},
                                style = MaterialTheme.typography.bodyMedium, fontStyle = FontStyle.Italic,
                                color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                    }
                }
            }
            Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                SignatureToolbarButton(Icons.AutoMirrored.Outlined.Undo, stringResource(R.string.operations_signature_undo),
                    enabled = session.history.isNotEmpty() && activeStroke.isEmpty() && !committing,
                    modifier = Modifier.weight(1f), onClick = { session = session.undo() })
                SignatureToolbarButton(Icons.AutoMirrored.Outlined.Redo, stringResource(R.string.operations_signature_redo),
                    enabled = session.redoHistory.isNotEmpty() && activeStroke.isEmpty() && !committing,
                    modifier = Modifier.weight(1f), onClick = { session = session.redo() })
                SignatureToolbarButton(Icons.Outlined.DeleteOutline, stringResource(R.string.operations_signature_clear_action),
                    enabled = !session.viewingOriginal && ink.strokes.isNotEmpty() && activeStroke.isEmpty() && !committing,
                    modifier = Modifier.weight(1f), onClick = { session = session.clear() })
            }
            Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp),
                horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                OutlinedButton(onClick = close, modifier = Modifier.weight(1f).heightIn(min = 48.dp),
                    shape = RoundedCornerShape(12.dp), contentPadding = PaddingValues(horizontal = 12.dp, vertical = 10.dp)) {
                    Text(stringResource(R.string.flights_cancel), style = MaterialTheme.typography.labelLarge,
                        maxLines = 2, overflow = TextOverflow.Ellipsis)
                }
                Button(onClick = {
                    committing = true
                    onValueChange(session.draft)
                    close()
                }, modifier = Modifier.weight(1.4f).heightIn(min = 48.dp), shape = RoundedCornerShape(12.dp),
                    enabled = session.hasChanges && ink.strokes.isNotEmpty() && activeStroke.isEmpty() && !committing,
                    contentPadding = PaddingValues(horizontal = 12.dp, vertical = 10.dp)) {
                    Text(stringResource(R.string.operations_signature_save), style = MaterialTheme.typography.labelLarge,
                        fontWeight = FontWeight.SemiBold, maxLines = 2, overflow = TextOverflow.Ellipsis)
                }
            }
        }
    }
}

@Composable
private fun SignatureToolbarButton(icon: ImageVector, label: String, enabled: Boolean,
    modifier: Modifier = Modifier, onClick: () -> Unit) {
    TextButton(onClick = onClick, enabled = enabled, modifier = modifier.heightIn(min = 48.dp),
        colors = ButtonDefaults.textButtonColors(contentColor = MaterialTheme.colorScheme.onSurface),
        contentPadding = PaddingValues(horizontal = 8.dp, vertical = 6.dp)) {
        Icon(icon, null, Modifier.size(16.dp))
        Spacer(Modifier.width(6.dp))
        Text(label, Modifier.weight(1f, fill = false), style = MaterialTheme.typography.bodySmall,
            fontWeight = FontWeight.Medium, maxLines = 1, overflow = TextOverflow.Ellipsis)
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

/** Encode the same normalized strokes as the preview; metadata and file ownership stay server-side. */
internal fun signaturePngContent(value: String): String? {
    val ink = readSignature(value)
    if (ink.strokes.isEmpty()) return null
    val width = 1024
    val height = (width / ink.aspectRatio).roundToInt().coerceIn(256, 1024)
    val bitmap = createBitmap(width, height)
    try {
        val canvas = android.graphics.Canvas(bitmap)
        canvas.drawColor(android.graphics.Color.WHITE)
        val paint = android.graphics.Paint(android.graphics.Paint.ANTI_ALIAS_FLAG).apply {
            color = android.graphics.Color.BLACK
            strokeWidth = 4f
            strokeCap = android.graphics.Paint.Cap.ROUND
            strokeJoin = android.graphics.Paint.Join.ROUND
            style = android.graphics.Paint.Style.STROKE
        }
        val bounds = signatureBounds(Size(width.toFloat(), height.toFloat()), ink.aspectRatio)
        fun project(point: Offset) = Offset(bounds.left + point.x * bounds.width, bounds.top + point.y * bounds.height)
        for (stroke in ink.strokes) {
            if (stroke.size == 1) {
                val point = project(stroke.first())
                canvas.drawPoint(point.x, point.y, paint)
            } else {
                val path = android.graphics.Path()
                val first = project(stroke.first())
                path.moveTo(first.x, first.y)
                for (index in 1 until stroke.size) {
                    val previous = project(stroke[index - 1])
                    val current = project(stroke[index])
                    path.quadTo(previous.x, previous.y, (previous.x + current.x) / 2f, (previous.y + current.y) / 2f)
                }
                val last = project(stroke.last())
                path.lineTo(last.x, last.y)
                canvas.drawPath(path, paint)
            }
        }
        return java.io.ByteArrayOutputStream().use { output ->
            check(bitmap.compress(android.graphics.Bitmap.CompressFormat.PNG, 100, output)) { "Could not prepare the customer signature." }
            android.util.Base64.encodeToString(output.toByteArray(), android.util.Base64.NO_WRAP)
        }
    } finally { bitmap.recycle() }
}
