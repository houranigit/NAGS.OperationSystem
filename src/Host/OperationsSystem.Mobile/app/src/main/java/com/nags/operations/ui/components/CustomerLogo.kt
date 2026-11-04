package com.nags.operations.ui.components

import android.graphics.BitmapFactory
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.FlightTakeoff
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

@Composable
internal fun CustomerLogo(
    customerId: String?,
    revision: Long,
    load: suspend (String) -> ByteArray?,
    modifier: Modifier = Modifier,
) {
    val targetPixels = with(LocalDensity.current) { 36.dp.roundToPx() }.coerceAtLeast(1)
    var bitmap by remember(customerId, revision, load, targetPixels) { mutableStateOf<ImageBitmap?>(null) }
    LaunchedEffect(customerId, revision, load, targetPixels) {
        if (customerId.isNullOrBlank()) return@LaunchedEffect
        try {
            val bytes = load(customerId) ?: return@LaunchedEffect
            bitmap = withContext(Dispatchers.Default) { decodeLogo(bytes, targetPixels) }
        } catch (cancelled: CancellationException) {
            throw cancelled
        } catch (_: Exception) {
            bitmap = null
        }
    }
    val image = bitmap
    Box(
        modifier = modifier.clip(RoundedCornerShape(10.dp))
            .background(if (image == null) MaterialTheme.colorScheme.primaryContainer.copy(alpha = 0.5f)
                else MaterialTheme.colorScheme.surface),
        contentAlignment = Alignment.Center,
    ) {
        if (image != null) {
            Image(bitmap = image, contentDescription = null, contentScale = ContentScale.Fit,
                modifier = Modifier.fillMaxSize())
        } else {
            Icon(Icons.Default.FlightTakeoff, null, tint = MaterialTheme.colorScheme.primary,
                modifier = Modifier.size(23.dp))
        }
    }
}

private fun decodeLogo(bytes: ByteArray, targetPixels: Int): ImageBitmap? {
    val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
    BitmapFactory.decodeByteArray(bytes, 0, bytes.size, bounds)
    val longestSide = maxOf(bounds.outWidth, bounds.outHeight)
    if (bounds.outWidth <= 0 || bounds.outHeight <= 0) return null
    var sampleSize = 1
    while (longestSide.toLong() / sampleSize > targetPixels * 2L) sampleSize *= 2
    return BitmapFactory.decodeByteArray(bytes, 0, bytes.size, BitmapFactory.Options().apply {
        inSampleSize = sampleSize
    })?.asImageBitmap()
}
