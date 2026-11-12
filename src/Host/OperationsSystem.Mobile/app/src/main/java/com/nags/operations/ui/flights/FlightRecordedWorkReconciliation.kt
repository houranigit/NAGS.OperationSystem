package com.nags.operations.ui.flights

/** Refresh recorded rows without claiming or replacing edits in unrelated local form drafts. */
internal fun reconcileRecordedServices(
    current: List<FlightServiceDraft>,
    projected: List<FlightServiceDraft>,
    previousKnown: Set<String>,
    nextKnown: Set<String>,
    protectedIds: Set<String> = emptySet(),
): List<FlightServiceDraft> = reconcileRecordedWork(current, projected, previousKnown, nextKnown, protectedIds,
    id = { it.id }, merge = { old, canonical -> canonical.copy(attachments = recordedAttachmentPreviews(old.attachments, canonical.attachments)) })

internal fun reconcileRecordedTasks(
    current: List<FlightTaskDraft>,
    projected: List<FlightTaskDraft>,
    previousKnown: Set<String>,
    nextKnown: Set<String>,
    protectedIds: Set<String> = emptySet(),
): List<FlightTaskDraft> = reconcileRecordedWork(current, projected, previousKnown, nextKnown, protectedIds,
    id = { it.id }, merge = { old, canonical -> canonical.copy(attachments = recordedAttachmentPreviews(old.attachments, canonical.attachments)) })

private fun <T> reconcileRecordedWork(
    current: List<T>,
    projected: List<T>,
    previousKnown: Set<String>,
    nextKnown: Set<String>,
    protectedIds: Set<String>,
    id: (T) -> String,
    merge: (T, T) -> T,
): List<T> {
    val recordedIds = previousKnown + nextKnown
    val canonical = projected.filter { id(it) in nextKnown }.associateBy(id)
    val result = mutableListOf<T>()
    val retainedIds = mutableSetOf<String>()
    current.forEach { old ->
        val recordId = id(old)
        if (recordId !in recordedIds || recordId in protectedIds) {
            result += old
            retainedIds += recordId
        } else canonical[recordId]?.let { replacement ->
            result += merge(old, replacement)
            retainedIds += recordId
        }
    }
    projected.forEach { row ->
        val recordId = id(row)
        if (recordId in nextKnown && retainedIds.add(recordId)) result += canonical.getValue(recordId)
    }
    return result
}

/** A private capture remains owned by its editor; canonical queued/server rows load their own bytes. */
private fun recordedAttachmentPreviews(
    current: List<FlightAttachmentDraft>,
    canonical: List<FlightAttachmentDraft>,
): List<FlightAttachmentDraft> = canonical.map { attachment ->
    val loaded = current.firstOrNull { it.id == attachment.id && it.uploadedAtUtc == attachment.uploadedAtUtc &&
        !it.ownedCapture && it.uri.isNotBlank() }
    if (loaded == null) attachment else attachment.copy(uri = loaded.uri, ownedCapture = loaded.ownedCapture)
}
