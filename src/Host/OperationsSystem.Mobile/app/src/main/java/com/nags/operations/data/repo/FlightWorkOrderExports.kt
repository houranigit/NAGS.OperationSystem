package com.nags.operations.data.repo

import android.content.ClipData
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.provider.DocumentsContract
import androidx.core.content.FileProvider
import com.nags.operations.data.api.OperationsApi
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import java.io.File
import java.io.Serializable
import java.util.UUID
import java.util.concurrent.TimeUnit

/** A private cache entry that may be restored while Android's save dialog is open. */
internal data class FlightWorkOrderPdf(
    internal val cacheKey: String,
    val fileName: String,
    internal val owner: String,
) : Serializable

internal class FlightWorkOrderExports(
    context: Context,
    private val api: OperationsApi,
    private val currentOwner: suspend () -> String?,
) {
    private val appContext = context.applicationContext
    private val directory = File(appContext.cacheDir, "flight-work-orders")
    private val lock = Mutex()

    suspend fun download(flightId: String, flightNumber: String): FlightWorkOrderPdf = withContext(Dispatchers.IO) {
        lock.withLock {
            val owner = checkNotNull(currentOwner()) { "Sign in to export a Work Order PDF." }
            check(directory.isDirectory || directory.mkdirs()) { "The Work Order PDF could not be prepared." }
            prune()
            val cacheKey = UUID.randomUUID().toString()
            val exportDirectory = File(directory, cacheKey)
            check(exportDirectory.mkdir()) { "The Work Order PDF could not be prepared." }
            try {
                val pending = File(exportDirectory, "download.part")
                val headerName = pending.outputStream().buffered().use { api.downloadWorkOrder(flightId, it) }
                check(currentOwner() == owner) { "Your session changed. Export the Work Order PDF again." }
                val fileName = safePdfFileName(headerName)
                    ?: safePdfFileName("work-order-$flightNumber.pdf")
                    ?: "work-order.pdf"
                check(pending.renameTo(File(exportDirectory, fileName))) { "The Work Order PDF could not be prepared." }
                FlightWorkOrderPdf(cacheKey, fileName, owner)
            } catch (error: Exception) {
                exportDirectory.deleteRecursively()
                throw error
            }
        }
    }

    suspend fun saveCopy(pdf: FlightWorkOrderPdf, destination: Uri) = withContext(Dispatchers.IO) {
        lock.withLock {
            check(destination.scheme == "content") { "The selected location could not be opened." }
            val resolver = appContext.contentResolver
            try {
                val source = cachedFile(pdf)
                val output = checkNotNull(resolver.openOutputStream(destination, "wt")) {
                    "The selected location could not be opened."
                }
                output.use { stream -> source.inputStream().use { it.copyTo(stream) } }
            } catch (error: Exception) {
                // The document picker has already created a new file at this URI.
                runCatching { DocumentsContract.deleteDocument(resolver, destination) }
                throw error
            }
            Unit
        }
    }

    suspend fun shareIntent(pdf: FlightWorkOrderPdf): Intent = withContext(Dispatchers.IO) {
        lock.withLock {
            val uri = FileProvider.getUriForFile(appContext, "${appContext.packageName}.flightmedia", cachedFile(pdf))
            val send = Intent(Intent.ACTION_SEND).apply {
                type = "application/pdf"
                putExtra(Intent.EXTRA_STREAM, uri)
                clipData = ClipData.newRawUri(pdf.fileName, uri)
                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            }
            Intent.createChooser(send, null)
        }
    }

    suspend fun <T> readPreview(pdf: FlightWorkOrderPdf, read: (File) -> T): T = withContext(Dispatchers.IO) {
        lock.withLock { read(cachedFile(pdf)) }
    }

    suspend fun clear(): Unit = withContext(Dispatchers.IO) {
        lock.withLock { directory.deleteRecursively() }
    }

    private suspend fun cachedFile(pdf: FlightWorkOrderPdf): File {
        check(currentOwner() == pdf.owner) { "Your session changed. Export the Work Order PDF again." }
        check(UUID.fromString(pdf.cacheKey).toString() == pdf.cacheKey && safePdfFileName(pdf.fileName) == pdf.fileName)
        return File(File(directory, pdf.cacheKey), pdf.fileName).also {
            check(it.isFile) { "The temporary Work Order PDF expired. Export it again." }
        }
    }

    private fun prune() {
        val cutoff = System.currentTimeMillis() - TimeUnit.HOURS.toMillis(24)
        directory.listFiles().orEmpty().sortedByDescending(File::lastModified).forEachIndexed { index, entry ->
            if (entry.lastModified() < cutoff || index >= MAX_REPORTS - 1) entry.deleteRecursively()
        }
    }

    private fun safePdfFileName(value: String?): String? {
        if (value == null || !value.endsWith(".pdf", ignoreCase = true)) return null
        val stem = value.dropLast(4).map { if (it.isLetterOrDigit() || it == '-' || it == '_') it else '-' }
            .joinToString("").take(80).trim('-')
        return stem.takeIf { it.isNotBlank() }?.plus(".pdf")
    }

    private companion object {
        const val MAX_REPORTS = 4
    }
}
