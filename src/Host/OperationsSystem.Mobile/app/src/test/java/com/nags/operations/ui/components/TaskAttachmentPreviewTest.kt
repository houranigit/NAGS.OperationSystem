package com.nags.operations.ui.components

import com.nags.operations.data.TaskAttachmentKindValue
import com.nags.operations.ui.workorder.TaskAttachmentDraft
import java.util.Base64
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class TaskAttachmentPreviewTest {
    @Test
    fun previewContent_decodesOnlyWhenDeclaredSizeMatches() {
        val bytes = "%PDF-preview".toByteArray()
        val attachment = draft(
            kind = TaskAttachmentKindValue.Document,
            contentType = "application/pdf",
            fileName = "inspection.pdf",
            bytes = bytes,
        )

        assertArrayEquals(bytes, attachment.decodePreviewContent())
        assertNull(attachment.copy(sizeBytes = bytes.size + 1L).decodePreviewContent())
    }

    @Test
    fun previewContent_rejectsInvalidBase64AndUnknownKinds() {
        assertNull(
            draft(
                kind = TaskAttachmentKindValue.Image,
                contentType = "image/jpeg",
                fileName = "photo.jpg",
                bytes = byteArrayOf(1),
            ).copy(base64 = "not base64").decodePreviewContent(),
        )
        assertNull(
            draft(
                kind = "Unknown",
                contentType = "application/octet-stream",
                fileName = "file.bin",
                bytes = byteArrayOf(1),
            ).decodePreviewContent(),
        )
    }

    @Test
    fun previewFileName_isRandomPathSafe_and_never_exposes_original_name_or_content_hash() {
        val attachment = draft(
            kind = TaskAttachmentKindValue.Document,
            contentType = "text/html",
            fileName = "../../ramp report.html",
            bytes = byteArrayOf(1, 2, 3),
        )

        assertEquals("application/pdf", attachment.previewContentType())
        val first = attachment.previewFileName("private-token-1")
        val second = attachment.previewFileName("private-token-2")
        assertEquals("preview-private-token-1.pdf", first)
        assertNotEquals(first, second)
        assertFalse(first.contains("ramp", ignoreCase = true))
        assertFalse(first.contains("report", ignoreCase = true))
        assertTrue("/" !in first)
    }

    @Test
    fun cleanup_removes_only_expired_preview_files() {
        val directory = java.nio.file.Files.createTempDirectory("attachment-preview-test").toFile()
        try {
            val stale = java.io.File(directory, "preview-stale.pdf").apply {
                writeBytes(byteArrayOf(1))
                setLastModified(100L)
            }
            val fresh = java.io.File(directory, "preview-fresh.pdf").apply {
                writeBytes(byteArrayOf(2))
                setLastModified(300L)
            }

            assertEquals(1, cleanupAttachmentPreviewDirectory(directory, 200L))
            assertFalse(stale.exists())
            assertTrue(fresh.exists())
        } finally {
            directory.deleteRecursively()
        }
    }

    private fun draft(
        kind: String,
        contentType: String,
        fileName: String,
        bytes: ByteArray,
    ) = TaskAttachmentDraft(
        kind = kind,
        contentType = contentType,
        fileName = fileName,
        base64 = Base64.getEncoder().encodeToString(bytes),
        capturedAtIso = "2026-08-08T12:00:00Z",
        sizeBytes = bytes.size.toLong(),
    )
}
