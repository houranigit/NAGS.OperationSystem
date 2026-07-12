package com.nags.operations.ui.workorder

import com.nags.operations.data.TaskTypeKind
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Test

class WorkOrderFormValidationTest {
    @Test
    fun valid_portal_equivalent_form_passes() {
        val errors = computeCreateWorkOrderSubmitErrors(
            form = validForm(),
            dialogAtdIso = null,
            isAdHocScratch = true,
            selectedCustomerId = "customer-1",
        )

        assertNull(errors)
    }

    @Test
    fun required_lengths_task_type_and_quantities_are_rejected() {
        val form = validForm().copy(
            flightNumber = "",
            aircraftTailNumber = "X".repeat(WorkOrderFormLimits.AircraftTailNumber + 1),
            remarks = "R".repeat(WorkOrderFormLimits.Remarks + 1),
            tasks = listOf(
                validForm().tasks.single().copy(
                    taskType = "Unknown",
                    description = "D".repeat(WorkOrderFormLimits.LineDescription + 1),
                    toolQuantities = mapOf("tool-1" to 0.0),
                ),
            ),
        )

        val errors = computeCreateWorkOrderSubmitErrors(
            form = form,
            dialogAtdIso = null,
            isAdHocScratch = false,
            selectedCustomerId = null,
        )

        assertNotNull(errors)
        assertNotNull(errors!!.flightNumber)
        assertNotNull(errors.aircraftTailNumber)
        assertNotNull(errors.remarks)
        val taskErrors = errors.tasksByKey.getValue(2L)
        assertNotNull(taskErrors.taskType)
        assertNotNull(taskErrors.description)
        assertNotNull(taskErrors.tools)
    }

    @Test
    fun scratch_schedule_requires_departure_after_arrival() {
        val form = validForm().copy(scheduledDepartureIso = validForm().scheduledArrivalIso)

        val errors = computeCreateWorkOrderSubmitErrors(
            form = form,
            dialogAtdIso = null,
            isAdHocScratch = true,
            selectedCustomerId = "customer-1",
        )

        assertNotNull(errors?.scheduledDeparture)
    }

    @Test
    fun existing_and_new_attachments_share_the_server_limit() {
        val task = validForm().tasks.single().copy(
            existingAttachmentNames = List(WorkOrderFormLimits.TaskAttachments) { "existing-$it.pdf" },
            attachments = listOf(
                TaskAttachmentDraft(
                    kind = "Document",
                    contentType = "application/pdf",
                    fileName = "new.pdf",
                    base64 = "JVBERi0=",
                    capturedAtIso = "2026-07-11T10:00:00Z",
                    sizeBytes = 5,
                ),
            ),
        )

        val errors = computeCreateWorkOrderSubmitErrors(
            form = validForm().copy(tasks = listOf(task)),
            dialogAtdIso = null,
            isAdHocScratch = false,
            selectedCustomerId = null,
        )

        assertNotNull(errors?.tasksByKey?.get(2L)?.attachments)
    }

    @Test
    fun legacy_resource_rows_default_to_quantity_one() {
        assertEquals(1.0, resourceQuantity(emptyMap(), "tool-1"), 0.0)
        assertEquals(
            mapOf("tool-1" to 2.5, "tool-2" to 1.0),
            quantitiesForSelection(
                selectedIds = listOf("tool-1", "tool-2"),
                current = mapOf("tool-1" to 2.5, "removed" to 9.0),
            ),
        )
    }

    @Test
    fun wizard_validation_exposes_only_the_current_step_errors() {
        val form = validForm().copy(
            flightNumber = "",
            serviceLines = listOf(validForm().serviceLines.single().copy(serviceId = null)),
            tasks = listOf(validForm().tasks.single().copy(employeeIds = emptyList())),
        )
        val allErrors = requireNotNull(
            computeCreateWorkOrderSubmitErrors(
                form = form,
                dialogAtdIso = null,
                isAdHocScratch = false,
                selectedCustomerId = null,
            ),
        )

        val flightErrors = requireNotNull(
            submitErrorsForWizardStep(allErrors, WorkOrderWizardStep.Flight),
        )
        assertNotNull(flightErrors.flightNumber)
        assertEquals(emptyMap<Long, ServiceLineSubmitFieldErrors>(), flightErrors.serviceLinesByKey)
        assertEquals(emptyMap<Long, TaskLineSubmitFieldErrors>(), flightErrors.tasksByKey)

        val serviceErrors = requireNotNull(
            submitErrorsForWizardStep(allErrors, WorkOrderWizardStep.ServiceLines),
        )
        assertNull(serviceErrors.flightNumber)
        assertNotNull(serviceErrors.serviceLinesByKey[1L]?.serviceType)

        val taskErrors = requireNotNull(
            submitErrorsForWizardStep(allErrors, WorkOrderWizardStep.Tasks),
        )
        assertNotNull(taskErrors.tasksByKey[2L]?.performers)
        assertNull(submitErrorsForWizardStep(allErrors, WorkOrderWizardStep.Signature))
        assertEquals(WorkOrderWizardStep.Flight, firstWizardStepWithErrors(allErrors))
    }

    private fun validForm(): CreateWorkOrderFormState = CreateWorkOrderFormState(
        flightNumber = "MOB100",
        aircraftTypeId = "aircraft-1",
        aircraftTailNumber = "N100",
        scheduledArrivalIso = "2026-07-11T10:00:00Z",
        scheduledDepartureIso = "2026-07-11T12:00:00Z",
        ataIso = "2026-07-11T10:00:00Z",
        atdIso = "2026-07-11T12:00:00Z",
        serviceLines = listOf(
            ServiceLineFormRow(
                localKey = 1L,
                serviceId = "service-1",
                employeeId = "employee-1",
                fromIso = "2026-07-11T10:00:00Z",
                toIso = "2026-07-11T11:00:00Z",
            ),
        ),
        tasks = listOf(
            TaskFormRow(
                localKey = 2L,
                taskType = TaskTypeKind.Major,
                employeeIds = listOf("employee-1"),
                toolIds = listOf("tool-1"),
                toolQuantities = mapOf("tool-1" to 2.5),
                fromIso = "2026-07-11T10:15:00Z",
                toIso = "2026-07-11T11:30:00Z",
            ),
        ),
    )
}
