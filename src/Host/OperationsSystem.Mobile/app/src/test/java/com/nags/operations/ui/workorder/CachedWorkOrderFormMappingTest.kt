package com.nags.operations.ui.workorder

import com.nags.operations.data.WorkOrderDetailWireDto
import com.nags.operations.data.WorkOrderTaskResourceWireDto
import com.nags.operations.data.WorkOrderTaskWireDto
import com.nags.operations.data.WorkOrderSignatureWireDto
import com.nags.operations.data.WorkOrderTaskAttachmentWireDto
import com.nags.operations.data.WorkOrderServiceLineWireDto
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
                    performedByStaffMemberId = "staff-1",
                    performedByName = "Staff One",
                    fromUtc = "2026-07-11T10:10:00Z",
                    toUtc = "2026-07-11T11:00:00Z",
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
        assertEquals("Revoked service", form.serviceLines.single().serviceName)
        assertEquals("line-1", form.serviceLines.single().serverId)
        assertEquals(1, form.serviceLineIdentityVersion)
        assertTrue(form.serviceLines.single().returnToRamp)
        assertTrue(form.tasks.single().returnToRamp)
        assertEquals(2.5, form.tasks.single().toolQuantities.getValue("tool-1"), 0.0)
        assertEquals(3.0, form.tasks.single().materialQuantities.getValue("material-1"), 0.0)
        assertEquals(listOf("existing.pdf"), form.tasks.single().existingAttachmentNames)
        assertEquals("customer-signature.png", form.existingCustomerSignatureName)
    }
}
