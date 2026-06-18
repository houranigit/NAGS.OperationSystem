package com.nags.operations.ui.components

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Paint
import android.util.Base64
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Brush
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Draw
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.asAndroidBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.IntSize
import androidx.compose.ui.unit.dp
import java.io.ByteArrayOutputStream

/**
 * Optional customer signature — matches [com.operations.mobile.ui.components.SignaturePad]
 * UX; PNG is stored as Base64 for form state until submit wires the backend.
 */
@Composable
fun SignatureField(
    signaturePng: String?,
    onChange: (String?) -> Unit,
    modifier: Modifier = Modifier,
) {
    var dialogOpen by remember { mutableStateOf(false) }
    val bitmap = remember(signaturePng) { decodeBase64ToImageBitmap(signaturePng) }

    Surface(
        modifier = modifier.fillMaxWidth(),
        shape = RoundedCornerShape(14.dp),
        color = MaterialTheme.colorScheme.surface,
        border = BorderStroke(
            1.dp,
            MaterialTheme.colorScheme.outline.copy(alpha = 0.4f),
        ),
    ) {
        Column(modifier = Modifier.padding(14.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box(
                    modifier = Modifier
                        .size(36.dp)
                        .background(
                            MaterialTheme.colorScheme.primary.copy(alpha = 0.12f),
                            RoundedCornerShape(10.dp),
                        ),
                    contentAlignment = Alignment.Center,
                ) {
                    Icon(
                        Icons.Default.Draw,
                        contentDescription = null,
                        tint = MaterialTheme.colorScheme.primary,
                    )
                }
                Spacer(Modifier.size(10.dp))
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        "Customer signature",
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = FontWeight.SemiBold,
                    )
                    Text(
                        text = if (bitmap != null) {
                            "Captured. Tap to recapture or clear."
                        } else {
                            "Optional — tap Add signature when the customer has signed."
                        },
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
                if (bitmap != null) {
                    TextButton(onClick = { onChange(null) }) {
                        Icon(Icons.Default.Delete, contentDescription = null, modifier = Modifier.size(18.dp))
                        Spacer(Modifier.size(4.dp))
                        Text("Clear")
                    }
                }
            }
            if (bitmap != null) {
                Spacer(Modifier.size(10.dp))
                Surface(
                    modifier = Modifier
                        .fillMaxWidth()
                        .aspectRatio(2.5f),
                    shape = RoundedCornerShape(10.dp),
                    color = Color.White,
                    border = BorderStroke(
                        1.dp,
                        MaterialTheme.colorScheme.outline.copy(alpha = 0.3f),
                    ),
                ) {
                    Image(
                        bitmap = bitmap,
                        contentDescription = "Captured signature",
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(8.dp),
                    )
                }
            }
            Spacer(Modifier.size(10.dp))
            Button(
                onClick = { dialogOpen = true },
                modifier = Modifier
                    .fillMaxWidth()
                    .height(46.dp),
                shape = RoundedCornerShape(12.dp),
            ) {
                Icon(
                    Icons.Default.Brush,
                    contentDescription = null,
                    modifier = Modifier.size(18.dp),
                )
                Spacer(Modifier.size(8.dp))
                Text(
                    if (bitmap != null) "Recapture signature" else "Add signature",
                    fontWeight = FontWeight.SemiBold,
                )
            }
        }
    }

    if (dialogOpen) {
        SignaturePadDialog(
            onDismiss = { dialogOpen = false },
            onConfirm = { png ->
                onChange(png)
                dialogOpen = false
            },
        )
    }
}

@Composable
private fun SignaturePadDialog(
    onDismiss: () -> Unit,
    onConfirm: (String) -> Unit,
) {
    val strokes = remember { mutableStateListOf<List<Offset>>() }
    var currentStroke by remember { mutableStateOf<List<Offset>>(emptyList()) }
    var canvasSize by remember { mutableStateOf(IntSize.Zero) }

    val strokeColor = MaterialTheme.colorScheme.onSurface

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text("Customer signature", fontWeight = FontWeight.SemiBold)
        },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Text(
                    "Ask the customer to sign in the box below using their finger or a stylus.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                Surface(
                    shape = RoundedCornerShape(12.dp),
                    color = Color.White,
                    border = BorderStroke(
                        1.5.dp,
                        MaterialTheme.colorScheme.outline.copy(alpha = 0.6f),
                    ),
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(220.dp),
                ) {
                    Canvas(
                        modifier = Modifier
                            .fillMaxWidth()
                            .height(220.dp)
                            .pointerInput(Unit) {
                                detectDragGestures(
                                    onDragStart = { offset ->
                                        currentStroke = listOf(offset)
                                    },
                                    onDrag = { change, _ ->
                                        currentStroke = currentStroke + change.position
                                    },
                                    onDragEnd = {
                                        if (currentStroke.size > 1) strokes.add(currentStroke.toList())
                                        currentStroke = emptyList()
                                    },
                                    onDragCancel = { currentStroke = emptyList() },
                                )
                            },
                    ) {
                        canvasSize = IntSize(size.width.toInt(), size.height.toInt())
                        val style = Stroke(
                            width = 4.dp.toPx(),
                            cap = StrokeCap.Round,
                            join = StrokeJoin.Round,
                        )
                        for (stroke in strokes) drawStrokePath(stroke, strokeColor, style)
                        if (currentStroke.size > 1) drawStrokePath(currentStroke, strokeColor, style)
                    }
                }
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    val png = renderStrokesToBase64Png(
                        strokes = strokes.toList(),
                        widthPx = canvasSize.width.coerceAtLeast(1),
                        heightPx = canvasSize.height.coerceAtLeast(1),
                        strokeColor = strokeColor.toArgb(),
                        background = android.graphics.Color.WHITE,
                    )
                    onConfirm(png)
                },
                enabled = strokes.isNotEmpty(),
            ) {
                Icon(Icons.Default.CheckCircle, contentDescription = null, modifier = Modifier.size(18.dp))
                Spacer(Modifier.size(6.dp))
                Text("Save")
            }
        },
        dismissButton = {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedButton(
                    onClick = {
                        strokes.clear()
                        currentStroke = emptyList()
                    },
                    enabled = strokes.isNotEmpty(),
                ) {
                    Icon(Icons.Default.Delete, contentDescription = null, modifier = Modifier.size(16.dp))
                    Spacer(Modifier.size(4.dp))
                    Text("Clear")
                }
                TextButton(onClick = onDismiss) { Text("Cancel") }
            }
        },
    )
}

private fun DrawScope.drawStrokePath(
    points: List<Offset>,
    color: Color,
    style: Stroke,
) {
    if (points.size < 2) return
    val path = Path().apply {
        moveTo(points.first().x, points.first().y)
        for (i in 1 until points.size) {
            lineTo(points[i].x, points[i].y)
        }
    }
    drawPath(path = path, color = color, style = style)
}

private fun renderStrokesToBase64Png(
    strokes: List<List<Offset>>,
    widthPx: Int,
    heightPx: Int,
    strokeColor: Int,
    background: Int,
): String {
    val bitmap = Bitmap.createBitmap(widthPx, heightPx, Bitmap.Config.ARGB_8888)
    val canvas = android.graphics.Canvas(bitmap)
    canvas.drawColor(background)
    val paint = Paint().apply {
        color = strokeColor
        isAntiAlias = true
        style = Paint.Style.STROKE
        strokeWidth = 6f
        strokeCap = Paint.Cap.ROUND
        strokeJoin = Paint.Join.ROUND
    }
    for (stroke in strokes) {
        if (stroke.size < 2) continue
        val path = android.graphics.Path()
        path.moveTo(stroke.first().x, stroke.first().y)
        for (i in 1 until stroke.size) path.lineTo(stroke[i].x, stroke[i].y)
        canvas.drawPath(path, paint)
    }
    val output = ByteArrayOutputStream()
    bitmap.compress(Bitmap.CompressFormat.PNG, 100, output)
    bitmap.recycle()
    return Base64.encodeToString(output.toByteArray(), Base64.NO_WRAP)
}

private fun decodeBase64ToImageBitmap(base64: String?): androidx.compose.ui.graphics.ImageBitmap? {
    if (base64.isNullOrBlank()) return null
    return runCatching {
        val bytes = Base64.decode(base64, Base64.DEFAULT)
        val bmp = BitmapFactory.decodeByteArray(bytes, 0, bytes.size) ?: return null
        bmp.asImageBitmap()
    }.getOrNull()
}
