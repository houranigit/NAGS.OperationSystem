package com.nags.operations.ui.workorder

import kotlinx.serialization.Serializable

/**
 * Inline binary attachment on a task form row. Persisted to the outbox as a file; the backend
 * enforces max sizes per attachment kind.
 */
@Serializable
data class TaskAttachmentDraft(
    /** `Image` / `Voice` / `Document` — server `TaskAttachmentKind` enum names. */
    val kind: String,
    val contentType: String,
    val fileName: String,
    val base64: String,
    val capturedAtIso: String,
    val sizeBytes: Long,
)
