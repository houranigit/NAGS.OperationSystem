package com.nags.operations.ui.workorder

import com.nags.operations.data.MobileMyWorkOrderWireDto

internal fun MobileMyWorkOrderWireDto.toPrefilledCreateFormState(nextKey: () -> Long): CreateWorkOrderFormState {
    val sig = customerSignature
    val lines = serviceLines.map { line ->
        ServiceLineFormRow(
            localKey = nextKey(),
            serviceId = line.serviceSnapshot.serviceId,
            employeeId = line.employeeSnapshot.employeeId,
            fromIso = line.from,
            toIso = line.to,
            description = line.description.orEmpty(),
            returnToRamp = line.returnToRamp,
        )
    }
    val taskRows = tasks.map { t ->
        TaskFormRow(
            localKey = nextKey(),
            taskType = t.taskType,
            employeeIds = t.employees.map { it.employeeId },
            toolIds = t.tools.map { it.toolId },
            materialIds = t.materials.map { it.materialId },
            generalSupportIds = t.generalSupports.map { it.generalSupportId },
            description = t.description.orEmpty(),
            fromIso = t.from,
            toIso = t.to,
            returnToRamp = t.returnToRamp,
            attachments = t.attachments.map { a ->
                TaskAttachmentDraft(
                    kind = a.kind,
                    contentType = a.contentType,
                    fileName = a.fileName,
                    base64 = a.bytes,
                    capturedAtIso = a.capturedAt,
                    sizeBytes = a.sizeBytes.toLong(),
                )
            },
        )
    }
    return CreateWorkOrderFormState(
        flightNumber = "",
        aircraftTypeId = aircraftTypeId,
        aircraftTailNumber = aircraftTailNumber.orEmpty(),
        ataIso = ata.orEmpty(),
        remarks = remarks.orEmpty(),
        serviceLines = lines,
        tasks = taskRows,
        customerSignaturePng = sig,
    )
}
