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
        val resolvedType = resolver.getType(uri) ?: when (kind) {
            TaskAttachmentKindValue.Image -> "image/jpeg"
            TaskAttachmentKindValue.Voice -> "audio/mp4"
            TaskAttachmentKindValue.Document -> "application/pdf"
            else -> "application/octet-stream"
        }
        // Voice recordings created by TaskAttachmentPickers use an MPEG-4 container. Some
        // FileProvider/MIME databases report .m4a inconsistently, so send the canonical type
        // only after verifying that the recorder produced a complete MPEG-4 file.
        val type = if (kind == TaskAttachmentKindValue.Voice) "audio/mp4" else resolvedType
        val name = uri.lastPathSegment ?: when (kind) {
            TaskAttachmentKindValue.Image -> "photo.jpg"
            TaskAttachmentKindValue.Voice -> "voice.m4a"
            else -> "document"
        }
        if (kind == TaskAttachmentKindValue.Voice && !bytes.hasMp4ContainerSignature()) {
            return@runCatching null
        }
        if (kind == TaskAttachmentKindValue.Document &&
            (type != "application/pdf" || !bytes.hasPdfSignature())
        ) {
            return@runCatching null
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

private fun ByteArray.hasPdfSignature(): Boolean =
    size >= 5 && this[0] == 0x25.toByte() && this[1] == 0x50.toByte() &&
        this[2] == 0x44.toByte() && this[3] == 0x46.toByte() && this[4] == 0x2D.toByte()

internal fun ByteArray.hasMp4ContainerSignature(): Boolean =
    size >= 12 && this[4] == 0x66.toByte() && this[5] == 0x74.toByte() &&
        this[6] == 0x79.toByte() && this[7] == 0x70.toByte()
