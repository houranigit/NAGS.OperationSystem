package com.nags.operations.ui.workorder

import com.nags.operations.data.WorkOrderDetailWireDto
import com.nags.operations.data.ResourceCalculationType
import com.nags.operations.data.WorkOrderTaskResourceWireDto
import com.nags.operations.data.WorkOrderTaskWireDto
import com.nags.operations.data.WorkOrderSignatureWireDto
import com.nags.operations.data.WorkOrderTaskAttachmentWireDto
import com.nags.operations.data.WorkOrderServiceLineWireDto
import com.nags.operations.data.WorkOrderServiceLinePerformerWireDto
import com.nags.operations.data.WorkOrderReturnToRampWireDto
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class CachedWorkOrderFormMappingTest {
    @Test
    fun mapping_preserves_atd_and_resource_quantities() {
        val workOrder = WorkOrderDetailWireDto(
            id = "work-order-1",
            flightId = "flight-1",
            type = "Completion",
            status = "Submitted",
            ownerUserId = "user-1",
            customerId = "customer-1",
            customerName = "Customer",
            stationId = "station-1",
            stationIata = "ORD",
            stationName = "Chicago",
            operationTypeId = "operation-1",
            operationTypeName = "Transit",
            plannedFlightNumber = "MOB100",
            scheduledArrivalUtc = "2026-07-11T10:00:00Z",
            scheduledDepartureUtc = "2026-07-11T12:00:00Z",
            actualFlightNumber = "MOB100",
            actualArrivalUtc = "2026-07-11T10:05:00Z",
            actualDepartureUtc = "2026-07-11T11:55:00Z",
            serviceLines = listOf(
                WorkOrderServiceLineWireDto(
                    id = "line-1",
                    serviceId = "service-1",
                    serviceName = "Revoked service",
                    performedBy = listOf(
                        WorkOrderServiceLinePerformerWireDto("staff-1", "Staff One", "E001"),
                        WorkOrderServiceLinePerformerWireDto("staff-2", "Staff Two", "E002"),
                    ),
                    fromUtc = "2026-07-11T10:10:00Z",
                    toUtc = "2026-07-11T11:00:00Z",
                    attachments = listOf(
                        WorkOrderTaskAttachmentWireDto(
                            id = "service-attachment-1",
                            kind = "Image",
                            originalFileName = "existing-service.jpg",
                            contentType = "image/jpeg",
                        ),
                    ),
                    isReturnToRamp = true,
                ),
            ),
            tasks = listOf(
                WorkOrderTaskWireDto(
                    id = "task-1",
                    taskType = "Major",
                    fromUtc = "2026-07-11T10:10:00Z",
                    toUtc = "2026-07-11T11:00:00Z",
                    isReturnToRamp = true,
                    tools = listOf(
                        WorkOrderTaskResourceWireDto(
                            toolId = "tool-1",
                            name = "Tow bar",
                            quantity = 2.5,
                        ),
                    ),
                    materials = listOf(
                        WorkOrderTaskResourceWireDto(
                            materialId = "material-1",
                            name = "Oil",
                            quantity = 3.0,
                        ),
                    ),
                    attachments = listOf(
                        WorkOrderTaskAttachmentWireDto(
                            id = "attachment-1",
                            kind = "Document",
                            originalFileName = "existing.pdf",
                            contentType = "application/pdf",
                        ),
                    ),
                ),
            ),
            customerSignature = WorkOrderSignatureWireDto(
                fileName = "customer-signature.png",
                contentType = "image/png",
                signedAtUtc = "2026-07-11T11:50:00Z",
            ),
            createdAtUtc = "2026-07-11T12:00:00Z",
            rowVersion = "AQID",
        )

        val form = workOrder.toPrefilledCreateFormState { 1L }

        assertEquals(workOrder.actualDepartureUtc, form.atdIso)
        assertEquals(workOrder.scheduledArrivalUtc, form.scheduledArrivalIso)
        assertTrue(form.serviceLines.isEmpty())
        assertTrue(form.tasks.isEmpty())
        val occurrence = form.returnToRamps.single()
        assertEquals("Revoked service", occurrence.serviceLines.single().serviceName)
        assertEquals("line-1", occurrence.serviceLines.single().serverId)
        assertEquals(listOf("staff-1", "staff-2"), occurrence.serviceLines.single().employeeIds)
        assertEquals(
            listOf("existing-service.jpg"),
            occurrence.serviceLines.single().existingAttachmentNames,
        )
        assertEquals(1, form.serviceLineIdentityVersion)
        assertEquals(false, occurrence.serviceLines.single().returnToRamp)
        assertEquals(false, occurrence.tasks.single().returnToRamp)
        assertEquals(2.5, occurrence.tasks.single().toolQuantities.getValue("tool-1"), 0.0)
        val bridgedTool = occurrence.tasks.single().toolUsages.getValue("tool-1")
        assertEquals(ResourceCalculationType.Duration, bridgedTool.calculationType)
        assertEquals(null, bridgedTool.quantity)
        assertEquals("2026-07-11T10:10:00Z", bridgedTool.fromIso)
        assertEquals("2026-07-11T11:00:00Z", bridgedTool.toIso)
        assertEquals(3.0, occurrence.tasks.single().materialQuantities.getValue("material-1"), 0.0)
        assertEquals(listOf("existing.pdf"), occurrence.tasks.single().existingAttachmentNames)
        assertEquals("customer-signature.png", form.existingCustomerSignatureName)
    }

    @Test
    fun canonical_occurrences_remain_separate_and_filter_flattened_aliases() {
        val alias = WorkOrderServiceLineWireDto(
            id = "alias",
            serviceId = "service-alias",
            serviceName = "Compatibility alias",
            fromUtc = "2026-08-08T10:00:00Z",
            toUtc = "2026-08-08T10:30:00Z",
            isReturnToRamp = true,
        )
        val workOrder = basicWorkOrder().copy(
            serviceLines = listOf(alias),
            returnToRamps = listOf(
                WorkOrderReturnToRampWireDto(
                    id = "rtr-1",
                    fromUtc = "2026-08-08T10:00:00Z",
                    toUtc = "2026-08-08T10:30:00Z",
                    description = "First",
                    serviceLines = listOf(alias.copy(id = "nested-1", isReturnToRamp = false)),
                ),
                WorkOrderReturnToRampWireDto(
                    id = "rtr-2",
                    fromUtc = "2026-08-08T11:00:00Z",
                    toUtc = "2026-08-08T11:45:00Z",
                    description = "Second",
                    tasks = listOf(
                        WorkOrderTaskWireDto(
                            id = "nested-task",
                            taskType = "Minor",
                            fromUtc = "2026-08-08T11:05:00Z",
                            toUtc = "2026-08-08T11:40:00Z",
                        ),
                    ),
                ),
            ),
        )

        var key = 0L
        val form = workOrder.toPrefilledCreateFormState { ++key }

        assertTrue(form.serviceLines.isEmpty())
        assertEquals(listOf("rtr-1", "rtr-2"), form.returnToRamps.map { it.serverId })
        assertEquals("nested-1", form.returnToRamps.first().serviceLines.single().serverId)
        assertEquals("nested-task", form.returnToRamps.last().tasks.single().serverId)
    }

    private fun basicWorkOrder() = WorkOrderDetailWireDto(
        id = "work-order",
        flightId = "flight",
        type = "Completion",
        status = "Submitted",
        ownerUserId = "user",
        customerId = "customer",
        customerName = "Customer",
        stationId = "station",
        stationIata = "RUH",
        stationName = "Riyadh",
        operationTypeId = "operation",
        operationTypeName = "Transit",
        plannedFlightNumber = "SV100",
        scheduledArrivalUtc = "2026-08-08T10:00:00Z",
        scheduledDepartureUtc = "2026-08-08T12:00:00Z",
        actualFlightNumber = "SV100",
        createdAtUtc = "2026-08-08T12:00:00Z",
        rowVersion = "AQID",
    )
}
