package com.nags.operations.ui.components

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Matrix
import android.media.ExifInterface
import android.net.Uri
import android.util.Base64
import com.nags.operations.data.TaskAttachmentKindValue
import com.nags.operations.ui.workorder.TaskAttachmentDraft
import java.io.ByteArrayOutputStream
import java.time.OffsetDateTime

// Long edge after downscaling; keeps detail readable while shedding most pixels.
private const val MaxDimensionPx = 1920

// Stop compressing once the encoded JPEG is at or below this size.
private const val TargetBytes = 1L * 1024 * 1024

// Quality sweep bounds for the JPEG re-encode.
private const val StartQuality = 85
private const val MinQuality = 50
private const val QualityStep = 10

/**
 * Decodes [uri] into a downscaled, EXIF-corrected, JPEG-recompressed
 * [TaskAttachmentDraft]. Aims to keep images visually good while landing well
 * under the server's size cap and shrinking the base64 payload, draft rows, and
 * outbox files. Returns null on any decode/encode failure.
 */
internal fun compressImageToDraft(context: Context, uri: Uri): TaskAttachmentDraft? {
    val resolver = context.contentResolver
    return runCatching {
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        resolver.openInputStream(uri)?.use { BitmapFactory.decodeStream(it, null, bounds) }
            ?: return@runCatching null
        if (bounds.outWidth <= 0 || bounds.outHeight <= 0) return@runCatching null

        val decodeOptions = BitmapFactory.Options().apply {
            inSampleSize = computeInSampleSize(bounds.outWidth, bounds.outHeight, MaxDimensionPx)
        }
        val decoded = resolver.openInputStream(uri)?.use {
            BitmapFactory.decodeStream(it, null, decodeOptions)
        } ?: return@runCatching null

        val orientation = readExifOrientation(context, uri)
        val rotated = applyOrientation(decoded, orientation)
        val scaled = scaleToMaxDimension(rotated, MaxDimensionPx)

        val bytes = encodeJpegToTarget(scaled, TargetBytes)
        if (scaled !== decoded) scaled.recycle()
        decoded.recycle()

        TaskAttachmentDraft(
            kind = TaskAttachmentKindValue.Image,
            contentType = "image/jpeg",
            fileName = "photo_${System.currentTimeMillis()}.jpg",
            base64 = Base64.encodeToString(bytes, Base64.NO_WRAP),
            capturedAtIso = OffsetDateTime.now().toString(),
            sizeBytes = bytes.size.toLong(),
        )
    }.getOrNull()
}

private fun computeInSampleSize(width: Int, height: Int, maxDimension: Int): Int {
    var sample = 1
    var longEdge = maxOf(width, height)
    while (longEdge / 2 >= maxDimension) {
        longEdge /= 2
        sample *= 2
    }
    return sample
}

private fun readExifOrientation(context: Context, uri: Uri): Int =
    runCatching {
        context.contentResolver.openInputStream(uri)?.use { input ->
            ExifInterface(input).getAttributeInt(
                ExifInterface.TAG_ORIENTATION,
                ExifInterface.ORIENTATION_NORMAL,
            )
        } ?: ExifInterface.ORIENTATION_NORMAL
    }.getOrDefault(ExifInterface.ORIENTATION_NORMAL)

private fun applyOrientation(bitmap: Bitmap, orientation: Int): Bitmap {
    val matrix = Matrix()
    when (orientation) {
        ExifInterface.ORIENTATION_ROTATE_90 -> matrix.postRotate(90f)
        ExifInterface.ORIENTATION_ROTATE_180 -> matrix.postRotate(180f)
        ExifInterface.ORIENTATION_ROTATE_270 -> matrix.postRotate(270f)
        ExifInterface.ORIENTATION_FLIP_HORIZONTAL -> matrix.preScale(-1f, 1f)
        ExifInterface.ORIENTATION_FLIP_VERTICAL -> matrix.preScale(1f, -1f)
        ExifInterface.ORIENTATION_TRANSPOSE -> {
            matrix.postRotate(90f)
            matrix.preScale(-1f, 1f)
        }
        ExifInterface.ORIENTATION_TRANSVERSE -> {
            matrix.postRotate(270f)
            matrix.preScale(-1f, 1f)
        }
        else -> return bitmap
    }
    val rotated = Bitmap.createBitmap(bitmap, 0, 0, bitmap.width, bitmap.height, matrix, true)
    if (rotated !== bitmap) bitmap.recycle()
    return rotated
}

private fun scaleToMaxDimension(bitmap: Bitmap, maxDimension: Int): Bitmap {
    val longEdge = maxOf(bitmap.width, bitmap.height)
    if (longEdge <= maxDimension) return bitmap
    val ratio = maxDimension.toFloat() / longEdge
    val width = (bitmap.width * ratio).toInt().coerceAtLeast(1)
    val height = (bitmap.height * ratio).toInt().coerceAtLeast(1)
    return Bitmap.createScaledBitmap(bitmap, width, height, true)
}

private fun encodeJpegToTarget(bitmap: Bitmap, targetBytes: Long): ByteArray {
    var quality = StartQuality
    var output = ByteArrayOutputStream()
    bitmap.compress(Bitmap.CompressFormat.JPEG, quality, output)
    while (output.size() > targetBytes && quality - QualityStep >= MinQuality) {
        quality -= QualityStep
        output = ByteArrayOutputStream()
        bitmap.compress(Bitmap.CompressFormat.JPEG, quality, output)
    }
    return output.toByteArray()
}
