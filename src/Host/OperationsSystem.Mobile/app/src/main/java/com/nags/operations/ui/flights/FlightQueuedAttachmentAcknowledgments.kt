package com.nags.operations.ui.flights

import com.nags.operations.data.FlightServiceAttachmentRecord

/** Promote only retained queued media; removed media and new editor captures remain independent. */
internal fun promoteAcknowledgedWorkAttachments(
    current: List<FlightAttachmentDraft>,
    acknowledged: List<FlightServiceAttachmentRecord>,
    queuedAttachmentIds: Set<String>,
): List<FlightAttachmentDraft> = current.map { attachment ->
    val uploaded = acknowledged.firstOrNull { it.id == attachment.id && it.uploadedAtUtc.isNotBlank() }
        ?.takeIf { attachment.id in queuedAttachmentIds && !attachment.ownedCapture }
    if (uploaded == null) attachment
    else if (attachment.uploadedAtUtc == uploaded.uploadedAtUtc && attachment.name == uploaded.fileName &&
        attachment.mimeType == uploaded.contentType) attachment
    else attachment.copy(name = uploaded.fileName, mimeType = uploaded.contentType,
        uploadedAtUtc = uploaded.uploadedAtUtc, uri = "", ownedCapture = false)
}
