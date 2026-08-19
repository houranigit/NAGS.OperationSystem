package com.nags.operations.ui.components

import android.content.Context
import com.nags.operations.data.TaskAttachmentKindValue
import com.nags.operations.ui.workorder.TaskAttachmentDraft
import java.io.File
import java.util.Base64
import java.util.Locale
import java.util.UUID

private const val PreviewDirectory = "attachment-previews"

internal fun materializeAttachmentPreview(
    context: Context,
    attachment: TaskAttachmentDraft,
): File? {
    var pendingFile: File? = null
    return runCatching {
        val content = attachment.decodePreviewContent() ?: return@runCatching null
        val directory = File(context.cacheDir, PreviewDirectory).apply { mkdirs() }
        // Never expose the employee's original filename or a content-derived fingerprint in cache.
        File(directory, attachment.previewFileName()).also { file ->
            pendingFile = file
            file.outputStream().use { output -> output.write(content) }
        }
    }.getOrElse {
        pendingFile?.let(::deleteMaterializedAttachmentPreview)
        null
    }
}

internal fun removeMaterializedAttachmentPreview(
    context: Context,
    @Suppress("UNUSED_PARAMETER")
    attachment: TaskAttachmentDraft,
) {
    cleanupAllAttachmentPreviews(context)
}

internal fun deleteMaterializedAttachmentPreview(file: File) {
    val directory = file.parentFile ?: return
    if (directory.name != PreviewDirectory || !file.name.startsWith("preview-")) return
    file.delete()
    if (directory.listFiles().orEmpty().isEmpty()) directory.delete()
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

internal enum class AttachmentPreviewKind {
    Image,
    Voice,
    Pdf,
    Unsupported,
}

internal fun TaskAttachmentDraft.attachmentPreviewKind(): AttachmentPreviewKind = when (kind) {
    TaskAttachmentKindValue.Image -> AttachmentPreviewKind.Image
    TaskAttachmentKindValue.Voice -> AttachmentPreviewKind.Voice
    TaskAttachmentKindValue.Document -> AttachmentPreviewKind.Pdf
    else -> AttachmentPreviewKind.Unsupported
}

internal fun TaskAttachmentDraft.attachmentKindLabel(): String = when (attachmentPreviewKind()) {
    AttachmentPreviewKind.Image -> "Photo"
    AttachmentPreviewKind.Voice -> "Voice note"
    AttachmentPreviewKind.Pdf -> "PDF document"
    AttachmentPreviewKind.Unsupported -> "Attachment"
}

internal fun formatAttachmentBytes(size: Long): String {
    val safeSize = size.coerceAtLeast(0L)
    return when {
        safeSize < 1_024L -> "$safeSize B"
        safeSize < 1_024L * 1_024L -> "${safeSize / 1_024L} KB"
        else -> String.format(Locale.US, "%.1f MB", safeSize / (1_024.0 * 1_024.0))
    }
}

internal fun formatAttachmentDuration(positionMs: Int): String {
    val totalSeconds = positionMs.coerceAtLeast(0) / 1_000
    return "%d:%02d".format(Locale.US, totalSeconds / 60, totalSeconds % 60)
}
