package com.nags.operations.data

import java.time.OffsetDateTime

/** Keep local providedness separate from the full request produced by an earlier Review. */
private fun sourceText(source: String?, saved: String?, staged: String?): String? =
    if (saved?.trim()?.takeIf { it.isNotEmpty() } == staged?.trim()?.takeIf { it.isNotEmpty() }) source else saved

private fun sameReviewTime(left: String?, right: String?): Boolean {
    if (left.isNullOrBlank() || right.isNullOrBlank()) return left.isNullOrBlank() && right.isNullOrBlank()
    val first = runCatching { OffsetDateTime.parse(left).toInstant() }.getOrNull()
    val second = runCatching { OffsetDateTime.parse(right).toInstant() }.getOrNull()
    return if (first != null && second != null) first == second else left == right
}

private fun sourceTime(source: String?, saved: String?, staged: String?) =
    if (sameReviewTime(saved, staged)) source else saved

fun arrivalReviewSourceAfterSave(online: SaveFlightArrivalRequest, source: SaveFlightArrivalRequest,
    saved: SaveFlightArrivalRequest, staged: SaveFlightArrivalRequest? = null): SaveFlightArrivalRequest {
    val baseline = (staged ?: mergeArrivalReview(online, source)).normalized()
    val final = saved.normalized()
    return source.copy(ata = sourceTime(source.ata, final.ata, baseline.ata).orEmpty(),
        aircraftTypeId = sourceText(source.aircraftTypeId, final.aircraftTypeId, baseline.aircraftTypeId).orEmpty(),
        aircraftTailNumber = sourceText(source.aircraftTailNumber, final.aircraftTailNumber, baseline.aircraftTailNumber).orEmpty(),
        customerWorkReferenceNumber = sourceText(source.customerWorkReferenceNumber, final.customerWorkReferenceNumber, baseline.customerWorkReferenceNumber),
        remarks = sourceText(source.remarks, final.remarks, baseline.remarks),
        acknowledgeConflictingSubmission = false, targetSubmissionId = online.targetSubmissionId).normalized()
}

fun departureReviewSourceAfterSave(online: SaveFlightDepartureRequest, source: SaveFlightDepartureRequest,
    saved: SaveFlightDepartureRequest, staged: SaveFlightDepartureRequest? = null): SaveFlightDepartureRequest {
    val baseline = (staged ?: mergeDepartureReview(online, source)).normalized()
    val final = saved.normalized()
    // Signing time is the private-file presence marker. Never persist signature bytes in provenance.
    return source.copy(atd = sourceTime(source.atd, final.atd, baseline.atd).orEmpty(),
        remarks = sourceText(source.remarks, final.remarks, baseline.remarks), signatureContent = null,
        signatureSignedAtUtc = sourceTime(source.signatureSignedAtUtc, final.signatureSignedAtUtc, baseline.signatureSignedAtUtc),
        acknowledgeConflictingSubmission = false, targetSubmissionId = online.targetSubmissionId).normalized()
}

private fun <T : Any> sourceChild(rows: List<T>, value: T, id: (T) -> String, identity: (T) -> String): T? =
    rows.firstOrNull { identity(value).isNotBlank() && identity(it).equals(identity(value), true) }
        ?: rows.firstOrNull { id(it).equals(id(value), true) }

/** An untouched inherited row stays absent; supplied rows retain sparse fields despite ID remapping. */
private fun <T : Any> sourceChildren(source: List<T>, saved: List<T>, staged: List<T>, id: (T) -> String,
    identity: (T) -> String, empty: (T) -> T, patch: (T, T, T) -> T): List<T> = saved.mapNotNull { final ->
    val original = sourceChild(source, final, id, identity)
    val baseline = sourceChild(staged, final, id, identity)
    if (baseline == null) final
    else {
        val absent = empty(final)
        val updated = patch(original ?: absent, final, baseline)
        updated.takeUnless { original == null && it == absent }
    }
}

private fun sourceAttachments(source: List<FlightServiceAttachmentInput>?, saved: List<FlightServiceAttachmentInput>?,
    staged: List<FlightServiceAttachmentInput>?): List<FlightServiceAttachmentInput>? = saved?.mapNotNull { final ->
    val original = source?.firstOrNull { it.id.equals(final.id, true) }
    (original ?: final.takeIf { staged.orEmpty().none { it.id.equals(final.id, true) } })
        ?.copy(content = null, contentUri = null)
}

fun serviceReviewSourceAfterSave(online: SaveFlightServiceRequest, source: SaveFlightServiceRequest,
    saved: SaveFlightServiceRequest, staged: SaveFlightServiceRequest? = null): SaveFlightServiceRequest {
    val baseline = (staged ?: mergeServiceReview(online, source)).normalized()
    val final = saved.normalized()
    val original = source.normalized()
    return original.copy(serviceId = sourceText(original.serviceId, final.serviceId, baseline.serviceId).orEmpty(),
        startUtc = sourceTime(original.startUtc, final.startUtc, baseline.startUtc).orEmpty(),
        endUtc = sourceTime(original.endUtc, final.endUtc, baseline.endUtc),
        customerReferenceNumber = sourceText(original.customerReferenceNumber, final.customerReferenceNumber, baseline.customerReferenceNumber),
        notes = sourceText(original.notes, final.notes, baseline.notes),
        performers = sourceChildren(original.performers, final.performers, baseline.performers, { it.id }, { it.staffMemberId },
            { SaveFlightServicePerformerRequest(it.id, "", "") }) { local, value, base -> local.copy(
                staffMemberId = sourceText(local.staffMemberId, value.staffMemberId, base.staffMemberId).orEmpty(),
                startUtc = sourceTime(local.startUtc, value.startUtc, base.startUtc).orEmpty(),
                endUtc = sourceTime(local.endUtc, value.endUtc, base.endUtc)) },
        attachments = sourceAttachments(original.attachments, final.attachments, baseline.attachments)).normalized()
}

fun taskReviewSourceAfterSave(online: SaveFlightTaskRequest, source: SaveFlightTaskRequest,
    saved: SaveFlightTaskRequest, staged: SaveFlightTaskRequest? = null): SaveFlightTaskRequest {
    val baseline = (staged ?: mergeTaskReview(online, source)).normalized()
    val final = saved.normalized()
    val original = source.normalized()
    fun resources(local: List<SaveFlightTaskResourceRequest>, value: List<SaveFlightTaskResourceRequest>,
        base: List<SaveFlightTaskResourceRequest>) = sourceChildren(local, value, base, { it.id }, { it.resourceId },
        { SaveFlightTaskResourceRequest(it.id, "") }) { prior, item, previous -> prior.copy(
            resourceId = sourceText(prior.resourceId, item.resourceId, previous.resourceId).orEmpty(),
            quantity = sourceText(prior.quantity, item.quantity, previous.quantity),
            startUtc = sourceTime(prior.startUtc, item.startUtc, previous.startUtc),
            endUtc = sourceTime(prior.endUtc, item.endUtc, previous.endUtc),
            notes = sourceText(prior.notes, item.notes, previous.notes)) }
    return original.copy(taskType = if (final.taskType == baseline.taskType) original.taskType else final.taskType,
        startUtc = sourceTime(original.startUtc, final.startUtc, baseline.startUtc).orEmpty(),
        endUtc = sourceTime(original.endUtc, final.endUtc, baseline.endUtc),
        ataChapterId = sourceText(original.ataChapterId, final.ataChapterId, baseline.ataChapterId),
        customerReferenceNumber = sourceText(original.customerReferenceNumber, final.customerReferenceNumber, baseline.customerReferenceNumber),
        notes = sourceText(original.notes, final.notes, baseline.notes),
        performers = sourceChildren(original.performers, final.performers, baseline.performers, { it.id }, { it.staffMemberId },
            { SaveFlightTaskPerformerRequest(it.id, "", "") }) { local, value, base -> local.copy(
                staffMemberId = sourceText(local.staffMemberId, value.staffMemberId, base.staffMemberId).orEmpty(),
                startUtc = sourceTime(local.startUtc, value.startUtc, base.startUtc).orEmpty(),
                endUtc = sourceTime(local.endUtc, value.endUtc, base.endUtc), notes = sourceText(local.notes, value.notes, base.notes)) },
        tools = resources(original.tools, final.tools, baseline.tools),
        materials = resources(original.materials, final.materials, baseline.materials),
        generalSupports = resources(original.generalSupports, final.generalSupports, baseline.generalSupports),
        attachments = sourceAttachments(original.attachments, final.attachments, baseline.attachments)).normalized()
}
