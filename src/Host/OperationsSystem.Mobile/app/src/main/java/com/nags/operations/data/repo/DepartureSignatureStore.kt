package com.nags.operations.data.repo

import java.io.File
import java.io.FileOutputStream
import java.nio.file.Files
import java.nio.file.StandardCopyOption
import java.util.UUID
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

interface DepartureSignatureStore {
    suspend fun save(content: ByteArray): String
    suspend fun read(key: String): ByteArray
    suspend fun retainOnly(referenced: Set<String>)
}

/** Private durable files keep PNG bytes outside SQLite CursorWindow-sized rows. */
class DiskDepartureSignatureStore(private val directory: File) : DepartureSignatureStore {
    private val fileName = Regex("[0-9a-f-]{36}\\.png")

    override suspend fun save(content: ByteArray): String = withContext(Dispatchers.IO) {
        check(directory.isDirectory || directory.mkdirs()) { "Could not save the customer signature on this device." }
        val key = "${UUID.randomUUID()}.png"
        val temporary = File(directory, "$key.part")
        try {
            FileOutputStream(temporary).use { output -> output.write(content); output.fd.sync() }
            Files.move(temporary.toPath(), File(directory, key).toPath(), StandardCopyOption.ATOMIC_MOVE)
            key
        } finally { temporary.delete() }
    }

    override suspend fun read(key: String): ByteArray = withContext(Dispatchers.IO) {
        check(fileName.matches(key)) { "The saved customer signature is unavailable. Review departure details." }
        File(directory, key).readBytes()
    }

    override suspend fun retainOnly(referenced: Set<String>) = withContext(Dispatchers.IO) {
        directory.listFiles()?.forEach { file ->
            val owned = fileName.matches(file.name) || file.name.endsWith(".png.part") && fileName.matches(file.name.removeSuffix(".part"))
            if (owned && file.name !in referenced) file.delete()
        }
        Unit
    }
}
