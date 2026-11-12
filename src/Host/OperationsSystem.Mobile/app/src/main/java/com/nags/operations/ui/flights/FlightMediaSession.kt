package com.nags.operations.ui.flights

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.media.MediaRecorder
import androidx.core.net.toUri
import android.os.Build
import android.os.SystemClock
import androidx.core.content.ContextCompat
import androidx.core.content.FileProvider
import java.io.File
import java.io.IOException
import java.util.UUID

/** Shares only explicitly granted temporary work-media files with the system camera. */
class FlightMediaFileProvider : FileProvider()

internal data class FlightCapturedMedia(
    val uri: String,
    val name: String,
    val mimeType: String,
    val file: File,
)

/** One temporary form's media directory; this never operates on another session or picked document. */
internal class FlightMediaSession(context: Context, val id: String) {
    private val appContext = context.applicationContext
    private val authority = "${appContext.packageName}.flightmedia"
    private val mediaRoot = File(appContext.cacheDir.canonicalFile, "flight-work-media")
    private val directory = File(mediaRoot, id).canonicalFile
    private var audioOwner: Any? = null
    private var stopActiveAudio: (() -> Unit)? = null
    var hasActiveRecording: Boolean = false
        private set

    init {
        require(mediaRoot.canonicalFile == mediaRoot) { "Temporary media root must be within the app cache." }
        require(id.isNotBlank() && directory.name == id && directory.parentFile == mediaRoot) {
            "Temporary media session must have a single directory name."
        }
    }

    fun createPhoto(): FlightCapturedMedia = create("jpg", "image/jpeg")

    fun createVoice(): FlightCapturedMedia = create("m4a", "audio/mp4")

    fun fileFor(uri: String): File? = runCatching {
        val parsed = uri.toUri()
        val segments = parsed.pathSegments
        if (parsed.scheme != "content" || parsed.authority != authority || segments.size != 3 ||
            segments[0] != "flight_work_media" || segments[1] != id) return@runCatching null
        File(directory, segments[2]).canonicalFile.takeIf { it.parentFile == directory && it.isFile }
    }.getOrNull()

    fun size(uri: String): Long = fileFor(uri)?.length() ?: 0L

    fun delete(uri: String) {
        runCatching { fileFor(uri)?.delete() }
    }

    /** A form has several composers, but only one active recording or preview at a time. */
    fun claimAudio(owner: Any, recording: Boolean = false, stop: () -> Unit) {
        if (audioOwner !== owner) stopAudio()
        audioOwner = owner
        hasActiveRecording = recording
        stopActiveAudio = stop
    }

    fun releaseAudio(owner: Any) {
        if (audioOwner === owner) {
            audioOwner = null
            hasActiveRecording = false
            stopActiveAudio = null
        }
    }

    fun stopAudio() {
        val stop = stopActiveAudio
        audioOwner = null
        hasActiveRecording = false
        stopActiveAudio = null
        stop?.invoke()
    }

    fun clear() {
        stopAudio()
        // Captures are direct children. Never recurse through another directory or symlink.
        runCatching {
            if (directory.canonicalFile != directory) return@runCatching
            directory.listFiles()?.forEach { file ->
                if (file.canonicalFile.parentFile == directory && file.isFile) file.delete()
            }
            directory.delete()
        }
    }

    private fun create(extension: String, mimeType: String): FlightCapturedMedia {
        if ((!directory.mkdirs() && !directory.isDirectory) || directory.canonicalFile != directory) {
            throw IOException("Could not create the temporary media directory.")
        }
        val file = File(directory, "${UUID.randomUUID()}.$extension").canonicalFile
        if (file.parentFile != directory || !file.createNewFile()) throw IOException("Could not create temporary media.")
        try {
            val uri = FileProvider.getUriForFile(appContext, authority, file)
            return FlightCapturedMedia(uri.toString(), file.name, mimeType, file)
        } catch (failure: Throwable) {
            file.delete()
            throw failure
        }
    }
}

/** Foreground recording only; the owning form supplies lifecycle cancellation. */
internal class FlightVoiceRecorder(context: Context, private val session: FlightMediaSession) {
    private val appContext = context.applicationContext
    private var recorder: MediaRecorder? = null
    private var pendingMedia: FlightCapturedMedia? = null
    private var startedAtMillis: Long? = null
    private var lastElapsedMillis = 0L

    val isRecording: Boolean
        get() = recorder != null

    var hasFailure: Boolean = false
        private set

    val elapsedMillis: Long
        get() = startedAtMillis?.let { (SystemClock.elapsedRealtime() - it).coerceAtLeast(0L) } ?: lastElapsedMillis

    val amplitude: Int
        get() = runCatching { recorder?.maxAmplitude ?: 0 }.getOrDefault(0)

    fun start() {
        check(recorder == null) { "A recording is already active." }
        hasFailure = false
        if (ContextCompat.checkSelfPermission(appContext, Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
            throw SecurityException("Microphone permission is required.")
        }
        lastElapsedMillis = 0L
        val media = session.createVoice()
        var candidate: MediaRecorder? = null
        var started = false
        try {
            candidate = createRecorder()
            candidate.setOnErrorListener { failedRecorder, _, _ ->
                if (recorder === failedRecorder) {
                    cancel()
                    hasFailure = true
                }
            }
            candidate.setAudioSource(MediaRecorder.AudioSource.MIC)
            candidate.setOutputFormat(MediaRecorder.OutputFormat.MPEG_4)
            candidate.setAudioEncoder(MediaRecorder.AudioEncoder.AAC)
            candidate.setOutputFile(media.file.absolutePath)
            candidate.prepare()
            candidate.start()
            recorder = candidate
            pendingMedia = media
            startedAtMillis = SystemClock.elapsedRealtime()
            started = true
        } finally {
            if (!started) {
                runCatching { candidate?.release() }
                session.delete(media.uri)
                hasFailure = true
            }
        }
    }

    fun finish(): FlightCapturedMedia? {
        val active = recorder ?: return null
        val media = pendingMedia
        lastElapsedMillis = elapsedMillis
        recorder = null
        pendingMedia = null
        startedAtMillis = null
        var valid = false
        try {
            // stop() may throw for a recording that has not yet produced valid audio.
            active.stop()
            if (media != null && session.size(media.uri) > 0L) {
                valid = true
                return media
            }
            return null
        } catch (_: RuntimeException) {
            return null
        } finally {
            runCatching { active.release() }
            if (!valid) {
                media?.let { session.delete(it.uri) }
                lastElapsedMillis = 0L
                hasFailure = true
            }
        }
    }

    fun cancel() {
        val active = recorder
        val media = pendingMedia
        recorder = null
        pendingMedia = null
        startedAtMillis = null
        lastElapsedMillis = 0L
        hasFailure = false
        runCatching { active?.release() }
        media?.let { session.delete(it.uri) }
    }

    @Suppress("DEPRECATION")
    private fun createRecorder(): MediaRecorder = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
        MediaRecorder(appContext)
    } else {
        MediaRecorder()
    }
}
