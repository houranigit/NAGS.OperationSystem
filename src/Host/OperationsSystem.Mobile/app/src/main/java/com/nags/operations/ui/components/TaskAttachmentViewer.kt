package com.nags.operations.ui.components

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.pdf.PdfRenderer
import android.media.AudioAttributes
import android.media.AudioFocusRequest
import android.media.AudioManager
import android.media.MediaPlayer
import android.os.ParcelFileDescriptor
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.gestures.rememberTransformableState
import androidx.compose.foundation.gestures.transformable
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.BrokenImage
import androidx.compose.material.icons.filled.ChevronLeft
import androidx.compose.material.icons.filled.ChevronRight
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Mic
import androidx.compose.material.icons.filled.Pause
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.ZoomIn
import androidx.compose.material.icons.filled.ZoomOut
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.IconButtonDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Slider
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.IntSize
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.core.content.ContextCompat
import com.nags.operations.ui.workorder.TaskAttachmentDraft
import java.io.File
import kotlin.math.roundToInt
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext

private sealed interface PreviewFileState {
    data object Loading : PreviewFileState
    data class Ready(val file: File) : PreviewFileState
    data object Error : PreviewFileState
}

private sealed interface BitmapPreviewState {
    data object Loading : BitmapPreviewState
    data class Ready(val bitmap: Bitmap) : BitmapPreviewState
    data object Error : BitmapPreviewState
}

private sealed interface PdfInfoState {
    data object Loading : PdfInfoState
    data class Ready(val pageCount: Int) : PdfInfoState
    data object Error : PdfInfoState
}

/** Owns a private preview file even when the loading coroutine is cancelled before publishing it. */
private class PreviewFileOwner {
    private var file: File? = null
    private var closed = false

    @Synchronized
    fun track(candidate: File): File? {
        if (closed) {
            deleteMaterializedAttachmentPreview(candidate)
            return null
        }
        file = candidate
        return candidate
    }

    @Synchronized
    fun close() {
        closed = true
        file?.let(::deleteMaterializedAttachmentPreview)
        file = null
    }
}

/** Tracks native bitmaps so cancellation and rapid PDF page changes cannot orphan them. */
private class BitmapResourceOwner {
    private val bitmaps = mutableListOf<Bitmap>()
    private var closed = false

    @Synchronized
    fun track(candidate: Bitmap): Bitmap? {
        if (closed) {
            if (!candidate.isRecycled) candidate.recycle()
            return null
        }
        bitmaps += candidate
        return candidate
    }

    @Synchronized
    fun release(candidate: Bitmap) {
        bitmaps.remove(candidate)
        if (!candidate.isRecycled) candidate.recycle()
    }

    @Synchronized
    fun close() {
        closed = true
        bitmaps.forEach { bitmap ->
            if (!bitmap.isRecycled) bitmap.recycle()
        }
        bitmaps.clear()
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun TaskAttachmentPreviewDialog(
    attachment: TaskAttachmentDraft,
    onDismiss: () -> Unit,
) {
    val context = LocalContext.current.applicationContext
    val previewKind = remember(attachment.kind) { attachment.attachmentPreviewKind() }
    val fileOwner = remember(attachment) { PreviewFileOwner() }
    var fileState by remember(attachment) {
        mutableStateOf<PreviewFileState>(PreviewFileState.Loading)
    }
    DisposableEffect(fileOwner) {
        onDispose(fileOwner::close)
    }
    LaunchedEffect(attachment, previewKind, fileOwner) {
        if (previewKind == AttachmentPreviewKind.Unsupported) {
            fileState = PreviewFileState.Error
            return@LaunchedEffect
        }
        fileState = PreviewFileState.Loading
        val loaded = withContext(Dispatchers.IO) {
            materializeAttachmentPreview(context, attachment)
                ?.let(fileOwner::track)
                ?.let(PreviewFileState::Ready)
                ?: PreviewFileState.Error
        }
        fileState = loaded
    }

    Dialog(
        onDismissRequest = onDismiss,
        properties = DialogProperties(
            dismissOnBackPress = true,
            dismissOnClickOutside = false,
            usePlatformDefaultWidth = false,
            decorFitsSystemWindows = false,
        ),
    ) {
        Surface(
            modifier = Modifier.fillMaxSize(),
            color = MaterialTheme.colorScheme.surface,
        ) {
            Scaffold(
                topBar = {
                    TopAppBar(
                        title = {
                            Column {
                                Text(
                                    text = attachment.fileName.ifBlank { attachment.attachmentKindLabel() },
                                    maxLines = 1,
                                    overflow = TextOverflow.Ellipsis,
                                    style = MaterialTheme.typography.titleMedium,
                                    fontWeight = FontWeight.SemiBold,
                                )
                                Text(
                                    text = "${attachment.attachmentKindLabel()} · " +
                                        formatAttachmentBytes(attachment.sizeBytes),
                                    maxLines = 1,
                                    style = MaterialTheme.typography.labelMedium,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                )
                            }
                        },
                        navigationIcon = {
                            IconButton(onClick = onDismiss) {
                                Icon(Icons.Default.Close, contentDescription = "Close attachment preview")
                            }
                        },
                    )
                },
            ) { padding ->
                Box(
                    modifier = Modifier
                        .fillMaxSize()
                        .padding(padding),
                ) {
                    if (previewKind == AttachmentPreviewKind.Unsupported) {
                        PreviewError(
                            "Preview not supported",
                            "This attachment type cannot be reviewed in the app.",
                        )
                    } else when (val current = fileState) {
                        PreviewFileState.Loading -> PreviewLoading("Preparing attachment…")
                        PreviewFileState.Error -> PreviewError(
                            "Preview unavailable",
                            "This attachment is incomplete or could not be read on this device.",
                        )
                        is PreviewFileState.Ready -> when (previewKind) {
                            AttachmentPreviewKind.Image -> ImageAttachmentPreview(
                                file = current.file,
                                contentDescription = "Preview of ${attachment.fileName}",
                            )
                            AttachmentPreviewKind.Pdf -> PdfAttachmentPreview(current.file)
                            AttachmentPreviewKind.Voice -> VoiceAttachmentPreview(
                                file = current.file,
                                fileName = attachment.fileName,
                            )
                            AttachmentPreviewKind.Unsupported -> Unit
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun ImageAttachmentPreview(
    file: File,
    contentDescription: String,
) {
    val bitmapOwner = remember(file) { BitmapResourceOwner() }
    var bitmapState by remember(file) {
        mutableStateOf<BitmapPreviewState>(BitmapPreviewState.Loading)
    }
    DisposableEffect(bitmapOwner) {
        onDispose(bitmapOwner::close)
    }
    LaunchedEffect(file, bitmapOwner) {
        bitmapState = BitmapPreviewState.Loading
        val loaded = withContext(Dispatchers.IO) {
            runCatching { BitmapFactory.decodeFile(file.absolutePath) }
                .getOrNull()
                ?.let(bitmapOwner::track)
                ?.let(BitmapPreviewState::Ready)
                ?: BitmapPreviewState.Error
        }
        bitmapState = loaded
    }

    when (val current = bitmapState) {
        BitmapPreviewState.Loading -> PreviewLoading("Loading photo…")
        BitmapPreviewState.Error -> PreviewError(
            "Photo unavailable",
            "The image could not be decoded. Try removing it and attaching it again.",
        )
        is BitmapPreviewState.Ready -> {
            DisposableEffect(current.bitmap) {
                onDispose {
                    bitmapOwner.release(current.bitmap)
                }
            }
            ZoomableBitmap(
                bitmap = current.bitmap,
                contentDescription = contentDescription,
                resetKey = file.absolutePath,
                canvasColor = Color(0xFF111315),
            )
        }
    }
}

@Composable
private fun PdfAttachmentPreview(file: File) {
    var infoState by remember(file) {
        mutableStateOf<PdfInfoState>(PdfInfoState.Loading)
    }
    LaunchedEffect(file) {
        infoState = PdfInfoState.Loading
        val loaded = withContext(Dispatchers.IO) {
            runCatching { readPdfPageCount(file) }
                .getOrNull()
                ?.takeIf { it > 0 }
                ?.let(PdfInfoState::Ready)
                ?: PdfInfoState.Error
        }
        infoState = loaded
    }

    when (val current = infoState) {
        PdfInfoState.Loading -> PreviewLoading("Opening PDF…")
        PdfInfoState.Error -> PreviewError(
            "PDF unavailable",
            "This document is damaged, password protected, or cannot be displayed.",
        )
        is PdfInfoState.Ready -> PdfDocument(
            file = file,
            pageCount = current.pageCount,
        )
    }
}

@Composable
private fun PdfDocument(
    file: File,
    pageCount: Int,
) {
    var pageIndex by remember(file) { mutableIntStateOf(0) }
    // PdfRenderer.Page.render is blocking and cannot be cancelled. Serializing calls avoids a
    // rapid sequence of page taps retaining several large native bitmaps at the same time.
    val renderMutex = remember(file) { Mutex() }
    Column(Modifier.fillMaxSize()) {
        BoxWithConstraints(
            modifier = Modifier
                .fillMaxWidth()
                .weight(1f)
                .background(Color(0xFF2A2D31)),
        ) {
            val density = LocalDensity.current
            val targetWidthPx = with(density) { maxWidth.roundToPx() }
                .coerceIn(480, 1_600)
            val bitmapOwner = remember(file, pageIndex, targetWidthPx) { BitmapResourceOwner() }
            var pageState by remember(file, pageIndex, targetWidthPx) {
                mutableStateOf<BitmapPreviewState>(BitmapPreviewState.Loading)
            }
            DisposableEffect(bitmapOwner) {
                onDispose(bitmapOwner::close)
            }
            LaunchedEffect(file, pageIndex, targetWidthPx, bitmapOwner) {
                pageState = BitmapPreviewState.Loading
                val loaded = withContext(Dispatchers.IO) {
                    renderMutex.withLock {
                        runCatching { renderPdfPage(file, pageIndex, targetWidthPx) }
                            .getOrNull()
                            ?.let(bitmapOwner::track)
                            ?.let(BitmapPreviewState::Ready)
                            ?: BitmapPreviewState.Error
                    }
                }
                pageState = loaded
            }

            when (val current = pageState) {
                BitmapPreviewState.Loading -> PreviewLoading(
                    message = "Rendering page ${pageIndex + 1}…",
                    onDarkCanvas = true,
                )
                BitmapPreviewState.Error -> PreviewError(
                    title = "Page unavailable",
                    message = "This page could not be rendered.",
                    onDarkCanvas = true,
                )
                is BitmapPreviewState.Ready -> {
                    DisposableEffect(current.bitmap) {
                        onDispose {
                            bitmapOwner.release(current.bitmap)
                        }
                    }
                    ZoomableBitmap(
                        bitmap = current.bitmap,
                        contentDescription = "PDF page ${pageIndex + 1} of $pageCount",
                        resetKey = "${file.absolutePath}:$pageIndex",
                        canvasColor = Color(0xFF2A2D31),
                    )
                }
            }
        }

        HorizontalDivider()
        Surface(
            color = MaterialTheme.colorScheme.surface,
            tonalElevation = 3.dp,
        ) {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .navigationBarsPadding()
                    .padding(horizontal = 16.dp, vertical = 10.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.SpaceBetween,
            ) {
                IconButton(
                    onClick = { pageIndex -= 1 },
                    enabled = pageIndex > 0,
                ) {
                    Icon(
                        Icons.Default.ChevronLeft,
                        contentDescription = "Previous PDF page",
                    )
                }
                Column(
                    modifier = Modifier.weight(1f),
                    horizontalAlignment = Alignment.CenterHorizontally,
                ) {
                    Text(
                        "Page ${pageIndex + 1} of $pageCount",
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = FontWeight.SemiBold,
                    )
                    Text(
                        "Pinch or use the zoom controls",
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
                IconButton(
                    onClick = { pageIndex += 1 },
                    enabled = pageIndex < pageCount - 1,
                ) {
                    Icon(
                        Icons.Default.ChevronRight,
                        contentDescription = "Next PDF page",
                    )
                }
            }
        }
    }
}

@Composable
private fun ZoomableBitmap(
    bitmap: Bitmap,
    contentDescription: String,
    resetKey: Any,
    canvasColor: Color,
) {
    var scale by remember(resetKey) { mutableFloatStateOf(1f) }
    var offset by remember(resetKey) { mutableStateOf(Offset.Zero) }
    var viewportSize by remember(resetKey) { mutableStateOf(IntSize.Zero) }
    fun boundedOffset(candidate: Offset, atScale: Float): Offset {
        if (atScale <= 1f) return Offset.Zero
        val maxX = viewportSize.width * (atScale - 1f) / 2f
        val maxY = viewportSize.height * (atScale - 1f) / 2f
        return Offset(
            x = candidate.x.coerceIn(-maxX, maxX),
            y = candidate.y.coerceIn(-maxY, maxY),
        )
    }
    val transformState = rememberTransformableState { zoomChange, panChange, _ ->
        val nextScale = (scale * zoomChange).coerceIn(1f, 5f)
        scale = nextScale
        offset = boundedOffset(offset + panChange, nextScale)
    }
    val imageBitmap = remember(bitmap) { bitmap.asImageBitmap() }
    fun resetZoom() {
        scale = 1f
        offset = Offset.Zero
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .clipToBounds()
            .background(canvasColor)
            .onSizeChanged { viewportSize = it }
            .semantics {
                stateDescription = "${(scale * 100).roundToInt()} percent zoom"
            },
        contentAlignment = Alignment.Center,
    ) {
        Image(
            bitmap = imageBitmap,
            contentDescription = contentDescription,
            contentScale = ContentScale.Fit,
            modifier = Modifier
                .fillMaxSize()
                .pointerInput(resetKey) {
                    detectTapGestures(
                        onDoubleTap = { resetZoom() },
                    )
                }
                .transformable(transformState)
                .graphicsLayer {
                    scaleX = scale
                    scaleY = scale
                    translationX = offset.x
                    translationY = offset.y
                },
        )

        Surface(
            modifier = Modifier
                .align(Alignment.BottomCenter)
                .padding(18.dp),
            color = Color.Black.copy(alpha = 0.72f),
            shape = RoundedCornerShape(50),
        ) {
            Row(
                modifier = Modifier.padding(horizontal = 4.dp, vertical = 2.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                IconButton(
                    onClick = {
                        scale = (scale - 0.5f).coerceAtLeast(1f)
                        offset = boundedOffset(offset, scale)
                    },
                    enabled = scale > 1f,
                    colors = IconButtonDefaults.iconButtonColors(
                        contentColor = Color.White,
                        disabledContentColor = Color.White.copy(alpha = 0.35f),
                    ),
                ) {
                    Icon(Icons.Default.ZoomOut, contentDescription = "Zoom out")
                }
                Text(
                    "${(scale * 100).roundToInt()}%",
                    color = Color.White,
                    style = MaterialTheme.typography.labelLarge,
                    modifier = Modifier.padding(horizontal = 4.dp),
                )
                IconButton(
                    onClick = { scale = (scale + 0.5f).coerceAtMost(5f) },
                    enabled = scale < 5f,
                    colors = IconButtonDefaults.iconButtonColors(
                        contentColor = Color.White,
                        disabledContentColor = Color.White.copy(alpha = 0.35f),
                    ),
                ) {
                    Icon(Icons.Default.ZoomIn, contentDescription = "Zoom in")
                }
                IconButton(
                    onClick = ::resetZoom,
                    enabled = scale > 1f || offset != Offset.Zero,
                    colors = IconButtonDefaults.iconButtonColors(
                        contentColor = Color.White,
                        disabledContentColor = Color.White.copy(alpha = 0.35f),
                    ),
                ) {
                    Icon(Icons.Default.Refresh, contentDescription = "Reset zoom")
                }
            }
        }
    }
}

@Composable
private fun VoiceAttachmentPreview(
    file: File,
    fileName: String,
) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val audioManager = remember(context) {
        context.getSystemService(AudioManager::class.java)
    }
    var player by remember(file) { mutableStateOf<MediaPlayer?>(null) }
    var prepared by remember(file) { mutableStateOf(false) }
    var isPlaying by remember(file) { mutableStateOf(false) }
    var isScrubbing by remember(file) { mutableStateOf(false) }
    var positionMs by remember(file) { mutableIntStateOf(0) }
    var durationMs by remember(file) { mutableIntStateOf(0) }
    var playbackError by remember(file) { mutableStateOf<String?>(null) }

    val focusListener = remember(file) {
        AudioManager.OnAudioFocusChangeListener { change ->
            if (change == AudioManager.AUDIOFOCUS_LOSS ||
                change == AudioManager.AUDIOFOCUS_LOSS_TRANSIENT ||
                change == AudioManager.AUDIOFOCUS_LOSS_TRANSIENT_CAN_DUCK
            ) {
                runCatching { player?.pause() }
                isPlaying = false
            }
        }
    }
    val focusRequest = remember(focusListener) {
        AudioFocusRequest.Builder(AudioManager.AUDIOFOCUS_GAIN_TRANSIENT)
            .setAudioAttributes(
                AudioAttributes.Builder()
                    .setUsage(AudioAttributes.USAGE_MEDIA)
                    .setContentType(AudioAttributes.CONTENT_TYPE_SPEECH)
                    .build(),
            )
            .setOnAudioFocusChangeListener(focusListener)
            .build()
    }

    DisposableEffect(file, audioManager, focusRequest) {
        val mediaPlayer = MediaPlayer()
        player = mediaPlayer
        mediaPlayer.setAudioAttributes(
            AudioAttributes.Builder()
                .setUsage(AudioAttributes.USAGE_MEDIA)
                .setContentType(AudioAttributes.CONTENT_TYPE_SPEECH)
                .build(),
        )
        mediaPlayer.setOnPreparedListener { readyPlayer ->
            durationMs = readyPlayer.duration.coerceAtLeast(0)
            positionMs = 0
            prepared = true
            playbackError = null
        }
        mediaPlayer.setOnCompletionListener { completedPlayer ->
            positionMs = completedPlayer.duration.coerceAtLeast(0)
            isPlaying = false
            isScrubbing = false
            audioManager?.abandonAudioFocusRequest(focusRequest)
        }
        mediaPlayer.setOnErrorListener { _, _, _ ->
            prepared = false
            isPlaying = false
            audioManager?.abandonAudioFocusRequest(focusRequest)
            playbackError = "This voice note could not be played."
            true
        }
        runCatching {
            mediaPlayer.setDataSource(file.absolutePath)
            mediaPlayer.prepareAsync()
        }.onFailure {
            playbackError = "This voice note could not be opened."
        }

        onDispose {
            audioManager?.abandonAudioFocusRequest(focusRequest)
            runCatching { mediaPlayer.release() }
        }
    }

    DisposableEffect(lifecycleOwner, player, audioManager, focusRequest) {
        val observer = LifecycleEventObserver { _, event ->
            if (event == Lifecycle.Event.ON_PAUSE) {
                runCatching { player?.pause() }
                isPlaying = false
                audioManager?.abandonAudioFocusRequest(focusRequest)
            }
        }
        lifecycleOwner.lifecycle.addObserver(observer)
        onDispose { lifecycleOwner.lifecycle.removeObserver(observer) }
    }

    DisposableEffect(context, player, audioManager, focusRequest) {
        val receiver = object : BroadcastReceiver() {
            override fun onReceive(receiverContext: Context?, intent: Intent?) {
                if (intent?.action != AudioManager.ACTION_AUDIO_BECOMING_NOISY) return
                runCatching { player?.pause() }
                isPlaying = false
                audioManager?.abandonAudioFocusRequest(focusRequest)
            }
        }
        ContextCompat.registerReceiver(
            context,
            receiver,
            IntentFilter(AudioManager.ACTION_AUDIO_BECOMING_NOISY),
            ContextCompat.RECEIVER_EXPORTED,
        )
        onDispose {
            runCatching { context.unregisterReceiver(receiver) }
        }
    }

    LaunchedEffect(player, isPlaying, isScrubbing) {
        while (isPlaying) {
            if (!isScrubbing) {
                positionMs = runCatching { player?.currentPosition ?: 0 }.getOrDefault(0)
            }
            delay(200L)
        }
    }

    fun togglePlayback() {
        val activePlayer = player ?: return
        if (isPlaying) {
            runCatching { activePlayer.pause() }
            isPlaying = false
            audioManager?.abandonAudioFocusRequest(focusRequest)
            return
        }
        if (!prepared) return
        val focusGranted = runCatching {
            audioManager?.requestAudioFocus(focusRequest) == AudioManager.AUDIOFOCUS_REQUEST_GRANTED
        }.getOrDefault(false)
        if (!focusGranted) {
            playbackError = "Audio is currently in use by another app. Try again in a moment."
            return
        }
        if (durationMs > 0 && positionMs >= durationMs) {
            runCatching { activePlayer.seekTo(0) }
            positionMs = 0
        }
        runCatching { activePlayer.start() }
            .onSuccess {
                playbackError = null
                isPlaying = true
            }
            .onFailure {
                playbackError = "This voice note could not be played."
                audioManager?.abandonAudioFocusRequest(focusRequest)
            }
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .navigationBarsPadding()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 28.dp, vertical = 24.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center,
    ) {
        Surface(
            modifier = Modifier.size(112.dp),
            shape = CircleShape,
            color = MaterialTheme.colorScheme.primaryContainer,
        ) {
            Box(contentAlignment = Alignment.Center) {
                Icon(
                    Icons.Default.Mic,
                    contentDescription = null,
                    modifier = Modifier.size(52.dp),
                    tint = MaterialTheme.colorScheme.onPrimaryContainer,
                )
            }
        }
        Spacer(Modifier.height(22.dp))
        Text(
            "Voice note",
            style = MaterialTheme.typography.headlineSmall,
            fontWeight = FontWeight.SemiBold,
        )
        Text(
            fileName,
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        Spacer(Modifier.height(28.dp))

        if (!prepared && playbackError == null) {
            CircularProgressIndicator()
            Spacer(Modifier.height(12.dp))
            Text("Preparing audio…", color = MaterialTheme.colorScheme.onSurfaceVariant)
        } else {
            Button(
                onClick = ::togglePlayback,
                enabled = prepared,
                modifier = Modifier.size(68.dp),
                shape = CircleShape,
                contentPadding = androidx.compose.foundation.layout.PaddingValues(0.dp),
            ) {
                Icon(
                    imageVector = when {
                        isPlaying -> Icons.Default.Pause
                        durationMs > 0 && positionMs >= durationMs -> Icons.Default.Refresh
                        else -> Icons.Default.PlayArrow
                    },
                    contentDescription = if (isPlaying) "Pause voice note" else "Play voice note",
                    modifier = Modifier.size(34.dp),
                )
            }
            Spacer(Modifier.height(22.dp))
            Slider(
                value = positionMs.coerceIn(0, durationMs.coerceAtLeast(0)).toFloat(),
                onValueChange = { value ->
                    isScrubbing = true
                    positionMs = value.roundToInt()
                },
                onValueChangeFinished = {
                    runCatching { player?.seekTo(positionMs) }
                    isScrubbing = false
                },
                enabled = prepared && durationMs > 0,
                valueRange = 0f..durationMs.coerceAtLeast(1).toFloat(),
                modifier = Modifier
                    .fillMaxWidth()
                    .semantics { contentDescription = "Voice note playback position" },
            )
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
            ) {
                Text(
                    formatAttachmentDuration(positionMs),
                    style = MaterialTheme.typography.labelMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                Text(
                    formatAttachmentDuration(durationMs),
                    style = MaterialTheme.typography.labelMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
        playbackError?.let { message ->
            Spacer(Modifier.height(14.dp))
            Text(
                message,
                color = MaterialTheme.colorScheme.error,
                style = MaterialTheme.typography.bodyMedium,
            )
        }
        Spacer(Modifier.height(22.dp))
        Text(
            "Playback stays inside the Operations app.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}

@Composable
private fun PreviewLoading(
    message: String,
    onDarkCanvas: Boolean = false,
) {
    Column(
        modifier = Modifier.fillMaxSize(),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center,
    ) {
        CircularProgressIndicator(color = if (onDarkCanvas) Color.White else MaterialTheme.colorScheme.primary)
        Spacer(Modifier.height(14.dp))
        Text(
            message,
            color = if (onDarkCanvas) Color.White else MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}

@Composable
private fun PreviewError(
    title: String,
    message: String,
    onDarkCanvas: Boolean = false,
) {
    val contentColor = if (onDarkCanvas) Color.White else MaterialTheme.colorScheme.onSurface
    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(32.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center,
    ) {
        Surface(
            modifier = Modifier.size(72.dp),
            shape = CircleShape,
            color = if (onDarkCanvas) {
                Color.White.copy(alpha = 0.12f)
            } else {
                MaterialTheme.colorScheme.errorContainer
            },
        ) {
            Box(contentAlignment = Alignment.Center) {
                Icon(
                    Icons.Default.BrokenImage,
                    contentDescription = null,
                    modifier = Modifier.size(34.dp),
                    tint = if (onDarkCanvas) Color.White else MaterialTheme.colorScheme.onErrorContainer,
                )
            }
        }
        Spacer(Modifier.height(18.dp))
        Text(
            title,
            style = MaterialTheme.typography.titleLarge,
            fontWeight = FontWeight.SemiBold,
            color = contentColor,
        )
        Spacer(Modifier.height(8.dp))
        Text(
            message,
            style = MaterialTheme.typography.bodyMedium,
            color = if (onDarkCanvas) Color.White.copy(alpha = 0.78f) else MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}

private fun readPdfPageCount(file: File): Int =
    ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_ONLY).use { descriptor ->
        PdfRenderer(descriptor).use(PdfRenderer::getPageCount)
    }

private fun renderPdfPage(
    file: File,
    pageIndex: Int,
    targetWidthPx: Int,
): Bitmap = ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_ONLY).use { descriptor ->
    PdfRenderer(descriptor).use { renderer ->
        require(pageIndex in 0 until renderer.pageCount)
        renderer.openPage(pageIndex).use { page ->
            val (width, height) = scaledPdfPreviewSize(
                sourceWidth = page.width,
                sourceHeight = page.height,
                targetWidth = targetWidthPx,
            )
            val bitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
            try {
                bitmap.eraseColor(android.graphics.Color.WHITE)
                page.render(bitmap, null, null, PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY)
                bitmap
            } catch (error: Throwable) {
                if (!bitmap.isRecycled) bitmap.recycle()
                throw error
            }
        }
    }
}

internal fun scaledPdfPreviewSize(
    sourceWidth: Int,
    sourceHeight: Int,
    targetWidth: Int,
    maxDimension: Int = 3_200,
): Pair<Int, Int> {
    require(sourceWidth > 0 && sourceHeight > 0)
    val width = targetWidth.coerceIn(1, maxDimension.coerceAtLeast(1))
    val proportionalHeight = (width.toDouble() * sourceHeight / sourceWidth)
        .roundToInt()
        .coerceAtLeast(1)
    if (proportionalHeight <= maxDimension) return width to proportionalHeight
    val adjustedWidth = (maxDimension.toDouble() * sourceWidth / sourceHeight)
        .roundToInt()
        .coerceAtLeast(1)
    return adjustedWidth to maxDimension
}
