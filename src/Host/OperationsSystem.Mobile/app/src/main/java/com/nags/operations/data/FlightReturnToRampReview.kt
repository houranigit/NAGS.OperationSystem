package com.nags.operations.data

/** Apply only local edits to the newly recorded occurrence, including deliberate clears and removals. */
fun mergeReturnToRampReview(online: FlightReturnToRampRecord?, local: SaveFlightReturnToRampRequest,
    baseline: FlightReturnToRampRecord?): SaveFlightReturnToRampRequest {
    val desired = local.normalized()
    if (online == null) return desired
    val saved = online.input().normalized()
    val before = baseline?.input()?.normalized() ?: SaveFlightReturnToRampRequest(FlightReturnToRampArrivalInput(""))
    return saved.copy(arrival = saved.arrival.copy(
        arrivedAtUtc = reviewValue(saved.arrival.arrivedAtUtc, desired.arrival.arrivedAtUtc, before.arrival.arrivedAtUtc),
        remarks = reviewValue(saved.arrival.remarks, desired.arrival.remarks, before.arrival.remarks)),
        departure = mergeRtrDeparture(saved.departure, desired.departure, before.departure),
        services = reviewRows(saved.services, desired.services, before.services, { it.recordId }) { recorded, edited, original ->
            recorded.copy(input = mergeRtrService(recorded.input, edited.input, original.input))
        },
        tasks = reviewRows(saved.tasks, desired.tasks, before.tasks, { it.recordId }) { recorded, edited, original ->
            recorded.copy(input = mergeRtrTask(recorded.input, edited.input, original.input))
        }).normalized()
}

private fun <T> reviewValue(online: T, local: T, baseline: T): T = if (local == baseline) online else local

private fun mergeRtrDeparture(online: FlightReturnToRampDepartureInput?, local: FlightReturnToRampDepartureInput?,
    baseline: FlightReturnToRampDepartureInput?): FlightReturnToRampDepartureInput? {
    if (local == null) return if (baseline == null) online else null
    if (online == null) return if (local == baseline) null else local
    val replaceSignature = local.signatureContent != null || local.removeSignature
    return online.copy(
        departedAtUtc = reviewValue(online.departedAtUtc, local.departedAtUtc, baseline?.departedAtUtc.orEmpty()),
        remarks = reviewValue(online.remarks, local.remarks, baseline?.remarks),
        signatureContent = if (replaceSignature) local.signatureContent else null,
        signatureFileName = if (replaceSignature) local.signatureFileName else null,
        signatureContentType = if (replaceSignature) local.signatureContentType else null,
        signatureSignedAtUtc = if (replaceSignature) local.signatureSignedAtUtc else null,
        removeSignature = local.removeSignature,
    )
}

/** IDs identify occurrences: two periods for the same staff member or resource remain distinct. */
private fun <T> reviewRows(online: List<T>, local: List<T>, baseline: List<T>, id: (T) -> String,
    merge: (T, T, T) -> T): List<T> {
    val original = baseline.associateBy { id(it).lowercase() }
    val edited = local.associateBy { id(it).lowercase() }
    val result = online.filterNot { id(it).lowercase() in original && id(it).lowercase() !in edited }.toMutableList()
    for (value in local) {
        val key = id(value).lowercase()
        val before = original[key]
        val index = result.indexOfFirst { id(it).equals(key, true) }
        if (index >= 0) result[index] = if (before == null) value else merge(result[index], value, before)
        else if (before == null || value != before) result += value
    }
    return result
}

private fun reviewAttachments(online: List<FlightServiceAttachmentInput>?, local: List<FlightServiceAttachmentInput>?,
    baseline: List<FlightServiceAttachmentInput>?): List<FlightServiceAttachmentInput>? {
    if (local == null) return online
    val recorded = online.orEmpty().associateBy { it.id.lowercase() }
    return reviewRows(online.orEmpty(), local, baseline.orEmpty(), { it.id }) { saved, _, _ -> saved }
        .map { recorded[it.id.lowercase()] ?: it }
}

private fun mergeRtrService(online: SaveFlightServiceRequest, local: SaveFlightServiceRequest,
    baseline: SaveFlightServiceRequest): SaveFlightServiceRequest = online.copy(
    serviceId = reviewValue(online.serviceId, local.serviceId, baseline.serviceId),
    startUtc = reviewValue(online.startUtc, local.startUtc, baseline.startUtc),
    endUtc = reviewValue(online.endUtc, local.endUtc, baseline.endUtc),
    customerReferenceNumber = reviewValue(online.customerReferenceNumber, local.customerReferenceNumber, baseline.customerReferenceNumber),
    notes = reviewValue(online.notes, local.notes, baseline.notes),
    performers = reviewRows(online.performers, local.performers, baseline.performers, { it.id }) { recorded, edited, original ->
        recorded.copy(staffMemberId = reviewValue(recorded.staffMemberId, edited.staffMemberId, original.staffMemberId),
            startUtc = reviewValue(recorded.startUtc, edited.startUtc, original.startUtc),
            endUtc = reviewValue(recorded.endUtc, edited.endUtc, original.endUtc))
    },
    attachments = reviewAttachments(online.attachments, local.attachments, baseline.attachments),
)

private fun mergeRtrTask(online: SaveFlightTaskRequest, local: SaveFlightTaskRequest,
    baseline: SaveFlightTaskRequest): SaveFlightTaskRequest {
    fun resources(recorded: List<SaveFlightTaskResourceRequest>, edited: List<SaveFlightTaskResourceRequest>,
        original: List<SaveFlightTaskResourceRequest>) = reviewRows(recorded, edited, original, { it.id }) { current, desired, before ->
        val calculationEdited = !desired.resourceId.equals(before.resourceId, true) || desired.quantity != before.quantity ||
            desired.startUtc != before.startUtc || desired.endUtc != before.endUtc
        val differentMode = (current.quantity != null) != (desired.quantity != null) ||
            (current.startUtc != null || current.endUtc != null) != (desired.startUtc != null || desired.endUtc != null)
        // Quantity and duration are alternatives belonging to the selected resource. Do not combine two modes.
        val localCalculation = calculationEdited && (differentMode || !desired.resourceId.equals(current.resourceId, true))
        current.copy(resourceId = if (localCalculation) desired.resourceId else reviewValue(current.resourceId, desired.resourceId, before.resourceId),
            quantity = if (localCalculation) desired.quantity else reviewValue(current.quantity, desired.quantity, before.quantity),
            startUtc = if (localCalculation) desired.startUtc else reviewValue(current.startUtc, desired.startUtc, before.startUtc),
            endUtc = if (localCalculation) desired.endUtc else reviewValue(current.endUtc, desired.endUtc, before.endUtc),
            notes = reviewValue(current.notes, desired.notes, before.notes))
    }
    return online.copy(taskType = reviewValue(online.taskType, local.taskType, baseline.taskType),
        startUtc = reviewValue(online.startUtc, local.startUtc, baseline.startUtc),
        endUtc = reviewValue(online.endUtc, local.endUtc, baseline.endUtc),
        ataChapterId = reviewValue(online.ataChapterId, local.ataChapterId, baseline.ataChapterId),
        customerReferenceNumber = reviewValue(online.customerReferenceNumber, local.customerReferenceNumber, baseline.customerReferenceNumber),
        notes = reviewValue(online.notes, local.notes, baseline.notes),
        performers = reviewRows(online.performers, local.performers, baseline.performers, { it.id }) { recorded, edited, original ->
            recorded.copy(staffMemberId = reviewValue(recorded.staffMemberId, edited.staffMemberId, original.staffMemberId),
                startUtc = reviewValue(recorded.startUtc, edited.startUtc, original.startUtc),
                endUtc = reviewValue(recorded.endUtc, edited.endUtc, original.endUtc),
                notes = reviewValue(recorded.notes, edited.notes, original.notes))
        }, tools = resources(online.tools, local.tools, baseline.tools),
        materials = resources(online.materials, local.materials, baseline.materials),
        generalSupports = resources(online.generalSupports, local.generalSupports, baseline.generalSupports),
        attachments = reviewAttachments(online.attachments, local.attachments, baseline.attachments))
}
