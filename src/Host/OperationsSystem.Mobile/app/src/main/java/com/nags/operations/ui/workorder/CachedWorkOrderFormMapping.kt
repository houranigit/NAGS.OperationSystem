package com.nags.operations.ui.workorder

import com.nags.operations.data.WorkOrderDetailWireDto

/**
 * Hydrates the create/update form from the cached embedded work order. Service and task rows keep
 * their stable server ids so an update reconciles them server-side (preserving their uploaded
 * attachments); attachment bytes are not cached, so the form surfaces existing names read-only
 * while newly captured attachments ride alongside.
 */
internal fun WorkOrderDetailWireDto.toPrefilledCreateFormState(nextKey: () -> Long): CreateWorkOrderFormState {
    val lines = serviceLines.map { line ->
        ServiceLineFormRow(
            localKey = nextKey(),
            serverId = line.id,
            serviceId = line.serviceId,
            serviceName = line.serviceName,
            employeeIds = line.effectivePerformedBy.map { it.staffMemberId },
            fromIso = line.fromUtc,
            toIso = line.toUtc,
            description = line.description.orEmpty(),
            existingAttachmentNames = line.attachments.map { it.originalFileName },
            returnToRamp = line.isReturnToRamp,
        )
    }
    val taskRows = tasks.map { t ->
        TaskFormRow(
            localKey = nextKey(),
            serverId = t.id,
            taskType = t.taskType,
            employeeIds = t.employees.map { it.staffMemberId },
            toolIds = t.tools.map { it.resourceId },
            toolQuantities = t.tools.associate { it.resourceId to it.quantity },
            materialIds = t.materials.map { it.resourceId },
            materialQuantities = t.materials.associate { it.resourceId to it.quantity },
            generalSupportIds = t.generalSupports.map { it.resourceId },
            generalSupportQuantities = t.generalSupports.associate { it.resourceId to it.quantity },
            description = t.description.orEmpty(),
            fromIso = t.fromUtc,
            toIso = t.toUtc,
            existingAttachmentNames = t.attachments.map { it.originalFileName },
            returnToRamp = t.isReturnToRamp,
        )
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
        customerSignaturePng = null,
        existingCustomerSignatureName = customerSignature?.fileName,
        serviceLineIdentityVersion = 1,
    )
}
