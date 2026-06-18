package com.nags.operations.data

import kotlinx.serialization.Serializable

@Serializable
data class WorkOrderSnapshotWireDto(
    val workOrderId: String,
    val workOrderNo: String? = null,
)

@Serializable
data class WorkOrderServiceLineWireDto(
    val id: String,
    val serviceSnapshot: ServiceSnapshotDto,
    val employeeSnapshot: EmployeeSnapshotDto,
    val workOrderSnapshot: WorkOrderSnapshotWireDto,
    val from: String,
    val to: String,
    val description: String? = null,
    val returnToRamp: Boolean = false,
)

@Serializable
data class WorkOrderTaskAttachmentWireDto(
    val id: String,
    val kind: Int,
    val contentType: String,
    val fileName: String,
    val sizeBytes: Int,
    val capturedAt: String,
    /** Base64-encoded bytes. The server serializes `byte[]` as a base64 string (System.Text.Json). */
    val bytes: String,
)

@Serializable
data class WorkOrderTaskWireDto(
    val id: String,
    val taskType: Int,
    val description: String? = null,
    val from: String,
    val to: String,
    val returnToRamp: Boolean = false,
    val employees: List<EmployeeSnapshotDto> = emptyList(),
    val tools: List<ToolSnapshotDto> = emptyList(),
    val materials: List<MaterialSnapshotDto> = emptyList(),
    val generalSupports: List<GeneralSupportSnapshotDto> = emptyList(),
    val attachments: List<WorkOrderTaskAttachmentWireDto> = emptyList(),
)

/**
 * Mirrors server `MobileMyWorkOrderDto` — embedded on flight summary rows so the
 * create/update screen can hydrate from Room alone.
 */
@Serializable
data class MobileMyWorkOrderWireDto(
    val id: String,
    val status: Int,
    val aircraftTypeId: String? = null,
    val aircraftTailNumber: String? = null,
    val ata: String? = null,
    val atd: String? = null,
    val isCanceled: Boolean = false,
    val canceledAt: String? = null,
    val remarks: String? = null,
    val serviceLines: List<WorkOrderServiceLineWireDto> = emptyList(),
    val tasks: List<WorkOrderTaskWireDto> = emptyList(),
    /** Base64-encoded PNG. The server serializes `byte[]?` as a base64 string (System.Text.Json). */
    val customerSignature: String? = null,
)
