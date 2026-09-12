package com.nags.operations.ui.workorder

import android.content.Context
import android.graphics.pdf.PdfRenderer
import android.os.Bundle
import android.os.CancellationSignal
import android.os.ParcelFileDescriptor
import android.print.PageRange
import android.print.PrintAttributes
import android.print.PrintDocumentAdapter
import android.print.PrintDocumentInfo
import android.print.PrintManager
import java.io.File
import java.io.FileOutputStream
import java.util.concurrent.Executors
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/** Sends the portal-generated PDF bytes directly to Android's print / Save as PDF dialog. */
internal suspend fun printWorkOrderPdf(context: Context, flightId: String, bytes: ByteArray) {
    val file = withContext(Dispatchers.IO) {
        File.createTempFile("work-order-", ".pdf", context.cacheDir).also { it.writeBytes(bytes) }
    }
    try {
        val pageCount = withContext(Dispatchers.IO) {
            ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_ONLY).use { descriptor ->
                PdfRenderer(descriptor).use { it.pageCount }
            }
        }
        val manager = context.getSystemService(Context.PRINT_SERVICE) as PrintManager
        manager.print("Work order $flightId", WorkOrderPdfPrintAdapter(file, pageCount), null)
    } catch (exception: Exception) {
        file.delete()
        throw exception
    }
}

private class WorkOrderPdfPrintAdapter(private val file: File, private val pageCount: Int) : PrintDocumentAdapter() {
    private val executor = Executors.newSingleThreadExecutor()

    override fun onLayout(
        oldAttributes: PrintAttributes?,
        newAttributes: PrintAttributes?,
        cancellationSignal: CancellationSignal,
        callback: LayoutResultCallback,
        extras: Bundle?,
    ) {
        if (cancellationSignal.isCanceled) callback.onLayoutCancelled() else {
            callback.onLayoutFinished(
                PrintDocumentInfo.Builder("WorkOrder.pdf")
                    .setContentType(PrintDocumentInfo.CONTENT_TYPE_DOCUMENT)
                    .setPageCount(pageCount)
                    .build(),
                oldAttributes != newAttributes,
            )
        }
    }

    override fun onWrite(
        pages: Array<out PageRange>,
        destination: ParcelFileDescriptor,
        cancellationSignal: CancellationSignal,
        callback: WriteResultCallback,
    ) {
        executor.execute {
            try {
                file.inputStream().use { input ->
                    FileOutputStream(destination.fileDescriptor).use { output ->
                        val buffer = ByteArray(8192)
                        while (!cancellationSignal.isCanceled) {
                            val count = input.read(buffer)
                            if (count == -1) break
                            output.write(buffer, 0, count)
                        }
                    }
                }
                if (cancellationSignal.isCanceled) callback.onWriteCancelled()
                else callback.onWriteFinished(arrayOf(PageRange.ALL_PAGES))
            } catch (_: Exception) {
                callback.onWriteFailed("Unable to prepare the work order PDF.")
            }
        }
    }

    override fun onFinish() {
        executor.execute { file.delete() }
        executor.shutdown()
    }
}
