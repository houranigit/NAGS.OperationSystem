package com.nags.operations.ui.flights

import android.Manifest
import android.app.Activity
import android.content.ClipData
import android.content.Context
import android.content.ContextWrapper
import android.content.Intent
import android.content.pm.PackageManager
import android.media.AudioAttributes
import android.media.AudioFocusRequest
import android.media.AudioManager
import android.media.MediaPlayer
import android.net.Uri
import android.os.Handler
import android.os.Looper
import android.provider.OpenableColumns
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.SideEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalResources
import androidx.core.content.ContextCompat
import androidx.core.net.toUri
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.nags.operations.R
import java.io.File
import java.util.UUID
import java.time.LocalDateTime
import java.time.format.DateTimeFormatter
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext

internal val LocalFlightMediaSession = staticCompositionLocalOf<FlightMediaSession> {
    error("Flight media belongs to the open operations form.")
}

/** Keep completed media through configuration recreation, and clear it when the form leaves. */
@Composable
internal fun rememberFlightMediaSession(): FlightMediaSession {
    val context = LocalContext.current
    val activity = context.findActivity()
    val id = rememberSaveable { UUID.randomUUID().toString() }
    val session = remember(id) { FlightMediaSession(context.applicationContext, id) }
    LaunchedEffect(session) {
        withContext(Dispatchers.IO) {
            // A process kill cannot run disposal. Only prune old, abandoned private capture folders.
            val root = File(context.applicationContext.cacheDir, "flight-work-media").canonicalFile
            val cutoff = System.currentTimeMillis() - 24 * 60 * 60 * 1000L
            root.listFiles()?.forEach { directory ->
                if (directory.name != id && directory.isDirectory && directory.canonicalFile.parentFile == root &&
                    directory.lastModified() < cutoff && runCatching { UUID.fromString(directory.name) }.isSuccess) {
                    FlightMediaSession(context.applicationContext, directory.name).clear()
                }
            }
        }
    }
    DisposableEffect(session, activity) {
        onDispose { if (activity?.isChangingConfigurations != true) session.clear() }
    }
    return session
}

@Composable
internal fun rememberFlightComposerMedia(
    attachments: List<FlightAttachmentDraft>,
    onAttachmentsChange: (List<FlightAttachmentDraft>) -> Unit,
): FlightComposerMedia {
    val context = LocalContext.current
    val resources = LocalResources.current
    val activity = context.findActivity()
    val session = LocalFlightMediaSession.current
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    var currentItems by remember { mutableStateOf(attachments) }
    SideEffect { currentItems = attachments }
    val latestChange by rememberUpdatedState(onAttachmentsChange)
    val audioOwner = remember { Any() }
    val recorder = remember(session) { FlightVoiceRecorder(context.applicationContext, session) }
    val audioManager = remember(context) { context.getSystemService(Context.AUDIO_SERVICE) as AudioManager }
    var recording by remember { mutableStateOf(false) }
    var elapsed by remember { mutableLongStateOf(0L) }
    var message by remember { mutableStateOf<String?>(null) }
    var pendingDocuments by rememberSaveable { mutableStateOf(emptyList<String>()) }
    val importing = pendingDocuments.isNotEmpty()
    var cameraUri by rememberSaveable { mutableStateOf<String?>(null) }
    var cameraName by rememberSaveable { mutableStateOf("") }
    var player by remember { mutableStateOf<MediaPlayer?>(null) }
    var loadedAttachmentId by remember { mutableStateOf<String?>(null) }
    var playing by remember { mutableStateOf(false) }
    var preparing by remember { mutableStateOf(false) }
    var position by remember { mutableLongStateOf(0L) }
    var duration by remember { mutableLongStateOf(0L) }
    val audioAttributes = remember {
        AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_MEDIA).setContentType(AudioAttributes.CONTENT_TYPE_SPEECH).build()
    }
    val focusRequest = remember(audioManager) {
        AudioFocusRequest.Builder(AudioManager.AUDIOFOCUS_GAIN_TRANSIENT).setAudioAttributes(audioAttributes)
            .setWillPauseWhenDucked(true).setOnAudioFocusChangeListener({ change ->
                if (change <= AudioManager.AUDIOFOCUS_LOSS) {
                    runCatching { player?.pause() }
                    playing = false
                }
            }, Handler(Looper.getMainLooper())).build()
    }

    fun releasePlayer() {
        player?.let { active ->
            active.setOnPreparedListener(null)
            active.setOnCompletionListener(null)
            active.setOnErrorListener(null)
            runCatching { active.release() }
        }
        player = null
        loadedAttachmentId = null
        playing = false
        preparing = false
        position = 0
        duration = 0
        audioManager.abandonAudioFocusRequest(focusRequest)
        session.releaseAudio(audioOwner)
    }

    fun claimAudio(isRecording: Boolean = false) {
        session.claimAudio(audioOwner, recording = isRecording) {
            recorder.cancel()
            recording = false
            elapsed = 0
            releasePlayer()
        }
    }

    fun changeItems(next: List<FlightAttachmentDraft>) {
        currentItems = next
        latestChange(next)
    }

    fun append(item: FlightAttachmentDraft) {
        if (currentItems.size < 10) changeItems(currentItems + item)
        else {
            if (item.ownedCapture) session.delete(item.uri)
            message = resources.getString(R.string.composer_limit)
        }
    }

    fun beginRecording() {
        if (recording || importing || currentItems.size >= 10 || !lifecycle.currentState.isAtLeast(Lifecycle.State.STARTED)) return
        session.stopAudio()
        releasePlayer()
        claimAudio(isRecording = true)
        message = null
        try {
            recorder.start()
            elapsed = 0
            recording = true
        } catch (_: Exception) {
            recorder.cancel()
            session.releaseAudio(audioOwner)
            message = resources.getString(R.string.composer_record_failed)
        }
    }

    val microphonePermission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) beginRecording()
        else message = resources.getString(R.string.composer_mic_denied)
    }

    fun importDocuments(uris: List<Uri>) {
        val slots = (10 - currentItems.size).coerceAtLeast(0)
        if (uris.size > slots) message = resources.getString(R.string.composer_limit)
        pendingDocuments = uris.take(slots).map(Uri::toString)
    }

    LaunchedEffect(pendingDocuments) {
        val requested = pendingDocuments
        if (requested.isEmpty()) return@LaunchedEffect
        var completed = false
        try {
            val slots = (10 - currentItems.size).coerceAtLeast(0)
            val fallback = resources.getString(R.string.work_attachment_name)
            val selected = withContext(Dispatchers.IO) {
                requested.take(slots).mapNotNull { raw -> runCatching { readComposerDocument(context, raw.toUri(), fallback) }.getOrNull() }
            }
            val remaining = (10 - currentItems.size).coerceAtLeast(0)
            changeItems(currentItems + selected.take(remaining))
            if (selected.size < requested.take(slots).size) message = resources.getString(R.string.composer_file_failed)
            else if (requested.size > slots || selected.size > remaining) message = resources.getString(R.string.composer_limit)
            completed = true
        } catch (cancelled: CancellationException) {
            // Keep the URI list for the recreated composition to finish the metadata lookup.
            throw cancelled
        } catch (_: Exception) {
            message = resources.getString(R.string.composer_file_failed)
            completed = true
        } finally {
            if (completed && pendingDocuments == requested) pendingDocuments = emptyList()
        }
    }

    val documents = rememberLauncherForActivityResult(ActivityResultContracts.OpenMultipleDocuments(), ::importDocuments)
    val images = rememberLauncherForActivityResult(ActivityResultContracts.OpenMultipleDocuments(), ::importDocuments)
    val camera = rememberLauncherForActivityResult(ActivityResultContracts.TakePicture()) { accepted ->
        val uri = cameraUri
        val name = cameraName
        cameraUri = null
        cameraName = ""
        if (uri != null) {
            if (accepted && session.size(uri) > 0) append(FlightAttachmentDraft(uri = uri, name = name,
                mimeType = "image/jpeg", ownedCapture = true))
            else {
                session.delete(uri)
                if (accepted) message = resources.getString(R.string.composer_camera_failed)
            }
        }
    }

    DisposableEffect(lifecycle, recorder, session) {
        val observer = LifecycleEventObserver { _, event ->
            if (event == Lifecycle.Event.ON_STOP) {
                recorder.cancel()
                recording = false
                elapsed = 0
                releasePlayer()
            }
        }
        lifecycle.addObserver(observer)
        onDispose {
            lifecycle.removeObserver(observer)
            recorder.cancel()
            releasePlayer()
            if (activity?.isChangingConfigurations != true) cameraUri?.let(session::delete)
        }
    }

    LaunchedEffect(recording) {
        while (recording) {
            if (recorder.hasFailure || !recorder.isRecording) {
                recording = false
                elapsed = 0
                session.releaseAudio(audioOwner)
                if (recorder.hasFailure) message = resources.getString(R.string.composer_record_failed)
            } else elapsed = recorder.elapsedMillis
            delay(200)
        }
    }
    LaunchedEffect(playing) {
        while (playing) {
            position = runCatching { player?.currentPosition?.toLong() ?: 0L }.getOrDefault(0L)
            delay(200)
        }
    }

    fun mayAdd(): Boolean = !recording && !importing && cameraUri == null && currentItems.size < 10
    fun mediaAction(action: () -> Unit) {
        if (!mayAdd()) return
        message = null
        session.stopAudio()
        releasePlayer()
        try { action() } catch (_: Exception) { message = resources.getString(R.string.composer_file_failed) }
    }

    return object : FlightComposerMedia {
        override val isRecording get() = recording
        override val recordingMillis get() = elapsed
        override val error get() = message
        override val playingAttachmentId get() = loadedAttachmentId
        override val isPlaying get() = playing
        override val playbackPositionMillis get() = position
        override val playbackDurationMillis get() = duration
        override val canAdd get() = mayAdd()
        override fun dismissError() { message = null }
        override fun pickFiles() = mediaAction { documents.launch(arrayOf("*/*")) }
        override fun pickImages() = mediaAction { images.launch(arrayOf("image/*")) }
        override fun takePhoto() {
            if (!mayAdd()) return
            message = null
            session.stopAudio()
            releasePlayer()
            try {
                val capture = session.createPhoto()
                cameraUri = capture.uri
                cameraName = captureName(resources.getString(R.string.composer_photo), "jpg")
                camera.launch(capture.uri.toUri())
            } catch (_: Exception) {
                cameraUri?.let(session::delete)
                cameraUri = null
                cameraName = ""
                message = resources.getString(R.string.composer_camera_failed)
            }
        }
        override fun startRecording() {
            if (!mayAdd()) return
            if (ContextCompat.checkSelfPermission(context, Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED) beginRecording()
            else microphonePermission.launch(Manifest.permission.RECORD_AUDIO)
        }
        override fun stopRecording() {
            if (!recording) return
            recording = false
            val capture = recorder.finish()
            val recordedDuration = recorder.elapsedMillis
            session.releaseAudio(audioOwner)
            elapsed = 0
            if (capture != null) append(FlightAttachmentDraft(uri = capture.uri, name = captureName(resources.getString(R.string.composer_voice_note), "m4a"),
                mimeType = capture.mimeType, durationMillis = recordedDuration, ownedCapture = true))
            else message = resources.getString(R.string.composer_record_failed)
        }
        override fun cancelRecording() {
            recorder.cancel()
            session.releaseAudio(audioOwner)
            recording = false
            elapsed = 0
        }
        override fun remove(item: FlightAttachmentDraft) {
            if (loadedAttachmentId == item.id) releasePlayer()
            // The form owns cleanup across all service/task lists, so shared references remain valid.
            changeItems(currentItems.filterNot { it.id == item.id })
        }
        override fun open(item: FlightAttachmentDraft) {
            session.stopAudio()
            releasePlayer()
            try {
                val uri = item.uri.toUri()
                val mime = item.mimeType.ifBlank { context.contentResolver.getType(uri) ?: "application/octet-stream" }
                context.startActivity(Intent(Intent.ACTION_VIEW).setDataAndType(uri, mime)
                    .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION).apply { clipData = ClipData.newRawUri(item.name, uri) })
            } catch (_: Exception) { message = resources.getString(R.string.composer_open_failed) }
        }
        override fun togglePlayback(item: FlightAttachmentDraft) {
            if (recording) return
            message = null
            if (loadedAttachmentId == item.id && player != null) {
                if (preparing) { releasePlayer(); return }
                val active = player ?: return
                try {
                    if (playing) {
                        active.pause()
                        position = active.currentPosition.toLong()
                        playing = false
                        audioManager.abandonAudioFocusRequest(focusRequest)
                        session.releaseAudio(audioOwner)
                    } else if (audioManager.requestAudioFocus(focusRequest) == AudioManager.AUDIOFOCUS_REQUEST_GRANTED) {
                        claimAudio()
                        if (position >= duration) active.seekTo(0)
                        active.start()
                        playing = true
                    }
                } catch (_: Exception) { releasePlayer(); message = resources.getString(R.string.composer_play_failed) }
                return
            }
            session.stopAudio()
            releasePlayer()
            claimAudio()
            val candidate = MediaPlayer()
            player = candidate
            loadedAttachmentId = item.id
            preparing = true
            try {
                candidate.setAudioAttributes(audioAttributes)
                candidate.setDataSource(context, item.uri.toUri())
                candidate.setOnPreparedListener { ready ->
                    if (player === ready && lifecycle.currentState.isAtLeast(Lifecycle.State.STARTED)) {
                        preparing = false
                        duration = ready.duration.toLong().coerceAtLeast(0)
                        if (audioManager.requestAudioFocus(focusRequest) == AudioManager.AUDIOFOCUS_REQUEST_GRANTED) {
                            ready.start()
                            playing = true
                        } else { releasePlayer(); message = resources.getString(R.string.composer_play_failed) }
                    }
                }
                candidate.setOnCompletionListener {
                    if (player === it) {
                        playing = false
                        position = duration
                        audioManager.abandonAudioFocusRequest(focusRequest)
                        session.releaseAudio(audioOwner)
                    }
                }
                candidate.setOnErrorListener { failed, _, _ ->
                    if (player === failed) { releasePlayer(); message = resources.getString(R.string.composer_play_failed) }
                    true
                }
                candidate.prepareAsync()
            } catch (_: Exception) { releasePlayer(); message = resources.getString(R.string.composer_play_failed) }
        }
    }
}

private fun readComposerDocument(context: Context, uri: Uri, fallbackName: String): FlightAttachmentDraft {
    require(uri.scheme == "content")
    val resolver = context.contentResolver
    var name = fallbackName
    resolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE), null, null, null)?.use { cursor ->
        if (cursor.moveToFirst()) {
            val nameIndex = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME)
            if (nameIndex >= 0) name = cursor.getString(nameIndex)?.takeIf(String::isNotBlank) ?: name
            val sizeIndex = cursor.getColumnIndex(OpenableColumns.SIZE)
            require(sizeIndex < 0 || cursor.isNull(sizeIndex) || cursor.getLong(sizeIndex) != 0L)
        }
    }
    return FlightAttachmentDraft(uri = uri.toString(), name = name, mimeType = resolver.getType(uri).orEmpty())
}

private fun Context.findActivity(): Activity? {
    var current = this
    while (current is ContextWrapper) {
        if (current is Activity) return current
        val next = current.baseContext
        if (next === current) return null
        current = next
    }
    return current as? Activity
}

private fun captureName(label: String, extension: String): String =
    "$label ${LocalDateTime.now().format(DateTimeFormatter.ofPattern("yyyy-MM-dd HH-mm-ss"))}.$extension"
