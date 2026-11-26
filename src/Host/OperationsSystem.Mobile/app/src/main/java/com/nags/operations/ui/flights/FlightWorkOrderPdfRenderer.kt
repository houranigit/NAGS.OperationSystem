package com.nags.operations.ui.flights

import android.graphics.Bitmap
import android.graphics.Color
import android.graphics.pdf.PdfRenderer
import android.os.ParcelFileDescriptor
import com.nags.operations.data.repo.FlightWorkOrderExports
import com.nags.operations.data.repo.FlightWorkOrderPdf
import java.io.File
import kotlin.math.sqrt
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive

/** Renders individual pages from the private Work Order PDF without keeping native handles open. */
internal class FlightWorkOrderPdfRenderer(
    private val exports: FlightWorkOrderExports,
    private val pdf: FlightWorkOrderPdf,
) {
    suspend fun pageCount(): Int = exports.readPreview(pdf) { file ->
        openRenderer(file).use { renderer ->
            renderer.pageCount.also { check(it > 0) { "The Work Order PDF has no pages." } }
        }
    }

    suspend fun renderPage(index: Int, widthPx: Int): Bitmap {
        val context = currentCoroutineContext()
        return exports.readPreview(pdf) { file ->
            context.ensureActive()
            openRenderer(file).use { renderer ->
                renderer.openPage(index).use { page ->
                    val width = page.width.toDouble()
                    val height = page.height.toDouble()
                    val scale = minOf(
                        widthPx.coerceIn(1, MAX_PAGE_SIDE) / width,
                        MAX_PAGE_SIDE / height,
                        sqrt(MAX_PAGE_PIXELS / (width * height)),
                    )
                    val bitmap = Bitmap.createBitmap(
                        (width * scale).toInt().coerceAtLeast(1),
                        (height * scale).toInt().coerceAtLeast(1),
                        Bitmap.Config.ARGB_8888,
                    )
                    try {
                        bitmap.eraseColor(Color.WHITE)
                        page.render(bitmap, null, null, PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY)
                        context.ensureActive()
                        bitmap
                    } catch (error: Throwable) {
                        bitmap.recycle()
                        throw error
                    }
                }
            }
        }
    }

    private fun openRenderer(file: File): PdfRenderer {
        val descriptor = ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_ONLY)
        return try {
            // PdfRenderer owns the descriptor once construction succeeds.
            PdfRenderer(descriptor)
        } catch (error: Throwable) {
            runCatching { descriptor.close() }
            throw error
        }
    }

    private companion object {
        const val MAX_PAGE_SIDE = 2_048
        const val MAX_PAGE_PIXELS = 2_000_000
    }
}
