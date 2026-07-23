package com.nags.operations.ui.workorder

import com.nags.operations.data.outbox.EnqueueAttachment
import com.nags.operations.data.outbox.OutboxPayload
import kotlinx.serialization.Serializable

/**
 * Inline binary attachment on a task or service-line form row. Persisted to the outbox as a file;
 * the backend enforces max sizes per attachment kind.
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

/** Applies a potentially stale service-field edit without rolling back asynchronous attachments. */
internal fun ServiceLineFormRow.mergeNonAttachmentEdit(
    edited: ServiceLineFormRow,
): ServiceLineFormRow = edited.copy(
    attachments = attachments,
    existingAttachmentNames = existingAttachmentNames,
)

/** Applies a potentially stale UI field edit without rolling back asynchronous attachments. */
internal fun TaskFormRow.mergeNonAttachmentEdit(edited: TaskFormRow): TaskFormRow = edited.copy(
    attachments = attachments,
    existingAttachmentNames = existingAttachmentNames,
)

internal fun CreateWorkOrderFormState.withServiceLineAttachmentAdded(
    serviceLineLocalKey: Long,
    attachment: TaskAttachmentDraft,
): CreateWorkOrderFormState = copy(
    serviceLines = serviceLines.map { line ->
        if (
            line.localKey == serviceLineLocalKey &&
            line.existingAttachmentNames.size + line.attachments.size <
            WorkOrderFormLimits.ServiceAttachments
        ) {
            line.copy(attachments = line.attachments + attachment)
        } else {
            line
        }
    },
)

internal fun CreateWorkOrderFormState.withServiceLineAttachmentRemoved(
    serviceLineLocalKey: Long,
    attachment: TaskAttachmentDraft,
): CreateWorkOrderFormState = copy(
    serviceLines = serviceLines.map { line ->
        if (line.localKey != serviceLineLocalKey) return@map line
        val index = line.attachments.indexOf(attachment)
        if (index < 0) line else line.copy(
            attachments = line.attachments.toMutableList().apply { removeAt(index) },
        )
    },
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

internal fun TaskAttachmentDraft.toOutboxPlaceholder(): OutboxPayload.AttachmentInput =
    OutboxPayload.AttachmentInput(
        relativePath = "",
        kind = kind,
        contentType = contentType,
        fileName = fileName,
        capturedAtIso = capturedAtIso,
        sizeBytes = sizeBytes,
    )

internal fun TaskAttachmentDraft.toEnqueueAttachment(): EnqueueAttachment =
    EnqueueAttachment(
        base64 = base64,
        kind = kind,
        contentType = contentType,
        fileName = fileName,
        capturedAtIso = capturedAtIso,
        sizeBytes = sizeBytes,
    )

/** Canonical durable-file order: every service line first, followed by every task. */
internal fun collectAttachmentsForOutbox(
    form: CreateWorkOrderFormState,
): List<EnqueueAttachment> =
    form.serviceLines.flatMap { line -> line.attachments.map(TaskAttachmentDraft::toEnqueueAttachment) } +
        form.tasks.flatMap { task -> task.attachments.map(TaskAttachmentDraft::toEnqueueAttachment) }

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
