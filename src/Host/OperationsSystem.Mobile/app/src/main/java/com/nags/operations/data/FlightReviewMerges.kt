package com.nags.operations.data

import kotlinx.serialization.Serializable

/** Identifies the exact durable local card the employee explicitly reviewed. */
@Serializable
data class FlightOperationReviewTarget(val kind: String, val recordId: String? = null, val changeId: String,
    val useLocalSource: Boolean = false, val stagedInputJson: String? = null)

private fun provided(local: String?, online: String?): String? = local?.takeIf { it.isNotBlank() } ?: online

/** These helpers only stage values. They never save, discard, retarget attribution, or acknowledge warnings. */
fun mergeArrivalReview(online: SaveFlightArrivalRequest, local: SaveFlightArrivalRequest) = online.copy(
    ata = provided(local.ata, online.ata).orEmpty(),
    aircraftTypeId = provided(local.aircraftTypeId, online.aircraftTypeId).orEmpty(),
    aircraftTailNumber = provided(local.aircraftTailNumber, online.aircraftTailNumber).orEmpty(),
    customerWorkReferenceNumber = provided(local.customerWorkReferenceNumber, online.customerWorkReferenceNumber),
    remarks = provided(local.remarks, online.remarks),
).normalized()

fun mergeDepartureReview(online: SaveFlightDepartureRequest, local: SaveFlightDepartureRequest): SaveFlightDepartureRequest {
    val hasLocalSignature = !local.signatureContent.isNullOrBlank()
    return online.copy(atd = provided(local.atd, online.atd).orEmpty(), remarks = provided(local.remarks, online.remarks),
        signatureContent = if (hasLocalSignature) local.signatureContent else online.signatureContent,
        signatureSignedAtUtc = if (hasLocalSignature) local.signatureSignedAtUtc else online.signatureSignedAtUtc).normalized()
}

private fun <T> mergeChildren(online: List<T>, local: List<T>, id: (T) -> String, identity: (T) -> String,
    patch: (T, T) -> T): List<T> {
    if (local.isEmpty()) return online
    val result = online.toMutableList()
    for (value in local) {
        // The same performer/resource cannot appear twice under different row IDs.
        val semantic = identity(value).takeIf { it.isNotBlank() }
        val byIdentity = semantic?.let { key -> result.indexOfFirst { identity(it).equals(key, true) } } ?: -1
        val index = if (byIdentity >= 0) byIdentity else result.indexOfFirst { id(it).equals(id(value), true) }
        if (index < 0) result += value else result[index] = patch(result[index], value)
    }
    return result
}

private fun mergeAttachments(online: List<FlightServiceAttachmentInput>?, local: List<FlightServiceAttachmentInput>?): List<FlightServiceAttachmentInput>? {
    if (local.isNullOrEmpty()) return online
    val result = online.orEmpty().toMutableList()
    for (file in local) {
        val index = result.indexOfFirst { it.id.equals(file.id, true) }
        // A published attachment identity is immutable. Retain its bytes and metadata.
        if (index < 0) result += file
    }
    return result
}

fun mergeServiceReview(online: SaveFlightServiceRequest, local: SaveFlightServiceRequest) = online.copy(
    serviceId = provided(local.serviceId, online.serviceId).orEmpty(),
    startUtc = provided(local.startUtc, online.startUtc).orEmpty(), endUtc = provided(local.endUtc, online.endUtc),
    customerReferenceNumber = provided(local.customerReferenceNumber, online.customerReferenceNumber),
    notes = provided(local.notes, online.notes),
    performers = mergeChildren(online.performers, local.performers, { it.id }, { it.staffMemberId }) { saved, desired -> saved.copy(
        staffMemberId = provided(desired.staffMemberId, saved.staffMemberId).orEmpty(),
        startUtc = provided(desired.startUtc, saved.startUtc).orEmpty(), endUtc = provided(desired.endUtc, saved.endUtc)) },
    attachments = mergeAttachments(online.attachments, local.attachments),
).normalized()

fun mergeTaskReview(online: SaveFlightTaskRequest, local: SaveFlightTaskRequest): SaveFlightTaskRequest {
    fun resources(saved: List<SaveFlightTaskResourceRequest>, desired: List<SaveFlightTaskResourceRequest>) =
        mergeChildren(saved, desired, { it.id }, { it.resourceId }) { current, input ->
            val quantityProvided = !input.quantity.isNullOrBlank()
            val durationProvided = !input.startUtc.isNullOrBlank() || !input.endUtc.isNullOrBlank()
            current.copy(resourceId = provided(input.resourceId, current.resourceId).orEmpty(),
                quantity = if (quantityProvided) input.quantity else if (durationProvided) null else current.quantity,
                startUtc = if (quantityProvided) null else provided(input.startUtc, current.startUtc),
                endUtc = if (quantityProvided) null else provided(input.endUtc, current.endUtc),
                notes = provided(input.notes, current.notes))
        }
    // -1 is used only by sparse review provenance, never by a valid saved Task request.
    return online.copy(taskType = if (local.taskType == -1) online.taskType else local.taskType, startUtc = provided(local.startUtc, online.startUtc).orEmpty(),
        endUtc = provided(local.endUtc, online.endUtc), ataChapterId = provided(local.ataChapterId, online.ataChapterId),
        customerReferenceNumber = provided(local.customerReferenceNumber, online.customerReferenceNumber), notes = provided(local.notes, online.notes),
        performers = mergeChildren(online.performers, local.performers, { it.id }, { it.staffMemberId }) { saved, desired -> saved.copy(
            staffMemberId = provided(desired.staffMemberId, saved.staffMemberId).orEmpty(), startUtc = provided(desired.startUtc, saved.startUtc).orEmpty(),
            endUtc = provided(desired.endUtc, saved.endUtc), notes = provided(desired.notes, saved.notes)) },
        tools = resources(online.tools, local.tools), materials = resources(online.materials, local.materials),
        generalSupports = resources(online.generalSupports, local.generalSupports),
        attachments = mergeAttachments(online.attachments, local.attachments)).normalized()
}
