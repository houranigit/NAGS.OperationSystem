package com.nags.operations.ui.flights

import android.content.ContentResolver
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Matrix
import androidx.exifinterface.media.ExifInterface
import androidx.core.net.toUri
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Add
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.DeleteOutline
import androidx.compose.material.icons.outlined.Image
import androidx.compose.material.icons.outlined.InsertDriveFile
import androidx.compose.material.icons.outlined.Mic
import androidx.compose.material.icons.outlined.Pause
import androidx.compose.material.icons.outlined.PhotoCamera
import androidx.compose.material.icons.outlined.PhotoLibrary
import androidx.compose.material.icons.outlined.PlayArrow
import androidx.compose.material.icons.outlined.Stop
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.AbsoluteAlignment
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.error
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.nags.operations.R
import java.util.Locale
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/** Local media operations are supplied separately; none of these actions submits a form. */
internal interface FlightComposerMedia {
    val isRecording: Boolean
    val recordingMillis: Long
    val error: String?
    /** The loaded clip remains selected while paused so its progress stays visible. */
    val playingAttachmentId: String?
    val isPlaying: Boolean
    val playbackPositionMillis: Long
    val playbackDurationMillis: Long
    val canAdd: Boolean
    fun pickFiles()
    fun pickImages()
    fun takePhoto()
    fun startRecording()
    fun stopRecording()
    fun cancelRecording()
    fun open(item: FlightAttachmentDraft)
    fun togglePlayback(item: FlightAttachmentDraft)
    fun remove(item: FlightAttachmentDraft)
    fun dismissError()
}

/** One remarks value and its local attachments, kept together in a compact composer. */
@Composable
internal fun FlightRemarksComposer(
    remarks: String,
    onRemarksChange: (String) -> Unit,
    attachments: List<FlightAttachmentDraft>,
    onAttachmentsChange: (List<FlightAttachmentDraft>) -> Unit,
    description: String,
    enabled: Boolean = true,
    errorMessage: String? = null,
    label: String? = null,
    required: Boolean = false,
) {
    val media = rememberFlightComposerMedia(attachments) { if (enabled) onAttachmentsChange(it) }
    var menuOpen by rememberSaveable { mutableStateOf(false) }
    val focus = LocalFocusManager.current
    val keyboard = LocalSoftwareKeyboardController.current
    val limitMessage = if (remarks.length > 2000) stringResource(R.string.composer_remarks_too_long, remarks.length) else null
    fun prepareMedia(action: () -> Unit) { focus.clearFocus(); keyboard?.hide(); action() }
    FlightFormValidation(errorMessage ?: limitMessage) {
        Column(Modifier.fillMaxWidth().padding(horizontal = 14.dp, vertical = 10.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(label ?: stringResource(R.string.flight_form_remarks), style = MaterialTheme.typography.bodyMedium,
                fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onSurfaceVariant)
            Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(FlightFormGround)) {
                attachments.forEach { item -> key(item.id) {
                    if (composerAttachmentIsAudio(item)) ComposerAudioRow(item, media, enabled)
                    else ComposerFileRow(item, media, enabled)
                    HorizontalDivider(Modifier.padding(horizontal = 12.dp), thickness = 0.5.dp,
                        color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.55f))
                } }
                BasicTextField(remarks, onRemarksChange,
                    modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp).flightFormValidationFocus(enabled)
                        .flightFormEditableFocus().padding(horizontal = 12.dp, vertical = 10.dp)
                        .semantics { contentDescription = description; if (limitMessage != null) error(limitMessage) },
                    readOnly = media.isRecording || !enabled, enabled = enabled, minLines = 1, maxLines = 6,
                    textStyle = MaterialTheme.typography.bodyMedium.copy(color = MaterialTheme.colorScheme.onSurface,
                        fontWeight = FontWeight.Medium, textAlign = TextAlign.Left),
                    cursorBrush = SolidColor(MaterialTheme.colorScheme.primary),
                    keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Sentences, imeAction = ImeAction.Default),
                    decorationBox = { inner -> Box(Modifier.fillMaxWidth(), contentAlignment = AbsoluteAlignment.TopLeft) {
                        if (required && remarks.isEmpty()) Text(stringResource(R.string.flight_form_required_field), Modifier.fillMaxWidth(),
                            style = MaterialTheme.typography.bodyMedium, fontStyle = FontStyle.Italic,
                            color = MaterialTheme.colorScheme.onSurfaceVariant, textAlign = TextAlign.Left)
                        inner()
                    } })
                HorizontalDivider(Modifier.padding(horizontal = 12.dp), thickness = 0.5.dp,
                    color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.55f))
                if (media.isRecording) ComposerRecordingToolbar(media)
                else Row(Modifier.fillMaxWidth().padding(horizontal = 2.dp), verticalAlignment = Alignment.CenterVertically) {
                    IconButton(onClick = { prepareMedia { menuOpen = true } }, enabled = enabled && media.canAdd, modifier = Modifier.size(48.dp)) {
                        Icon(Icons.Outlined.Add, stringResource(R.string.composer_add_attachment), Modifier.size(24.dp),
                            tint = if (enabled && media.canAdd) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurface.copy(alpha = 0.35f))
                    }
                    Spacer(Modifier.weight(1f))
                    if (attachments.isNotEmpty()) Text(stringResource(R.string.composer_attachment_count, attachments.size),
                        style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    IconButton(onClick = { prepareMedia(media::takePhoto) }, enabled = enabled && media.canAdd, modifier = Modifier.size(48.dp)) {
                        Icon(Icons.Outlined.PhotoCamera, stringResource(R.string.composer_camera), Modifier.size(22.dp))
                    }
                    IconButton(onClick = { prepareMedia(media::startRecording) }, enabled = enabled && media.canAdd, modifier = Modifier.size(48.dp)) {
                        Box(Modifier.size(28.dp).background(MaterialTheme.colorScheme.primary.copy(alpha = if (enabled && media.canAdd) 1f else 0.35f), CircleShape),
                            contentAlignment = Alignment.Center) {
                            Icon(Icons.Outlined.Mic, stringResource(R.string.composer_record_voice), Modifier.size(17.dp),
                                tint = MaterialTheme.colorScheme.onPrimary)
                        }
                    }
                }
                media.error?.let { message ->
                    Row(Modifier.fillMaxWidth().padding(start = 12.dp), verticalAlignment = Alignment.CenterVertically) {
                        Text(message, Modifier.weight(1f).semantics { liveRegion = LiveRegionMode.Polite },
                            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.error)
                        IconButton(onClick = media::dismissError, modifier = Modifier.size(48.dp)) {
                            Icon(Icons.Outlined.Close, stringResource(R.string.composer_dismiss_error), Modifier.size(18.dp),
                                tint = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                    }
                }
            }
        }
        if (menuOpen) FlightFormModalSheet(stringResource(R.string.composer_add_attachment), onDismiss = { menuOpen = false }) { close ->
            Column(Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 4.dp)) {
                Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                    Text(stringResource(R.string.composer_add_attachment), Modifier.weight(1f).padding(start = 8.dp),
                        style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
                    IconButton(onClick = close, modifier = Modifier.size(48.dp)) {
                        Icon(Icons.Outlined.Close, stringResource(R.string.flights_cancel), Modifier.size(20.dp))
                    }
                }
                Row(Modifier.fillMaxWidth().padding(bottom = 12.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    ComposerMenuItem(Icons.Outlined.PhotoLibrary, stringResource(R.string.composer_photos), Modifier.weight(1f)) { close(); media.pickImages() }
                    ComposerMenuItem(Icons.Outlined.PhotoCamera, stringResource(R.string.composer_camera), Modifier.weight(1f)) { close(); media.takePhoto() }
                    ComposerMenuItem(Icons.Outlined.InsertDriveFile, stringResource(R.string.composer_document), Modifier.weight(1f)) { close(); media.pickFiles() }
                }
            }
        }
    }
}

/** The submitted-card view keeps existing document/photo and inline audio behavior. */
@Composable
internal fun FlightReadOnlyAttachmentRow(item: FlightAttachmentDraft, media: FlightComposerMedia) {
    val currentMedia by rememberUpdatedState(media)
    val session = LocalFlightMediaSession.current
    DisposableEffect(item.id, session) {
        onDispose { if (currentMedia.playingAttachmentId == item.id) session.stopAudio() }
    }
    if (item.mimeType.startsWith("audio/", true)) ComposerAudioRow(item, media, enabled = false, showRemove = false)
    else ComposerFileRow(item, media, enabled = false, showRemove = false)
}

@Composable
private fun ComposerRecordingToolbar(media: FlightComposerMedia) {
    Row(Modifier.fillMaxWidth().padding(horizontal = 2.dp), verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        IconButton(onClick = media::cancelRecording, modifier = Modifier.size(48.dp)) {
            Icon(Icons.Outlined.DeleteOutline, stringResource(R.string.composer_cancel_recording), Modifier.size(22.dp),
                tint = MaterialTheme.colorScheme.error)
        }
        Box(Modifier.size(7.dp).background(MaterialTheme.colorScheme.error, CircleShape))
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(1.dp)) {
            Text(composerDuration(media.recordingMillis), style = MaterialTheme.typography.bodyMedium.copy(fontFeatureSettings = "tnum"),
                fontWeight = FontWeight.Bold)
            Text(stringResource(R.string.composer_recording), style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        IconButton(onClick = media::stopRecording, modifier = Modifier.size(48.dp)) {
            Box(Modifier.size(34.dp).background(MaterialTheme.colorScheme.primary, CircleShape), contentAlignment = Alignment.Center) {
                Icon(Icons.Outlined.Stop, stringResource(R.string.composer_stop_recording), Modifier.size(20.dp),
                    tint = MaterialTheme.colorScheme.onPrimary)
            }
        }
    }
}

@Composable
private fun ComposerFileRow(item: FlightAttachmentDraft, media: FlightComposerMedia, enabled: Boolean, showRemove: Boolean = true) {
    val image = item.mimeType.startsWith("image/") || (item.mimeType.isBlank() &&
        item.name.substringAfterLast('.', "").lowercase(Locale.ROOT) in setOf("jpg", "jpeg", "png", "webp", "gif", "heic", "heif", "bmp"))
    Row(Modifier.fillMaxWidth().padding(start = 6.dp), verticalAlignment = Alignment.CenterVertically) {
        Row(Modifier.weight(1f).heightIn(min = 60.dp).clip(RoundedCornerShape(10.dp))
            .clickable(enabled = !media.isRecording && item.uri.isNotBlank(), onClick = { media.open(item) }).padding(6.dp),
            horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
            if (image) ComposerThumbnail(item.uri)
            else Box(Modifier.size(44.dp), contentAlignment = Alignment.Center) {
                Icon(Icons.Outlined.InsertDriveFile, null, Modifier.size(26.dp), tint = MaterialTheme.colorScheme.primary)
            }
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text(item.name.ifBlank { stringResource(R.string.composer_file) }, style = MaterialTheme.typography.bodySmall,
                    fontWeight = FontWeight.SemiBold, maxLines = 2, overflow = TextOverflow.Ellipsis)
                val extension = item.name.substringAfterLast('.', "").uppercase(Locale.ROOT).take(12)
                Text(if (image) stringResource(R.string.composer_photo) else extension.ifBlank { stringResource(R.string.composer_document) },
                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
        }
        if (showRemove) ComposerRemove(item, media, enabled)
    }
}

@Composable
private fun ComposerAudioRow(item: FlightAttachmentDraft, media: FlightComposerMedia, enabled: Boolean, showRemove: Boolean = true) {
    val selected = media.playingAttachmentId == item.id
    val playing = selected && media.isPlaying
    val duration = if (selected && media.playbackDurationMillis > 0) media.playbackDurationMillis else item.durationMillis
    val position = if (selected) media.playbackPositionMillis.coerceIn(0, duration.coerceAtLeast(0)) else 0L
    val progress = if (duration > 0) position.toFloat() / duration else 0f
    Row(Modifier.fillMaxWidth().padding(start = 6.dp), verticalAlignment = Alignment.CenterVertically) {
        IconButton(onClick = { media.togglePlayback(item) }, enabled = !media.isRecording && item.uri.isNotBlank(), modifier = Modifier.size(48.dp)) {
            Icon(if (playing) Icons.Outlined.Pause else Icons.Outlined.PlayArrow,
                stringResource(if (playing) R.string.composer_pause else R.string.composer_play), Modifier.size(24.dp),
                tint = MaterialTheme.colorScheme.primary)
        }
        Column(Modifier.weight(1f).padding(vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Text(item.name.ifBlank { stringResource(R.string.composer_voice_note) }, style = MaterialTheme.typography.bodySmall,
                fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
            LinearProgressIndicator(progress = { progress }, modifier = Modifier.fillMaxWidth().height(3.dp).clip(CircleShape),
                color = MaterialTheme.colorScheme.primary, trackColor = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.5f))
            val timing = if (duration <= 0) "" else if (selected) "${composerDuration(position)} / ${composerDuration(duration)}" else composerDuration(duration)
            Text(if (timing.isEmpty()) stringResource(R.string.composer_audio) else "${stringResource(R.string.composer_audio)} · $timing",
                style = MaterialTheme.typography.bodySmall.copy(fontFeatureSettings = "tnum"), color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        if (showRemove) ComposerRemove(item, media, enabled)
    }
}

@Composable
private fun ComposerRemove(item: FlightAttachmentDraft, media: FlightComposerMedia, enabled: Boolean) {
    IconButton(onClick = { media.remove(item) }, enabled = enabled && !media.isRecording, modifier = Modifier.size(48.dp)) {
        Icon(Icons.Outlined.Close, stringResource(R.string.composer_remove_attachment, item.name), Modifier.size(18.dp),
            tint = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

@Composable
private fun ComposerMenuItem(icon: ImageVector, label: String, modifier: Modifier, onClick: () -> Unit) {
    Surface(onClick = onClick, modifier = modifier, color = MaterialTheme.colorScheme.surface, shape = RoundedCornerShape(12.dp)) {
        Column(Modifier.fillMaxWidth().heightIn(min = 88.dp).padding(horizontal = 4.dp, vertical = 8.dp),
            horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Box(Modifier.size(42.dp).background(MaterialTheme.colorScheme.primary.copy(alpha = 0.08f), CircleShape), contentAlignment = Alignment.Center) {
                Icon(icon, null, Modifier.size(24.dp), tint = MaterialTheme.colorScheme.primary)
            }
            Text(label, style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.Medium,
                textAlign = TextAlign.Center, maxLines = 2, overflow = TextOverflow.Ellipsis)
        }
    }
}

@Composable
private fun ComposerThumbnail(uri: String) {
    val resolver = LocalContext.current.contentResolver
    val bitmap by produceState<ImageBitmap?>(null, uri, resolver) {
        value = withContext(Dispatchers.IO) { runCatching { composerThumbnail(resolver, uri)?.asImageBitmap() }.getOrNull() }
    }
    Box(Modifier.size(44.dp).clip(RoundedCornerShape(8.dp)), contentAlignment = Alignment.Center) {
        if (bitmap != null) Image(bitmap!!, null, Modifier.size(44.dp), contentScale = ContentScale.Crop)
        else Icon(Icons.Outlined.Image, null, Modifier.size(26.dp), tint = MaterialTheme.colorScheme.primary)
    }
}

/** Bound allocations before decoding; EXIF correction also stays off the UI thread. */
private fun composerThumbnail(resolver: ContentResolver, rawUri: String): Bitmap? {
    val uri = rawUri.toUri()
    val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
    val boundsStream = resolver.openInputStream(uri) ?: return null
    boundsStream.use { BitmapFactory.decodeStream(it, null, bounds) }
    if (bounds.outWidth <= 0 || bounds.outHeight <= 0) return null
    var sample = 1
    while (bounds.outWidth / sample > 256 || bounds.outHeight / sample > 256) sample *= 2
    val bitmap = resolver.openInputStream(uri)?.use {
        BitmapFactory.decodeStream(it, null, BitmapFactory.Options().apply { inSampleSize = sample })
    } ?: return null
    val orientation = runCatching { resolver.openInputStream(uri)?.use {
        ExifInterface(it).getAttributeInt(ExifInterface.TAG_ORIENTATION, ExifInterface.ORIENTATION_NORMAL)
    } }.getOrNull()
    val matrix = Matrix()
    when (orientation) {
        ExifInterface.ORIENTATION_FLIP_HORIZONTAL -> matrix.setScale(-1f, 1f)
        ExifInterface.ORIENTATION_ROTATE_180 -> matrix.setRotate(180f)
        ExifInterface.ORIENTATION_FLIP_VERTICAL -> matrix.setScale(1f, -1f)
        ExifInterface.ORIENTATION_TRANSPOSE -> { matrix.setRotate(90f); matrix.postScale(-1f, 1f) }
        ExifInterface.ORIENTATION_ROTATE_90 -> matrix.setRotate(90f)
        ExifInterface.ORIENTATION_TRANSVERSE -> { matrix.setRotate(270f); matrix.postScale(-1f, 1f) }
        ExifInterface.ORIENTATION_ROTATE_270 -> matrix.setRotate(270f)
        else -> return bitmap
    }
    return Bitmap.createBitmap(bitmap, 0, 0, bitmap.width, bitmap.height, matrix, true).also { if (it !== bitmap) bitmap.recycle() }
}

private fun composerAttachmentIsAudio(item: FlightAttachmentDraft): Boolean = item.mimeType.startsWith("audio/") ||
    (item.mimeType.isBlank() && item.name.substringAfterLast('.', "").lowercase(Locale.ROOT) in setOf("m4a", "mp3", "aac", "wav", "ogg", "opus"))

private fun composerDuration(millis: Long): String {
    val seconds = millis.coerceAtLeast(0) / 1000
    return String.format(Locale.ROOT, "%d:%02d", seconds / 60, seconds % 60)
}
