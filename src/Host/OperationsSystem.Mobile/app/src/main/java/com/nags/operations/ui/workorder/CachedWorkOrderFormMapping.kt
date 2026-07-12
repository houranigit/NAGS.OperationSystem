package com.nags.operations.ui.workorder

import com.nags.operations.data.WorkOrderDetailWireDto

/**
 * Hydrates the create/update form from the cached embedded work order. Task rows keep their
 * stable server ids so an update reconciles tasks server-side (preserving their uploaded
 * attachments); attachments themselves are metadata-only in the cache, so the form surfaces
 * their names read-only and new attachments ride alongside.
 */
internal fun WorkOrderDetailWireDto.toPrefilledCreateFormState(nextKey: () -> Long): CreateWorkOrderFormState {
    val lines = serviceLines.map { line ->
        ServiceLineFormRow(
            localKey = nextKey(),
            serviceId = line.serviceId,
            employeeId = line.performedByStaffMemberId,
            fromIso = line.fromUtc,
            toIso = line.toUtc,
            description = line.description.orEmpty(),
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
    )
}
