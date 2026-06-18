package com.nags.operations.ui.components

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.media.MediaRecorder
import android.net.Uri
import android.os.Build
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.AttachFile
import androidx.compose.material.icons.filled.CameraAlt
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Description
import androidx.compose.material.icons.filled.FiberManualRecord
import androidx.compose.material.icons.filled.Image
import androidx.compose.material.icons.filled.Mic
import androidx.compose.material.icons.filled.PhotoLibrary
import androidx.compose.material.icons.filled.PictureAsPdf
import androidx.compose.material.icons.filled.Stop
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import androidx.core.content.FileProvider
import com.nags.operations.data.TaskAttachmentKindValue
import com.nags.operations.ui.workorder.TaskAttachmentDraft
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File

private const val MaxVoiceDurationMs = 60_000L

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PhotoAttachmentButton(
    onAttachment: (TaskAttachmentDraft) -> Unit,
    modifier: Modifier = Modifier,
) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var sheetOpen by remember { mutableStateOf(false) }
    var pendingCameraUri by remember { mutableStateOf<Uri?>(null) }

    fun captureImageAsync(uri: Uri) {
        scope.launch {
            val draft = withContext(Dispatchers.IO) {
                captureAttachmentInternal(context, uri, TaskAttachmentKindValue.Image)
            }
            draft?.let(onAttachment)
        }
    }

    val galleryLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.GetContent(),
    ) { uri: Uri? ->
        if (uri != null) {
            captureImageAsync(uri)
        }
    }

    val cameraLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.TakePicture(),
    ) { success ->
        val uri = pendingCameraUri
        pendingCameraUri = null
        if (success && uri != null) {
            captureImageAsync(uri)
        }
    }

    val cameraPermissionLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission(),
    ) { granted ->
        if (granted) {
            val uri = createCameraOutputUri(context)
            pendingCameraUri = uri
            cameraLauncher.launch(uri)
        }
    }

    AttachmentActionTile(
        label = "Photo",
        icon = Icons.Default.Image,
        onClick = { sheetOpen = true },
        modifier = modifier,
    )

    if (sheetOpen) {
        ModalBottomSheet(
            onDismissRequest = { sheetOpen = false },
            sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true),
        ) {
            Column(modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp)) {
                Text(
                    "Add a photo",
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.SemiBold,
                )
                Spacer(Modifier.height(12.dp))
                ChooserOption(
                    icon = Icons.Default.CameraAlt,
                    label = "Take photo",
                    subtitle = "Capture with the camera",
                    onClick = {
                        sheetOpen = false
                        if (hasCameraPermission(context)) {
                            val uri = createCameraOutputUri(context)
                            pendingCameraUri = uri
                            cameraLauncher.launch(uri)
                        } else {
                            cameraPermissionLauncher.launch(Manifest.permission.CAMERA)
                        }
                    },
                )
                Spacer(Modifier.height(8.dp))
                ChooserOption(
                    icon = Icons.Default.PhotoLibrary,
                    label = "Choose from gallery",
                    subtitle = "Pick an existing image",
                    onClick = {
                        sheetOpen = false
                        galleryLauncher.launch("image/*")
                    },
                )
                Spacer(Modifier.height(16.dp))
            }
        }
    }
}

@Composable
fun VoiceAttachmentButton(
    onAttachment: (TaskAttachmentDraft) -> Unit,
    modifier: Modifier = Modifier,
) {
    val context = LocalContext.current
    var recorder by remember { mutableStateOf<MediaRecorder?>(null) }
    var outputFile by remember { mutableStateOf<File?>(null) }
    var elapsedMs by remember { mutableStateOf(0L) }
    var recording by remember { mutableStateOf(false) }

    fun stopAndCapture() {
        val r = recorder
        val file = outputFile
        recorder = null
        outputFile = null
        recording = false
        elapsedMs = 0L

        if (r == null) return
        runCatching { r.stop() }
        runCatching { r.release() }
        if (file == null || !file.exists() || file.length() == 0L) return

        val uri = FileProvider.getUriForFile(
            context,
            "${context.packageName}.fileprovider",
            file,
        )
        captureAttachmentInternal(context, uri, TaskAttachmentKindValue.Voice)?.let(onAttachment)
    }

    val permissionLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission(),
    ) { granted ->
        if (granted) {
            val started = startRecording(context)
            if (started != null) {
                recorder = started.first
                outputFile = started.second
                recording = true
                elapsedMs = 0L
            }
        }
    }

    LaunchedEffect(recording) {
        if (!recording) return@LaunchedEffect
        val tick = 100L
        while (recording) {
            delay(tick)
            elapsedMs += tick
            if (elapsedMs >= MaxVoiceDurationMs) {
                stopAndCapture()
                break
            }
        }
    }

    DisposableEffect(Unit) {
        onDispose {
            recorder?.let { r ->
                runCatching { r.stop() }
                runCatching { r.release() }
            }
            recorder = null
            outputFile?.delete()
            outputFile = null
        }
    }

    AttachmentActionTile(
        label = if (recording) "Stop · ${elapsedMs / 1000}s" else "Voice",
        icon = if (recording) Icons.Default.Stop else Icons.Default.Mic,
        accent = if (recording) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.primary,
        onClick = {
            if (recording) {
                stopAndCapture()
            } else if (hasRecordPermission(context)) {
                val started = startRecording(context)
                if (started != null) {
                    recorder = started.first
                    outputFile = started.second
                    recording = true
                    elapsedMs = 0L
                }
            } else {
                permissionLauncher.launch(Manifest.permission.RECORD_AUDIO)
            }
        },
        modifier = modifier,
    )
}

@Composable
fun DocumentAttachmentButton(
    onAttachment: (TaskAttachmentDraft) -> Unit,
    modifier: Modifier = Modifier,
) {
    val context = LocalContext.current
    val picker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri: Uri? ->
        if (uri != null) {
            captureAttachmentInternal(context, uri, TaskAttachmentKindValue.Document)?.let(onAttachment)
        }
    }
    AttachmentActionTile(
        label = "Docs",
        icon = Icons.Default.Description,
        onClick = { picker.launch("*/*") },
        modifier = modifier,
    )
}

@Composable
private fun ChooserOption(
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    label: String,
    subtitle: String,
    onClick: () -> Unit,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(14.dp))
            .clickable(onClick = onClick)
            .padding(horizontal = 12.dp, vertical = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(
            modifier = Modifier
                .size(40.dp)
                .clip(CircleShape)
                .background(MaterialTheme.colorScheme.primaryContainer),
            contentAlignment = Alignment.Center,
        ) {
            Icon(
                icon,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.onPrimaryContainer,
            )
        }
        Spacer(Modifier.width(12.dp))
        Column(modifier = Modifier.fillMaxWidth()) {
            Text(label, style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.SemiBold)
            Text(
                subtitle,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

@Composable
fun AttachmentActionTile(
    label: String,
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    accent: Color = MaterialTheme.colorScheme.primary,
    pulsing: Boolean = false,
) {
    val tileColor = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.5f)
    Row(
        modifier = modifier
            .height(56.dp)
            .clip(RoundedCornerShape(14.dp))
            .background(tileColor)
            .clickable(onClick = onClick)
            .padding(horizontal = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(
            modifier = Modifier
                .size(32.dp)
                .clip(CircleShape)
                .background(accent.copy(alpha = 0.18f)),
            contentAlignment = Alignment.Center,
        ) {
            if (pulsing) {
                Icon(
                    Icons.Default.FiberManualRecord,
                    contentDescription = null,
                    tint = accent,
                )
            } else {
                Icon(icon, contentDescription = null, tint = accent)
            }
        }
        Spacer(Modifier.width(8.dp))
        Text(label, style = MaterialTheme.typography.labelLarge, fontWeight = FontWeight.SemiBold)
    }
}

@Composable
fun TaskAttachmentRow(
    attachment: TaskAttachmentDraft,
    onRemove: () -> Unit,
) {
    val (icon, kindLabel) = when (attachment.kind) {
        TaskAttachmentKindValue.Image -> Icons.Default.Image to "Photo"
        TaskAttachmentKindValue.Voice -> Icons.Default.Mic to "Voice"
        TaskAttachmentKindValue.Document -> Icons.Default.PictureAsPdf to "Docs"
        else -> Icons.Default.AttachFile to "File"
    }
    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(10.dp),
        color = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.5f),
    ) {
        Row(
            modifier = Modifier.padding(8.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Icon(icon, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
            Spacer(Modifier.width(10.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(attachment.fileName, style = MaterialTheme.typography.bodyMedium, maxLines = 1)
                Text(
                    "$kindLabel · ${formatAttachmentBytes(attachment.sizeBytes)}",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            IconButton(onClick = onRemove) {
                Icon(Icons.Default.Close, contentDescription = "Remove attachment")
            }
        }
    }
}

private fun formatAttachmentBytes(size: Long): String = when {
    size < 1024 -> "$size B"
    size < 1024 * 1024 -> "${size / 1024} KB"
    else -> "%.1f MB".format(size / (1024.0 * 1024.0))
}

private fun hasCameraPermission(context: Context): Boolean =
    ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED

private fun hasRecordPermission(context: Context): Boolean =
    ContextCompat.checkSelfPermission(context, Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED

private fun createCameraOutputUri(context: Context): Uri {
    val dir = File(context.cacheDir, "images").apply { mkdirs() }
    val file = File(dir, "photo_${System.currentTimeMillis()}.jpg")
    return FileProvider.getUriForFile(context, "${context.packageName}.fileprovider", file)
}

private fun startRecording(context: Context): Pair<MediaRecorder, File>? = runCatching {
    val dir = File(context.cacheDir, "voice").apply { mkdirs() }
    val file = File(dir, "voice_${System.currentTimeMillis()}.m4a")
    val recorder = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
        MediaRecorder(context)
    } else {
        @Suppress("DEPRECATION")
        MediaRecorder()
    }
    recorder.setAudioSource(MediaRecorder.AudioSource.MIC)
    recorder.setOutputFormat(MediaRecorder.OutputFormat.MPEG_4)
    recorder.setAudioEncoder(MediaRecorder.AudioEncoder.AAC)
    recorder.setAudioSamplingRate(44_100)
    recorder.setAudioEncodingBitRate(64_000)
    recorder.setMaxDuration(MaxVoiceDurationMs.toInt())
    recorder.setOutputFile(file.absolutePath)
    recorder.prepare()
    recorder.start()
    recorder to file
}.getOrNull()
