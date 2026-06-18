package com.nags.operations.ui.workorder

import kotlinx.serialization.Serializable

/**
 * Inline binary attachment on a task form row. Maps to wire `MobileTaskAttachmentInput`;
 * backend enforces max sizes per attachment kind.
 */
@Serializable
data class TaskAttachmentDraft(
    val kind: Int,
    val contentType: String,
    val fileName: String,
    val base64: String,
    val capturedAtIso: String,
    val sizeBytes: Long,
)
