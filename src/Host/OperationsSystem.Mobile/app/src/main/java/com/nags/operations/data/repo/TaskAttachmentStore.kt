package com.nags.operations.data.repo

import java.io.File
import java.io.FileOutputStream
import java.nio.file.Files
import java.nio.file.StandardCopyOption
import java.util.UUID
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

interface TaskAttachmentStore {
    suspend fun save(content: ByteArray): String
    suspend fun import(uri: String): String
    suspend fun read(key: String): ByteArray
    suspend fun retainOnly(referenced: Set<String>)
    suspend fun previewFile(key: String): File? = null
}

/** The caller supplies a private durable directory and a bounded content-URI reader. */
class DiskTaskAttachmentStore(private val directory: File,
    private val readSource: suspend (String) -> ByteArray) : TaskAttachmentStore {
    private val fileName = Regex("[0-9a-f-]{36}\\.bin")
    override suspend fun import(uri: String): String = save(readSource(uri))
    override suspend fun save(content: ByteArray): String = withContext(Dispatchers.IO) {
        check(content.isNotEmpty() && content.size <= 25 * 1024 * 1024) { "Service attachments must be nonempty and at most 25 MB." }
        check(directory.isDirectory || directory.mkdirs()) { "Could not save task attachments on this device." }
        val key = "${UUID.randomUUID()}.bin"
        val temporary = File(directory, "$key.part")
        try {
            FileOutputStream(temporary).use { output -> output.write(content); output.fd.sync() }
            Files.move(temporary.toPath(), File(directory, key).toPath(), StandardCopyOption.ATOMIC_MOVE)
            key
        } finally { temporary.delete() }
    }
    override suspend fun read(key: String): ByteArray = withContext(Dispatchers.IO) {
        check(fileName.matches(key)) { "The saved task attachment is unavailable." }
        File(directory, key).readBytes()
    }
    override suspend fun previewFile(key: String): File? = withContext(Dispatchers.IO) {
        check(fileName.matches(key)) { "The saved task attachment is unavailable." }
        File(directory, key).takeIf { it.isFile }
    }
    override suspend fun retainOnly(referenced: Set<String>) = withContext(Dispatchers.IO) {
        directory.listFiles()?.forEach { file ->
            val owned = fileName.matches(file.name) || file.name.endsWith(".bin.part") && fileName.matches(file.name.removeSuffix(".part"))
            if (owned && file.name !in referenced) file.delete()
        }
        Unit
    }
}
