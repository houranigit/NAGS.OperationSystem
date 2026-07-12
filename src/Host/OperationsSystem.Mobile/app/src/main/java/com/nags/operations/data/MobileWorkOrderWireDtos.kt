package com.nags.operations.data

import kotlinx.serialization.Serializable

/**
 * Mirrors server `WorkOrderDetailDto` — the caller's work order as embedded on flight rows
 * (`myWorkOrder`) and returned by `GET /api/v1/mobile/work-orders/{id}`. The flight context
 * fields (customer/station/schedule) are the immutable copy taken at submit time.
 */
@Serializable
data class WorkOrderDetailWireDto(
    val id: String,
    val flightId: String,
    val type: String,
    val status: String,
    val isMergeGenerated: Boolean = false,
    val ownerUserId: String,
    val ownerName: String? = null,
    val customerId: String,
    val customerIataCode: String? = null,
    val customerName: String,
    val stationId: String,
    val stationIata: String,
    val stationName: String,
    val operationTypeId: String,
    val operationTypeName: String,
    val plannedFlightNumber: String,
    val scheduledArrivalUtc: String,
    val scheduledDepartureUtc: String,
    val actualFlightNumber: String,
    val aircraftTypeId: String? = null,
    val aircraftTypeModel: String? = null,
    val aircraftTailNumber: String? = null,
    val actualArrivalUtc: String? = null,
    val actualDepartureUtc: String? = null,
    val canceledAtUtc: String? = null,
    val cancellationReason: String? = null,
    val remarks: String? = null,
    val customerSignature: WorkOrderSignatureWireDto? = null,
    val approvalSequence: Int? = null,
    val approvalNumber: String? = null,
    val serviceLines: List<WorkOrderServiceLineWireDto> = emptyList(),
    val tasks: List<WorkOrderTaskWireDto> = emptyList(),
    val createdAtUtc: String,
    val updatedAtUtc: String? = null,
    val rowVersion: String,
)

/** Mirrors server `WorkOrderServiceLineDto`. */
@Serializable
data class WorkOrderServiceLineWireDto(
    val id: String,
    val serviceId: String,
    val serviceName: String,
    val performedByStaffMemberId: String,
    val performedByName: String,
    val fromUtc: String,
    val toUtc: String,
    val description: String? = null,
)

/** Mirrors server `WorkOrderTaskDto`. Task ids are stable — resend them to keep attachments. */
@Serializable
data class WorkOrderTaskWireDto(
    val id: String,
    val taskType: String,
    val description: String? = null,
    val fromUtc: String,
    val toUtc: String,
    val employees: List<WorkOrderTaskEmployeeWireDto> = emptyList(),
    val tools: List<WorkOrderTaskResourceWireDto> = emptyList(),
    val materials: List<WorkOrderTaskResourceWireDto> = emptyList(),
    val generalSupports: List<WorkOrderTaskResourceWireDto> = emptyList(),
    val attachments: List<WorkOrderTaskAttachmentWireDto> = emptyList(),
)

@Serializable
data class WorkOrderTaskEmployeeWireDto(
    val staffMemberId: String,
    val fullName: String,
    val employeeId: String,
)

/**
 * One resource row on a task. The server DTOs use `toolId`/`materialId`/`generalSupportId` as
 * the id field name per kind, so each is optional here and [resourceId] resolves whichever is set.
 */
@Serializable
data class WorkOrderTaskResourceWireDto(
    val toolId: String? = null,
    val materialId: String? = null,
    val generalSupportId: String? = null,
    val name: String,
    val quantity: Double = 1.0,
) {
    val resourceId: String get() = toolId ?: materialId ?: generalSupportId ?: ""
}

/**
 * Mirrors server `WorkOrderTaskAttachmentDto` — metadata only. Attachment bytes live in object
 * storage and are fetched via the dedicated attachment content endpoint when needed.
 */
@Serializable
data class WorkOrderTaskAttachmentWireDto(
    val id: String,
    val kind: String,
    val originalFileName: String,
    val contentType: String,
    val size: Long = 0,
)

/** Mirrors server `WorkOrderSignatureDto` — metadata only (PNG stored server-side). */
@Serializable
data class WorkOrderSignatureWireDto(
    val fileName: String,
    val contentType: String,
    val size: Long = 0,
    val signedAtUtc: String,
)
