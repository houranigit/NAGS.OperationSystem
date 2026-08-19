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

    @Test
    fun exactPreviewCleanup_removesOnlyTheOwnedPrivateFile() {
        val root = java.nio.file.Files.createTempDirectory("attachment-preview-owner-test").toFile()
        val directory = java.io.File(root, "attachment-previews").apply { mkdirs() }
        val owned = java.io.File(directory, "preview-owned.pdf").apply { writeBytes(byteArrayOf(1)) }
        val other = java.io.File(directory, "preview-other.pdf").apply { writeBytes(byteArrayOf(2)) }
        try {
            deleteMaterializedAttachmentPreview(owned)

            assertFalse(owned.exists())
            assertTrue(other.exists())
            assertTrue(directory.exists())

            deleteMaterializedAttachmentPreview(other)
            assertFalse(directory.exists())
        } finally {
            root.deleteRecursively()
        }
    }

    @Test
    fun previewKind_routesEverySupportedAttachment_toItsInAppViewer() {
        val bytes = byteArrayOf(1)

        assertEquals(
            AttachmentPreviewKind.Image,
            draft(TaskAttachmentKindValue.Image, "image/jpeg", "photo.jpg", bytes)
                .attachmentPreviewKind(),
        )
        assertEquals(
            AttachmentPreviewKind.Voice,
            draft(TaskAttachmentKindValue.Voice, "audio/mp4", "voice.m4a", bytes)
                .attachmentPreviewKind(),
        )
        assertEquals(
            AttachmentPreviewKind.Pdf,
            draft(TaskAttachmentKindValue.Document, "application/pdf", "report.pdf", bytes)
                .attachmentPreviewKind(),
        )
        assertEquals(
            AttachmentPreviewKind.Unsupported,
            draft("Unknown", "application/octet-stream", "file.bin", bytes)
                .attachmentPreviewKind(),
        )
    }

    @Test
    fun previewMetadata_formatting_isCompactAndStable() {
        assertEquals("0 B", formatAttachmentBytes(-1))
        assertEquals("900 B", formatAttachmentBytes(900))
        assertEquals("2 KB", formatAttachmentBytes(2_048))
        assertEquals("1.5 MB", formatAttachmentBytes(1_572_864))
        assertEquals("0:00", formatAttachmentDuration(-1))
        assertEquals("1:05", formatAttachmentDuration(65_999))
    }

    @Test
    fun pdfRenderSize_preservesAspectRatio_andCapsTallPages() {
        assertEquals(1_000 to 1_500, scaledPdfPreviewSize(2, 3, 1_000))
        assertEquals(800 to 3_200, scaledPdfPreviewSize(1, 4, 1_000))
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
