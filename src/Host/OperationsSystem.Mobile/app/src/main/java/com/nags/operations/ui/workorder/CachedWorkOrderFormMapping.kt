package com.nags.operations.ui.workorder

import com.nags.operations.data.ResourceCalculationType
import com.nags.operations.data.WorkOrderDetailWireDto
import com.nags.operations.data.WorkOrderServiceLineWireDto
import com.nags.operations.data.WorkOrderTaskWireDto
import com.nags.operations.ui.util.parseOffsetDateTime

/**
 * Hydrates the create/update form from the cached embedded work order. Service and task rows keep
 * their stable server ids so an update reconciles them server-side (preserving their uploaded
 * attachments); attachment bytes are not cached, so the form surfaces existing names read-only
 * while newly captured attachments ride alongside.
 */
internal fun WorkOrderDetailWireDto.toPrefilledCreateFormState(nextKey: () -> Long): CreateWorkOrderFormState {
    // Current APIs return grouped occurrences and may also expose compatibility-flattened rows.
    // Never hydrate those aliases into the normal editors, otherwise an update duplicates them.
    val lines = serviceLines.filterNot { it.isReturnToRamp }.map { it.toFormRow(nextKey) }
    val taskRows = tasks.filterNot { it.isReturnToRamp }.map { it.toFormRow(nextKey) }
    val grouped = returnToRamps.map { occurrence ->
        ReturnToRampFormRow(
            localKey = nextKey(),
            serverId = occurrence.id,
            fromIso = occurrence.fromUtc,
            toIso = occurrence.toUtc,
            description = occurrence.description.orEmpty(),
            serviceLines = occurrence.serviceLines.map { it.toFormRow(nextKey) },
            tasks = occurrence.tasks.map { it.toFormRow(nextKey) },
        )
    }
    val legacyGrouped = if (grouped.isEmpty()) {
        legacyFlattenedReturnToRamp(nextKey)
    } else {
        emptyList()
    }
    return CreateWorkOrderFormState(
        flightNumber = actualFlightNumber,
        aircraftTypeId = aircraftTypeId,
        aircraftTailNumber = aircraftTailNumber.orEmpty(),
        scheduledArrivalIso = scheduledArrivalUtc,
        scheduledDepartureIso = scheduledDepartureUtc,
        ataIso = actualArrivalUtc.orEmpty(),
        atdIso = actualDepartureUtc.orEmpty(),
        remarks = remarks.orEmpty(),
        serviceLines = lines,
        tasks = taskRows,
        returnToRamps = grouped + legacyGrouped,
        customerSignaturePng = null,
        existingCustomerSignatureName = customerSignature?.fileName,
        serviceLineIdentityVersion = 1,
    )
}

private fun WorkOrderServiceLineWireDto.toFormRow(nextKey: () -> Long) = ServiceLineFormRow(
    localKey = nextKey(),
    serverId = id,
    serviceId = serviceId,
    serviceName = serviceName,
    employeeIds = effectivePerformedBy.map { it.staffMemberId },
    fromIso = fromUtc,
    toIso = toUtc,
    description = description.orEmpty(),
    existingAttachmentNames = attachments.map { it.originalFileName },
    // Group membership, not this retired compatibility flag, carries RTR semantics in the form.
    returnToRamp = false,
)

private fun WorkOrderTaskWireDto.toFormRow(nextKey: () -> Long) = TaskFormRow(
    localKey = nextKey(),
    serverId = id,
    taskType = taskType,
    employeeIds = employees.map { it.staffMemberId },
    toolIds = tools.map { it.resourceId },
    toolQuantities = tools.mapNotNull { resource ->
        resource.quantity?.let { resource.resourceId to it }
    }.toMap(),
    toolUsages = tools.associate { resource ->
        val calculationType = resource.calculationType ?: ResourceCalculationType.Duration
        val legacyQuantityOnlyDuration = calculationType == ResourceCalculationType.Duration &&
            resource.fromUtc.isNullOrBlank() && resource.quantity != null
        resource.resourceId to ResourceUsageForm(
            calculationType = calculationType,
            quantity = if (calculationType == ResourceCalculationType.Quantity) resource.quantity else null,
            fromIso = if (legacyQuantityOnlyDuration) fromUtc else resource.fromUtc.orEmpty(),
            toIso = if (legacyQuantityOnlyDuration) toUtc else resource.toUtc,
        )
    },
    materialIds = materials.map { it.resourceId },
    materialQuantities = materials.mapNotNull { resource ->
        resource.quantity?.let { resource.resourceId to it }
    }.toMap(),
    materialUsages = materials.associate { resource ->
        resource.resourceId to ResourceUsageForm(
            calculationType = resource.calculationType ?: ResourceCalculationType.Quantity,
            quantity = resource.quantity,
            fromIso = resource.fromUtc.orEmpty(),
            toIso = resource.toUtc,
        )
    },
    generalSupportIds = generalSupports.map { it.resourceId },
    generalSupportQuantities = generalSupports.mapNotNull { resource ->
        resource.quantity?.let { resource.resourceId to it }
    }.toMap(),
    generalSupportUsages = generalSupports.associate { resource ->
        resource.resourceId to ResourceUsageForm(
            calculationType = resource.calculationType ?: ResourceCalculationType.Quantity,
            quantity = resource.quantity,
            fromIso = resource.fromUtc.orEmpty(),
            toIso = resource.toUtc,
        )
    },
    description = description.orEmpty(),
    fromIso = fromUtc,
    toIso = toUtc,
    existingAttachmentNames = attachments.map { it.originalFileName },
    returnToRamp = false,
)

/** Bridges old cached responses that exposed only flagged flat rows into one grouped occurrence. */
private fun WorkOrderDetailWireDto.legacyFlattenedReturnToRamp(
    nextKey: () -> Long,
): List<ReturnToRampFormRow> {
    val legacyLines = serviceLines.filter { it.isReturnToRamp }
    val legacyTasks = tasks.filter { it.isReturnToRamp }
    if (legacyLines.isEmpty() && legacyTasks.isEmpty()) return emptyList()
    val fromCandidates = legacyLines.map { it.fromUtc } + legacyTasks.map { it.fromUtc }
    val toCandidates = legacyLines.map { it.toUtc } + legacyTasks.map { it.toUtc }
    fun minIso(values: List<String>, fallback: String): String = values.minByOrNull { value ->
        runCatching { parseOffsetDateTime(value).toInstant() }.getOrNull()
            ?: java.time.Instant.MAX
    } ?: fallback
    fun maxIso(values: List<String>, fallback: String): String = values.maxByOrNull { value ->
        runCatching { parseOffsetDateTime(value).toInstant() }.getOrNull()
            ?: java.time.Instant.MIN
    } ?: fallback
    return listOf(
        ReturnToRampFormRow(
            localKey = nextKey(),
            fromIso = minIso(fromCandidates, actualArrivalUtc ?: scheduledArrivalUtc),
            toIso = maxIso(toCandidates, actualDepartureUtc ?: scheduledDepartureUtc),
            serviceLines = legacyLines.map { it.toFormRow(nextKey) },
            tasks = legacyTasks.map { it.toFormRow(nextKey) },
        ),
    )
}
