package com.nags.operations.ui.flights

import android.graphics.Bitmap
import androidx.compose.foundation.Image
import androidx.compose.foundation.gestures.rememberTransformableState
import androidx.compose.foundation.gestures.transformable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.ArrowForward
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.Download
import androidx.compose.material.icons.outlined.IosShare
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.SideEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.isSpecified
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import androidx.compose.ui.window.DialogWindowProvider
import androidx.core.view.WindowCompat
import com.nags.operations.R
import com.nags.operations.ui.theme.isDark
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.launch

@Composable
internal fun FlightWorkOrderPreview(
    flightNumber: String,
    renderer: FlightWorkOrderPdfRenderer,
    pageCount: Int,
    busy: Boolean,
    saving: Boolean,
    actionError: Int?,
    onDismiss: () -> Unit,
    onSave: () -> Unit,
    onShare: () -> Unit,
    onReload: () -> Unit,
) {
    val pager = rememberPagerState { pageCount }
    val scope = rememberCoroutineScope()
    val currentPage = pager.currentPage
    var renderedPage by remember(renderer) { mutableIntStateOf(-1) }
    val actionEnabled = renderedPage == currentPage && !busy
    val colors = MaterialTheme.colorScheme
    Dialog(
        onDismissRequest = onDismiss,
        properties = DialogProperties(usePlatformDefaultWidth = false, decorFitsSystemWindows = false),
    ) {
        val view = LocalView.current
        val window = (view.parent as? DialogWindowProvider)?.window
        SideEffect {
            window?.let { WindowCompat.getInsetsController(it, view).isAppearanceLightStatusBars = !colors.isDark }
        }
        Surface(modifier = Modifier.fillMaxSize(), color = colors.surface) {
            Column(Modifier.fillMaxSize().safeDrawingPadding()) {
                Row(Modifier.fillMaxWidth().padding(horizontal = 8.dp, vertical = 4.dp),
                    verticalAlignment = Alignment.CenterVertically) {
                    IconButton(onClick = onDismiss, enabled = !busy) {
                        Icon(Icons.Outlined.Close, stringResource(R.string.flight_wo_close_preview))
                    }
                    Column(Modifier.weight(1f).padding(horizontal = 8.dp)) {
                        Text(stringResource(R.string.flight_wo_preview), Modifier.semantics { heading() },
                            style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                        Text(flightNumber, style = MaterialTheme.typography.bodySmall, color = colors.onSurfaceVariant,
                            maxLines = 1, overflow = TextOverflow.Ellipsis)
                    }
                }
                HorizontalDivider()
                Surface(Modifier.weight(1f).fillMaxWidth(), color = colors.surfaceContainerHighest) {
                    HorizontalPager(
                        state = pager,
                        contentPadding = PaddingValues(horizontal = 12.dp, vertical = 8.dp),
                        pageSpacing = 8.dp,
                    ) { index ->
                        WorkOrderPdfPage(renderer, index, pageCount, index == currentPage,
                            onReady = { ready -> renderedPage = if (ready) index else -1 },
                            onReload = onReload)
                    }
                }
                HorizontalDivider()
                if (pageCount > 1) {
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.Center,
                        verticalAlignment = Alignment.CenterVertically) {
                        IconButton(enabled = currentPage > 0, onClick = {
                            scope.launch { pager.animateScrollToPage(currentPage - 1) }
                        }) {
                            Icon(Icons.AutoMirrored.Filled.ArrowBack, stringResource(R.string.flight_wo_previous_page))
                        }
                        Text(stringResource(R.string.flight_wo_page, currentPage + 1, pageCount),
                            Modifier.padding(horizontal = 8.dp), style = MaterialTheme.typography.labelLarge)
                        IconButton(enabled = currentPage < pageCount - 1, onClick = {
                            scope.launch { pager.animateScrollToPage(currentPage + 1) }
                        }) {
                            Icon(Icons.AutoMirrored.Filled.ArrowForward, stringResource(R.string.flight_wo_next_page))
                        }
                    }
                }
                val feedback = if (saving) R.string.flight_wo_saving else actionError
                if (feedback != null) {
                    Text(stringResource(feedback), Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 4.dp),
                        color = if (saving) colors.onSurfaceVariant else colors.error,
                        style = MaterialTheme.typography.bodySmall, textAlign = TextAlign.Center)
                }
                Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp),
                    horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedButton(onClick = onSave, enabled = actionEnabled, modifier = Modifier.weight(1f),
                        contentPadding = ButtonDefaults.ButtonWithIconContentPadding) {
                        Icon(Icons.Outlined.Download, contentDescription = null, modifier = Modifier.size(ButtonDefaults.IconSize))
                        Spacer(Modifier.width(ButtonDefaults.IconSpacing))
                        Text(stringResource(R.string.flight_wo_download), textAlign = TextAlign.Center)
                    }
                    Button(onClick = onShare, enabled = actionEnabled, modifier = Modifier.weight(1f),
                        contentPadding = ButtonDefaults.ButtonWithIconContentPadding) {
                        Icon(Icons.Outlined.IosShare, contentDescription = null, modifier = Modifier.size(ButtonDefaults.IconSize))
                        Spacer(Modifier.width(ButtonDefaults.IconSpacing))
                        Text(stringResource(R.string.flight_wo_share), textAlign = TextAlign.Center)
                    }
                }
            }
        }
    }
}

@Composable
private fun WorkOrderPdfPage(
    renderer: FlightWorkOrderPdfRenderer,
    index: Int,
    pageCount: Int,
    active: Boolean,
    onReady: (Boolean) -> Unit,
    onReload: () -> Unit,
) {
    BoxWithConstraints(Modifier.fillMaxSize().clipToBounds(), contentAlignment = Alignment.Center) {
        val density = LocalDensity.current
        val renderWidth = with(density) { maxWidth.roundToPx() }.coerceIn(1, 1_024) * 2
        val result by produceState<Result<Bitmap>?>(null, renderer, index, renderWidth) {
            value = null
            try {
                value = Result.success(renderer.renderPage(index, renderWidth))
            } catch (cancelled: CancellationException) {
                throw cancelled
            } catch (error: Exception) {
                value = Result.failure(error)
            }
        }
        val bitmap = result?.getOrNull()
        LaunchedEffect(active, bitmap) {
            if (active) onReady(bitmap != null)
        }
        when {
            bitmap == null && result == null -> CircularProgressIndicator()
            bitmap == null -> Column(horizontalAlignment = Alignment.CenterHorizontally) {
                Text(stringResource(R.string.flight_wo_preview_failed), textAlign = TextAlign.Center)
                if (active) TextButton(onClick = onReload) { Text(stringResource(R.string.flights_retry)) }
            }
            else -> {
                val image = remember(bitmap) { bitmap.asImageBitmap() }
                val ratio = bitmap.width.toFloat() / bitmap.height
                val width = minOf(maxWidth, maxHeight * ratio)
                val height = width / ratio
                var zoom by remember(bitmap, width, height) { mutableFloatStateOf(1f) }
                var offset by remember(bitmap, width, height) { mutableStateOf(Offset.Zero) }
                val viewportCenter = with(density) { Offset(maxWidth.toPx() / 2, maxHeight.toPx() / 2) }
                val transform = rememberTransformableState { centroid, zoomChange, panChange, _ ->
                    val scale = (zoom * zoomChange).coerceIn(1f, 4f)
                    val anchor = if (centroid.isSpecified) centroid - viewportCenter else Offset.Zero
                    val translation = anchor + (offset - anchor) * (scale / zoom) + panChange
                    val maxX = with(density) { ((width * scale - maxWidth).toPx() / 2).coerceAtLeast(0f) }
                    val maxY = with(density) { ((height * scale - maxHeight).toPx() / 2).coerceAtLeast(0f) }
                    offset = Offset(
                        translation.x.coerceIn(-maxX, maxX),
                        translation.y.coerceIn(-maxY, maxY),
                    )
                    zoom = scale
                }
                Box(Modifier.fillMaxSize().transformable(transform, canPan = { zoom > 1f }),
                    contentAlignment = Alignment.Center) {
                    Image(image, stringResource(R.string.flight_wo_page, index + 1, pageCount),
                        Modifier.size(width, height).graphicsLayer {
                            scaleX = zoom
                            scaleY = zoom
                            translationX = offset.x
                            translationY = offset.y
                        })
                }
            }
        }
    }
}
