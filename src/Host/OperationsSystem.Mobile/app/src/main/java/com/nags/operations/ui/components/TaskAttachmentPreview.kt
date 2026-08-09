package com.nags.operations.ui.components

import android.content.Context
import android.net.Uri
import androidx.core.content.FileProvider
import com.nags.operations.data.TaskAttachmentKindValue
import com.nags.operations.ui.workorder.TaskAttachmentDraft
import java.io.File
import java.util.Base64
import java.util.UUID

private const val PreviewDirectory = "attachment-previews"

internal fun materializeAttachmentPreview(
    context: Context,
    attachment: TaskAttachmentDraft,
): Uri? = runCatching {
    val content = attachment.decodePreviewContent() ?: return@runCatching null
    val directory = File(context.cacheDir, PreviewDirectory).apply { mkdirs() }
    // Never expose the employee's original filename or a content-derived fingerprint in cache.
    val file = File(directory, attachment.previewFileName())
    file.outputStream().use { output -> output.write(content) }
    FileProvider.getUriForFile(
        context,
        "${context.packageName}.fileprovider",
        file,
    )
}.getOrNull()

internal fun removeMaterializedAttachmentPreview(
    context: Context,
    @Suppress("UNUSED_PARAMETER")
    attachment: TaskAttachmentDraft,
) {
    cleanupAllAttachmentPreviews(context)
}

internal fun cleanupAttachmentPreviews(
    context: Context,
    maxAgeMillis: Long = 24L * 60 * 60 * 1_000,
    nowEpochMillis: Long = System.currentTimeMillis(),
): Int = cleanupAttachmentPreviewDirectory(
    directory = File(context.cacheDir, PreviewDirectory),
    olderThanOrEqualToEpochMillis = nowEpochMillis - maxAgeMillis.coerceAtLeast(0L),
)

internal fun cleanupAllAttachmentPreviews(context: Context): Int =
    cleanupAttachmentPreviewDirectory(
        directory = File(context.cacheDir, PreviewDirectory),
        olderThanOrEqualToEpochMillis = Long.MAX_VALUE,
    )

/** File-only helper keeps privacy cleanup testable without an Android runtime. */
internal fun cleanupAttachmentPreviewDirectory(
    directory: File,
    olderThanOrEqualToEpochMillis: Long,
): Int {
    if (!directory.exists()) return 0
    var removed = 0
    directory.listFiles().orEmpty().forEach { file ->
        if (file.isFile && file.lastModified() <= olderThanOrEqualToEpochMillis && file.delete()) {
            removed += 1
        }
    }
    if (directory.listFiles().orEmpty().isEmpty()) directory.delete()
    return removed
}

internal fun TaskAttachmentDraft.decodePreviewContent(): ByteArray? {
    val maxBytes = when (kind) {
        TaskAttachmentKindValue.Image -> 5L * 1024 * 1024
        TaskAttachmentKindValue.Voice -> 2L * 1024 * 1024
        TaskAttachmentKindValue.Document -> 10L * 1024 * 1024
        else -> return null
    }
    if (sizeBytes !in 1..maxBytes) return null

    val maximumEncodedLength = ((maxBytes + 2) / 3) * 4 + 16
    if (base64.length > maximumEncodedLength) return null

    val decoded = runCatching { Base64.getDecoder().decode(base64) }.getOrNull() ?: return null
    return decoded.takeIf { it.size.toLong() == sizeBytes }
}

internal fun TaskAttachmentDraft.previewContentType(): String = when (kind) {
    TaskAttachmentKindValue.Image -> when (contentType.lowercase()) {
        "image/png" -> "image/png"
        "image/webp" -> "image/webp"
        else -> "image/jpeg"
    }
    TaskAttachmentKindValue.Voice -> "audio/mp4"
    TaskAttachmentKindValue.Document -> "application/pdf"
    else -> "application/octet-stream"
}

internal fun TaskAttachmentDraft.previewFileName(
    randomToken: String = UUID.randomUUID().toString(),
): String {
    val suffix = when (previewContentType()) {
        "image/png" -> ".png"
        "image/webp" -> ".webp"
        "image/jpeg" -> ".jpg"
        "audio/mp4" -> ".m4a"
        "application/pdf" -> ".pdf"
        else -> ".bin"
    }
    val safeToken = randomToken.replace(Regex("[^A-Za-z0-9_-]"), "").take(64)
    require(safeToken.isNotBlank()) { "Preview token is required." }
    return "preview-$safeToken$suffix"
}
