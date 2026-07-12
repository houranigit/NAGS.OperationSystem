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

/** Applies a potentially stale UI field edit without rolling back asynchronous attachments. */
internal fun TaskFormRow.mergeNonAttachmentEdit(edited: TaskFormRow): TaskFormRow = edited.copy(
    attachments = attachments,
    existingAttachmentNames = existingAttachmentNames,
)

internal fun CreateWorkOrderFormState.withTaskAttachmentAdded(
    taskLocalKey: Long,
    attachment: TaskAttachmentDraft,
): CreateWorkOrderFormState = copy(
    tasks = tasks.map { task ->
        if (
            task.localKey == taskLocalKey &&
            task.existingAttachmentNames.size + task.attachments.size < WorkOrderFormLimits.TaskAttachments
        ) {
            task.copy(attachments = task.attachments + attachment)
        } else {
            task
        }
    },
)

internal fun CreateWorkOrderFormState.withTaskAttachmentRemoved(
    taskLocalKey: Long,
    attachment: TaskAttachmentDraft,
): CreateWorkOrderFormState = copy(
    tasks = tasks.map { task ->
        if (task.localKey != taskLocalKey) return@map task
        val index = task.attachments.indexOf(attachment)
        if (index < 0) task else task.copy(
            attachments = task.attachments.toMutableList().apply { removeAt(index) },
        )
    },
)
