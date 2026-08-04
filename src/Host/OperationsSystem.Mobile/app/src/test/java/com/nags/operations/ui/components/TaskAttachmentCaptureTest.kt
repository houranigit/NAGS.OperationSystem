package com.nags.operations.ui.components

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class TaskAttachmentCaptureTest {
    @Test
    fun mp4Signature_acceptsFtypContainer() {
        val content = byteArrayOf(
            0x00, 0x00, 0x00, 0x18,
            0x66, 0x74, 0x79, 0x70,
            0x6D, 0x70, 0x34, 0x32,
        )

        assertTrue(content.hasMp4ContainerSignature())
    }

    @Test
    fun mp4Signature_rejectsIncompleteRecorderOutput() {
        val content = byteArrayOf(
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        )

        assertFalse(content.hasMp4ContainerSignature())
        assertFalse(byteArrayOf(0x00, 0x00, 0x00, 0x18).hasMp4ContainerSignature())
    }
}
