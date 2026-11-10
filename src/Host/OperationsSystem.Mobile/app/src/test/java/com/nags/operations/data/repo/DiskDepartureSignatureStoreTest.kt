package com.nags.operations.data.repo

import java.io.File
import java.nio.file.Files
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test

class DiskDepartureSignatureStoreTest {
    @Test fun durableBytesSurviveStoreRecreationAndOnlyUnreferencedOwnedFilesAreReclaimed() = runBlocking {
        val directory = Files.createTempDirectory("departure-signature-test").toFile()
        try {
            val store = DiskDepartureSignatureStore(directory)
            val bytes = ByteArray(2_097_152) { (it % 251).toByte() }
            val desired = store.save(bytes)
            val attempt = store.save(byteArrayOf(1, 2, 3))
            val restarted = DiskDepartureSignatureStore(directory)
            assertArrayEquals(bytes, restarted.read(desired))
            assertArrayEquals(byteArrayOf(1, 2, 3), restarted.read(attempt))
            File(directory, "external-name.txt").writeText("not owned by this store")
            val interrupted = File(directory, "11111111-1111-1111-1111-111111111111.png.part").apply { writeBytes(byteArrayOf(7)) }
            restarted.retainOnly(setOf(attempt))
            assertFalse(File(directory, desired).exists())
            assertTrue(File(directory, attempt).exists())
            assertFalse(interrupted.exists())
            assertTrue(File(directory, "external-name.txt").exists())
            assertFalse(directory.listFiles()!!.any { it.name.endsWith(".part") })
        } finally { directory.deleteRecursively() }
    }

    @Test fun controlledKeysCannotReadOutsideThePrivateSignatureDirectory() = runBlocking {
        val directory = Files.createTempDirectory("departure-signature-test").toFile()
        try {
            val store = DiskDepartureSignatureStore(directory)
            for (key in listOf("../secret", "/tmp/secret", "nested/file.png")) {
                assertTrue(runCatching { store.read(key) }.isFailure)
            }
        } finally { directory.deleteRecursively() }
    }
}
