package com.nags.operations.ui.components

import android.content.Context
import android.net.Uri
import android.util.Base64
import com.nags.operations.data.TaskAttachmentKindValue
import com.nags.operations.ui.workorder.TaskAttachmentDraft
import java.io.ByteArrayOutputStream
import java.time.OffsetDateTime

/**
 * Reads [uri] into a base64 [TaskAttachmentDraft]. Shared by photo, voice cache files,
 * and the document picker.
 */
internal fun captureAttachmentInternal(context: Context, uri: Uri, kind: String): TaskAttachmentDraft? {
    if (kind == TaskAttachmentKindValue.Image) {
        return compressImageToDraft(context, uri)
    }
    val resolver = context.contentResolver
    return runCatching {
        val maxBytes = when (kind) {
            TaskAttachmentKindValue.Image -> 5L * 1024 * 1024
            TaskAttachmentKindValue.Voice -> 2L * 1024 * 1024
            TaskAttachmentKindValue.Document -> 10L * 1024 * 1024
            else -> 5L * 1024 * 1024
        }
        val bytes = resolver.openInputStream(uri)?.use { input ->
            val buffer = ByteArrayOutputStream()
            val chunk = ByteArray(8 * 1024)
            var total = 0L
            while (true) {
                val n = input.read(chunk)
                if (n <= 0) break
                total += n
                if (total > maxBytes) return@use null
                buffer.write(chunk, 0, n)
            }
            buffer.toByteArray()
        } ?: return@runCatching null
        val type = resolver.getType(uri) ?: when (kind) {
            TaskAttachmentKindValue.Image -> "image/jpeg"
            TaskAttachmentKindValue.Voice -> "audio/mp4"
            else -> "application/octet-stream"
        }
        val name = uri.lastPathSegment ?: when (kind) {
            TaskAttachmentKindValue.Image -> "photo.jpg"
            TaskAttachmentKindValue.Voice -> "voice.m4a"
            else -> "document"
        }
        TaskAttachmentDraft(
            kind = kind,
            contentType = type,
            fileName = name,
            base64 = Base64.encodeToString(bytes, Base64.NO_WRAP),
            capturedAtIso = OffsetDateTime.now().toString(),
            sizeBytes = bytes.size.toLong(),
        )
    }.getOrNull()
}
