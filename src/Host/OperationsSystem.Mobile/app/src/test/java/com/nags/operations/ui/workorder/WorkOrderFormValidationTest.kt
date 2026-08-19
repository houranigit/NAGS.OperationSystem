package com.nags.operations.ui.workorder

import com.nags.operations.data.TaskTypeKind
import com.nags.operations.data.ResourceCalculationType
import com.nags.operations.data.WellKnownMasterDataIds
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class WorkOrderFormValidationTest {
    @Test
    fun valid_portal_equivalent_form_passes() {
        val errors = computeCreateWorkOrderSubmitErrors(
            form = validForm(),
            dialogAtdIso = null,
            validationPhase = WorkOrderValidationPhase.Submission,
            isAdHocScratch = true,
            selectedCustomerId = "customer-1",
            allowedPerformedServiceIds = setOf("service-1"),
        )

        assertNull(errors)
    }

    @Test
    fun scratch_customer_and_remarks_validation_covers_all_business_combinations() {
        data class Case(
            val customerId: String?,
            val remarks: String,
            val expectedRemarksError: Boolean,
        )

        listOf(
            Case(customerId = null, remarks = "", expectedRemarksError = true),
            Case(customerId = null, remarks = "Walk-in customer from terminal 2", expectedRemarksError = false),
            Case(
                customerId = WellKnownMasterDataIds.UnknownCustomer,
                remarks = "",
                expectedRemarksError = true,
            ),
            Case(
                customerId = WellKnownMasterDataIds.UnknownCustomer,
                remarks = "Badge name: A. Smith",
                expectedRemarksError = false,
            ),
            Case(customerId = "customer-1", remarks = "", expectedRemarksError = false),
            Case(customerId = "customer-1", remarks = "Optional note", expectedRemarksError = false),
        ).forEach { case ->
            val errors = computeCreateWorkOrderSubmitErrors(
                form = validForm().copy(remarks = case.remarks),
                dialogAtdIso = null,
                validationPhase = WorkOrderValidationPhase.Submission,
                isAdHocScratch = true,
                selectedCustomerId = case.customerId,
                allowedPerformedServiceIds = setOf("service-1"),
            )

            assertEquals(
                "Unexpected remarks validation for customer=${case.customerId}, remarks=${case.remarks}",
                case.expectedRemarksError,
                errors?.remarks != null,
            )
            assertNull(errors?.customer)
        }
    }

    @Test
    fun non_scratch_work_order_does_not_require_remarks_without_a_customer_selection() {
        val errors = computeCreateWorkOrderSubmitErrors(
            form = validForm().copy(remarks = ""),
            dialogAtdIso = null,
            validationPhase = WorkOrderValidationPhase.Submission,
            isAdHocScratch = false,
            selectedCustomerId = null,
            allowedPerformedServiceIds = setOf("service-1"),
        )

        assertNull(errors)
    }

    @Test
    fun blank_or_unknown_customer_detection_is_id_based_and_case_insensitive() {
        assertTrue(isBlankOrUnknownCustomer(null))
        assertTrue(isBlankOrUnknownCustomer(""))
        assertTrue(isBlankOrUnknownCustomer("   "))
        assertTrue(isBlankOrUnknownCustomer(WellKnownMasterDataIds.UnknownCustomer.uppercase()))
        assertFalse(isBlankOrUnknownCustomer("customer-1"))
    }

    @Test
    fun scratch_customer_resolution_maps_only_blank_values_to_unknown() {
        assertEquals(
            WellKnownMasterDataIds.UnknownCustomer,
            resolveScratchCustomerId(null),
        )
        assertEquals(
            WellKnownMasterDataIds.UnknownCustomer,
            resolveScratchCustomerId("   "),
        )
        assertEquals("customer-1", resolveScratchCustomerId("customer-1"))
    }

    @Test
    fun work_order_without_service_lines_passes_validation() {
        val errors = computeCreateWorkOrderSubmitErrors(
            form = validForm().copy(serviceLines = emptyList()),
            dialogAtdIso = null,
            validationPhase = WorkOrderValidationPhase.Submission,
            isAdHocScratch = false,
            selectedCustomerId = null,
            allowedPerformedServiceIds = setOf("service-1"),
        )

        assertNull(errors)
    }

    @Test
    fun service_line_requires_at_least_one_performer() {
        val form = validForm().copy(
            serviceLines = listOf(validForm().serviceLines.single().copy(employeeIds = emptyList())),
        )

        val errors = computeCreateWorkOrderSubmitErrors(
            form = form,
            dialogAtdIso = null,
            validationPhase = WorkOrderValidationPhase.Submission,
            isAdHocScratch = false,
            selectedCustomerId = null,
            allowedPerformedServiceIds = setOf("service-1"),
        )

        assertNotNull(errors?.serviceLinesByKey?.get(1L)?.performer)
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
                    toolUsages = mapOf(
                        "tool-1" to ResourceUsageForm(
                            calculationType = ResourceCalculationType.Quantity,
                            quantity = 0.0,
                        ),
                    ),
                ),
            ),
        )

        val errors = computeCreateWorkOrderSubmitErrors(
            form = form,
            dialogAtdIso = null,
            validationPhase = WorkOrderValidationPhase.Submission,
            isAdHocScratch = false,
            selectedCustomerId = null,
            allowedPerformedServiceIds = setOf("service-1"),
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
            validationPhase = WorkOrderValidationPhase.Submission,
            isAdHocScratch = true,
            selectedCustomerId = "customer-1",
            allowedPerformedServiceIds = setOf("service-1"),
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
            validationPhase = WorkOrderValidationPhase.Submission,
            isAdHocScratch = false,
            selectedCustomerId = null,
            allowedPerformedServiceIds = setOf("service-1"),
        )

        assertNotNull(errors?.tasksByKey?.get(2L)?.attachments)
    }

    @Test
    fun existing_and_new_service_attachments_share_the_server_limit() {
        val service = validForm().serviceLines.single().copy(
            existingAttachmentNames = List(WorkOrderFormLimits.ServiceAttachments) {
                "existing-$it.pdf"
            },
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
            form = validForm().copy(serviceLines = listOf(service)),
            dialogAtdIso = null,
            validationPhase = WorkOrderValidationPhase.Submission,
            isAdHocScratch = false,
            selectedCustomerId = null,
            allowedPerformedServiceIds = setOf("service-1"),
        )

        assertNotNull(errors?.serviceLinesByKey?.get(1L)?.attachments)
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
    fun legacy_quantity_only_tool_is_bridged_to_task_duration() {
        val usage = resourceUsage(
            itemId = "tool-1",
            usages = emptyMap(),
            legacyQuantities = mapOf("tool-1" to 2.5),
            calculationType = ResourceCalculationType.Duration,
            taskFromIso = "2026-07-11T10:15:00Z",
            taskToIso = "2026-07-11T11:30:00Z",
        )

        assertEquals(ResourceCalculationType.Duration, usage.calculationType)
        assertNull(usage.quantity)
        assertEquals("2026-07-11T10:15:00Z", usage.fromIso)
        assertEquals("2026-07-11T11:30:00Z", usage.toIso)
    }

    @Test
    fun duration_resource_allows_open_to_but_must_stay_inside_task() {
        val task = validForm().tasks.single()
        val openUsage = ResourceUsageForm(
            calculationType = ResourceCalculationType.Duration,
            quantity = null,
            fromIso = "2026-07-11T10:30:00Z",
            toIso = null,
        )
        val valid = validForm().copy(
            tasks = listOf(task.copy(toolUsages = mapOf("tool-1" to openUsage))),
        )

        assertNull(
            computeCreateWorkOrderSubmitErrors(
                form = valid,
                dialogAtdIso = null,
                validationPhase = WorkOrderValidationPhase.Submission,
                isAdHocScratch = false,
                selectedCustomerId = null,
                allowedPerformedServiceIds = setOf("service-1"),
            ),
        )

        val outside = valid.copy(
            tasks = listOf(task.copy(
                toolUsages = mapOf("tool-1" to openUsage.copy(fromIso = "2026-07-11T10:00:00Z")),
            )),
        )
        val errors = computeCreateWorkOrderSubmitErrors(
            form = outside,
            dialogAtdIso = null,
            validationPhase = WorkOrderValidationPhase.Submission,
            isAdHocScratch = false,
            selectedCustomerId = null,
            allowedPerformedServiceIds = setOf("service-1"),
        )
        assertNotNull(errors?.tasksByKey?.get(2L)?.tools)
    }

    @Test
    fun resource_quantity_enforces_decimal_18_2_shape() {
        assertTrue(isValidResourceQuantity(12.25))
        assertFalse(isValidResourceQuantity(1.001))
        assertFalse(isValidResourceQuantity(10_000_000_000_000_000.0))
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
                validationPhase = WorkOrderValidationPhase.Submission,
                isAdHocScratch = false,
                selectedCustomerId = null,
                allowedPerformedServiceIds = setOf("service-1"),
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

    @Test
    fun revoked_service_line_is_visible_but_blocks_resubmission() {
        val form = validForm().copy(
            serviceLines = listOf(
                validForm().serviceLines.single().copy(serviceName = "Revoked service"),
            ),
        )

        val errors = computeCreateWorkOrderSubmitErrors(
            form = form,
            dialogAtdIso = null,
            validationPhase = WorkOrderValidationPhase.Submission,
            isAdHocScratch = false,
            selectedCustomerId = null,
            allowedPerformedServiceIds = emptySet(),
        )

        val message = errors?.serviceLinesByKey?.get(1L)?.serviceType
        assertNotNull(message)
        assertEquals(true, message!!.contains("no longer allowed"))
        assertEquals("Revoked service", form.serviceLines.single().serviceName)
    }

    @Test
    fun before_atd_validation_ignores_missing_or_invalid_atd_and_atd_line_bounds() {
        listOf("", "not-a-date").forEach { atdIso ->
            val form = validForm().copy(
                atdIso = atdIso,
                serviceLines = listOf(
                    validForm().serviceLines.single().copy(toIso = "2026-07-11T13:00:00Z"),
                ),
                tasks = listOf(
                    validForm().tasks.single().copy(toIso = "2026-07-11T13:30:00Z"),
                ),
            )

            assertNull(
                validationErrors(
                    form = form,
                    phase = WorkOrderValidationPhase.BeforeAtd,
                ),
            )
        }
    }

    @Test
    fun before_atd_ignores_stale_atd_but_still_rejects_intrinsic_line_dates() {
        val form = validForm().copy(
            atdIso = "2026-07-11T10:01:00Z",
            serviceLines = listOf(
                validForm().serviceLines.single().copy(
                    fromIso = "2026-07-11T09:59:00Z",
                    toIso = "2026-07-11T09:58:00Z",
                ),
            ),
        )

        val errors = requireNotNull(
            validationErrors(
                form = form,
                phase = WorkOrderValidationPhase.BeforeAtd,
            ),
        )

        assertNull(errors.atd)
        assertTrue(errors.serviceLinesByKey.getValue(1L).from.orEmpty().contains("before actual arrival"))
        assertTrue(errors.serviceLinesByKey.getValue(1L).to.orEmpty().contains("on or after From"))
    }

    @Test
    fun submission_validation_requires_atd() {
        val errors = validationErrors(validForm().copy(atdIso = ""))

        assertEquals("ATD is required.", errors?.atd)
    }

    @Test
    fun submission_rejects_malformed_atd() {
        assertEquals(
            "Invalid ATD date or time.",
            validationErrors(validForm().copy(atdIso = "not-a-date"))?.atd,
        )
    }

    @Test
    fun dialog_atd_overrides_the_stored_form_value() {
        val withoutLines = validForm().copy(
            serviceLines = emptyList(),
            tasks = emptyList(),
        )

        assertNull(
            validationErrors(
                form = withoutLines.copy(atdIso = "not-a-date"),
                dialogAtdIso = "2026-07-11T10:01:00Z",
            ),
        )
        assertEquals(
            "Departure (ATD) must be after arrival (ATA).",
            validationErrors(
                form = withoutLines.copy(atdIso = "2026-07-11T12:00:00Z"),
                dialogAtdIso = "2026-07-11T10:00:00Z",
            )?.atd,
        )
    }

    @Test
    fun submission_requires_atd_to_be_strictly_after_ata() {
        val formWithoutLines = validForm().copy(
            serviceLines = emptyList(),
            tasks = emptyList(),
        )

        val beforeErrors = validationErrors(
            formWithoutLines.copy(atdIso = "2026-07-11T09:59:00Z"),
        )
        val equalErrors = validationErrors(
            formWithoutLines.copy(atdIso = "2026-07-11T10:00:00Z"),
        )
        val afterErrors = validationErrors(
            formWithoutLines.copy(atdIso = "2026-07-11T10:01:00Z"),
        )

        assertEquals(
            "Departure (ATD) must be after arrival (ATA).",
            beforeErrors?.atd,
        )
        assertEquals(
            "Departure (ATD) must be after arrival (ATA).",
            equalErrors?.atd,
        )
        assertNull(afterErrors)
    }

    @Test
    fun service_and_task_from_may_equal_but_cannot_precede_ata() {
        val equalForm = validForm().copy(
            serviceLines = listOf(
                validForm().serviceLines.single().copy(fromIso = "2026-07-11T10:00:00Z"),
            ),
            tasks = listOf(
                validForm().tasks.single().copy(fromIso = "2026-07-11T10:00:00Z"),
            ),
        )
        assertNull(validationErrors(equalForm))

        val beforeForm = equalForm.copy(
            serviceLines = listOf(
                equalForm.serviceLines.single().copy(fromIso = "2026-07-11T09:59:00Z"),
            ),
            tasks = listOf(
                equalForm.tasks.single().copy(fromIso = "2026-07-11T09:59:00Z"),
            ),
        )
        val errors = validationErrors(beforeForm)

        assertNotNull(errors?.serviceLinesByKey?.get(1L)?.from)
        assertNotNull(errors?.tasksByKey?.get(2L)?.from)
    }

    @Test
    fun service_and_task_to_may_equal_but_cannot_precede_from() {
        val equalForm = validForm().copy(
            serviceLines = listOf(
                validForm().serviceLines.single().copy(
                    toIso = validForm().serviceLines.single().fromIso,
                ),
            ),
            tasks = listOf(
                validForm().tasks.single().copy(
                    toIso = validForm().tasks.single().fromIso,
                ),
            ),
        )
        assertNull(validationErrors(equalForm))

        val beforeForm = equalForm.copy(
            serviceLines = listOf(
                equalForm.serviceLines.single().copy(toIso = "2026-07-11T09:59:00Z"),
            ),
            tasks = listOf(
                equalForm.tasks.single().copy(toIso = "2026-07-11T09:59:00Z"),
            ),
        )
        val errors = validationErrors(beforeForm)

        assertTrue(errors?.serviceLinesByKey?.get(1L)?.to.orEmpty().contains("on or after From"))
        assertTrue(errors?.tasksByKey?.get(2L)?.to.orEmpty().contains("on or after From"))
    }

    @Test
    fun dialog_atd_before_line_ends_sets_atd_and_row_errors() {
        val form = validForm().copy(
            serviceLines = listOf(
                validForm().serviceLines.single().copy(toIso = "2026-07-11T11:01:00Z"),
            ),
            tasks = listOf(
                validForm().tasks.single().copy(toIso = "2026-07-11T11:30:00Z"),
            ),
        )

        val errors = requireNotNull(
            validationErrors(
                form = form,
                dialogAtdIso = "2026-07-11T11:00:00Z",
            ),
        )

        assertTrue(errors.atd.orEmpty().contains("service or task end time"))
        assertTrue(errors.serviceLinesByKey.getValue(1L).to.orEmpty().contains("after departure"))
        assertTrue(errors.tasksByKey.getValue(2L).to.orEmpty().contains("after departure"))
    }

    @Test
    fun date_time_boundaries_compare_instants_across_offsets() {
        val equalAtaAndAtd = validForm().copy(
            ataIso = "2026-07-11T10:00:00Z",
            atdIso = "2026-07-11T05:00:00-05:00",
            serviceLines = emptyList(),
            tasks = emptyList(),
        )
        assertEquals(
            "Departure (ATD) must be after arrival (ATA).",
            validationErrors(equalAtaAndAtd)?.atd,
        )

        val validOffsetBoundaries = validForm().copy(
            ataIso = "2026-07-11T10:00:00Z",
            atdIso = "2026-07-11T07:00:00-05:00",
            serviceLines = listOf(
                validForm().serviceLines.single().copy(
                    fromIso = "2026-07-11T05:00:00-05:00",
                    toIso = "2026-07-11T07:00:00-05:00",
                ),
            ),
            tasks = emptyList(),
        )
        assertNull(validationErrors(validOffsetBoundaries))
    }

    @Test
    fun task_boundaries_compare_instants_across_offsets() {
        val form = validForm().copy(
            serviceLines = emptyList(),
            tasks = listOf(
                validForm().tasks.single().copy(
                    fromIso = "2026-07-11T04:59:00-05:00",
                    toIso = "2026-07-11T07:01:00-05:00",
                ),
            ),
        )

        val errors = requireNotNull(validationErrors(form))

        assertTrue(errors.tasksByKey.getValue(2L).from.orEmpty().contains("before actual arrival"))
        assertTrue(errors.tasksByKey.getValue(2L).to.orEmpty().contains("after departure"))
    }

    @Test
    fun wizard_sequences_omit_return_to_ramps_for_creation_and_keep_it_for_update() {
        assertEquals(
            listOf(
                WorkOrderWizardStep.Flight,
                WorkOrderWizardStep.ServiceLines,
                WorkOrderWizardStep.Tasks,
                WorkOrderWizardStep.Signature,
            ),
            workOrderWizardSteps(includeReturnToRamps = false),
        )
        assertEquals(
            listOf(
                WorkOrderWizardStep.Flight,
                WorkOrderWizardStep.ServiceLines,
                WorkOrderWizardStep.Tasks,
                WorkOrderWizardStep.ReturnToRamps,
                WorkOrderWizardStep.Signature,
            ),
            workOrderWizardSteps(includeReturnToRamps = true),
        )
        assertNull(validationErrors(validForm().copy(returnToRamps = emptyList())))
    }

    @Test
    fun wizard_navigation_skips_hidden_step_only_for_creation() {
        assertEquals(
            WorkOrderWizardStep.Signature,
            nextWorkOrderWizardStep(
                WorkOrderWizardStep.Tasks,
                includeReturnToRamps = false,
            ),
        )
        assertEquals(
            WorkOrderWizardStep.Tasks,
            previousWorkOrderWizardStep(
                WorkOrderWizardStep.Signature,
                includeReturnToRamps = false,
            ),
        )
        assertFalse(
            isEarlierWorkOrderWizardStep(
                candidate = WorkOrderWizardStep.ReturnToRamps,
                currentStep = WorkOrderWizardStep.Signature,
                includeReturnToRamps = false,
            ),
        )

        assertEquals(
            WorkOrderWizardStep.ReturnToRamps,
            nextWorkOrderWizardStep(
                WorkOrderWizardStep.Tasks,
                includeReturnToRamps = true,
            ),
        )
        assertEquals(
            WorkOrderWizardStep.ReturnToRamps,
            previousWorkOrderWizardStep(
                WorkOrderWizardStep.Signature,
                includeReturnToRamps = true,
            ),
        )
        assertTrue(
            isEarlierWorkOrderWizardStep(
                candidate = WorkOrderWizardStep.ReturnToRamps,
                currentStep = WorkOrderWizardStep.Signature,
                includeReturnToRamps = true,
            ),
        )
    }

    @Test
    fun wizard_error_routing_does_not_target_hidden_creation_step() {
        val errors = CreateWorkOrderSubmitFieldErrors(
            returnToRampsByKey = mapOf(
                10L to ReturnToRampSubmitFieldErrors(activity = "Add at least one service or task."),
            ),
        )

        assertEquals(
            WorkOrderWizardStep.Signature,
            firstWizardStepWithErrors(errors, includeReturnToRamps = false),
        )
        assertEquals(
            WorkOrderWizardStep.ReturnToRamps,
            firstWizardStepWithErrors(errors, includeReturnToRamps = true),
        )
    }

    @Test
    fun creation_mode_discards_hidden_return_to_ramp_data_while_update_preserves_it() {
        val occurrence = ReturnToRampFormRow(localKey = 10L)
        val form = validForm().copy(returnToRamps = listOf(occurrence))

        val creationForm = formForWorkOrderWizardMode(
            form,
            includeReturnToRamps = false,
        )
        val updateForm = formForWorkOrderWizardMode(
            form,
            includeReturnToRamps = true,
        )

        assertTrue(creationForm.returnToRamps.isEmpty())
        assertEquals(form, updateForm)
        assertNull(validationErrors(creationForm))
        assertNotNull(validationErrors(updateForm))
    }

    @Test
    fun legacy_flat_return_to_ramp_rows_are_dropped_for_creation_and_grouped_for_update() {
        val normalService = validForm().serviceLines.single()
        val legacyService = ServiceLineFormRow(
            localKey = 10L,
            serverId = "legacy-service",
            serviceId = "service-1",
            employeeIds = listOf("employee-1"),
            fromIso = "2026-07-11T13:00:00Z",
            toIso = "2026-07-11T13:20:00Z",
            existingAttachmentNames = listOf("ramp-photo.jpg"),
            returnToRamp = true,
        )
        val legacyTask = TaskFormRow(
            localKey = 11L,
            serverId = "legacy-task",
            employeeIds = listOf("employee-1"),
            fromIso = "2026-07-11T12:55:00Z",
            toIso = "2026-07-11T13:30:00Z",
            returnToRamp = true,
        )
        val legacyForm = validForm().copy(
            serviceLines = listOf(normalService, legacyService),
            tasks = listOf(legacyTask),
            returnToRamps = emptyList(),
        )

        val creation = formForWorkOrderWizardMode(
            legacyForm,
            includeReturnToRamps = false,
        )
        assertEquals(listOf(normalService), creation.serviceLines)
        assertTrue(creation.tasks.isEmpty())
        assertTrue(creation.returnToRamps.isEmpty())

        assertTrue(returnToRampRowsNeedIdentityResolution(legacyForm))
        val canonicalOccurrence = ReturnToRampFormRow(
            localKey = 20L,
            serverId = "occurrence-20",
            fromIso = "2026-07-11T12:50:00Z",
            toIso = "2026-07-11T13:35:00Z",
            serviceLines = listOf(ServiceLineFormRow(localKey = 21L, serverId = "legacy-service")),
            tasks = listOf(TaskFormRow(localKey = 22L, serverId = "legacy-task")),
        )
        val reconciled = requireNotNull(
            reconcileLegacyReturnToRampRows(legacyForm, listOf(canonicalOccurrence)),
        )
        val update = formForWorkOrderWizardMode(reconciled, includeReturnToRamps = true)
        assertEquals(listOf(normalService), update.serviceLines)
        assertTrue(update.tasks.isEmpty())
        val occurrence = update.returnToRamps.single()
        assertEquals("occurrence-20", occurrence.serverId)
        assertEquals("2026-07-11T12:50:00Z", occurrence.fromIso)
        assertEquals("2026-07-11T13:35:00Z", occurrence.toIso)
        assertEquals("legacy-service", occurrence.serviceLines.single().serverId)
        assertEquals(
            listOf("ramp-photo.jpg"),
            occurrence.serviceLines.single().existingAttachmentNames,
        )
        assertFalse(occurrence.serviceLines.single().returnToRamp)
        assertEquals("legacy-task", occurrence.tasks.single().serverId)
        assertFalse(occurrence.tasks.single().returnToRamp)
    }

    @Test
    fun legacy_flat_update_rows_fail_identity_reconciliation_when_cache_cannot_match_them() {
        val form = validForm().copy(
            serviceLines = validForm().serviceLines + ServiceLineFormRow(
                localKey = 50L,
                serverId = "missing-service",
                returnToRamp = true,
                existingAttachmentNames = listOf("must-not-be-lost.pdf"),
            ),
        )
        val canonical = ReturnToRampFormRow(
            localKey = 60L,
            serverId = "occurrence-60",
            serviceLines = listOf(ServiceLineFormRow(localKey = 61L, serverId = "different-service")),
        )

        assertNull(reconcileLegacyReturnToRampRows(form, listOf(canonical)))
        assertEquals(form, formForWorkOrderWizardMode(form, includeReturnToRamps = true))
    }

    @Test
    fun grouped_legacy_update_occurrence_restores_parent_identity_and_keeps_attachment_metadata() {
        val legacyOccurrence = ReturnToRampFormRow(
            localKey = 70L,
            serverId = null,
            fromIso = "2026-07-11T14:00:00Z",
            toIso = "2026-07-11T14:20:00Z",
            description = "Returned after pushback",
            serviceLines = listOf(
                ServiceLineFormRow(
                    localKey = 71L,
                    serverId = "legacy-grouped-service",
                    serviceId = "service-1",
                    employeeIds = listOf("employee-1"),
                    existingAttachmentNames = listOf("existing-ramp-photo.jpg"),
                ),
            ),
            tasks = listOf(
                TaskFormRow(
                    localKey = 72L,
                    serverId = "legacy-grouped-task",
                    employeeIds = listOf("employee-1"),
                    existingAttachmentNames = listOf("existing-ramp-note.pdf"),
                ),
            ),
        )
        val form = validForm().copy(returnToRamps = listOf(legacyOccurrence))
        val canonical = ReturnToRampFormRow(
            localKey = 80L,
            serverId = "canonical-occurrence",
            fromIso = "2026-07-11T13:55:00Z",
            toIso = "2026-07-11T14:25:00Z",
            serviceLines = listOf(
                ServiceLineFormRow(localKey = 81L, serverId = "legacy-grouped-service"),
            ),
            tasks = listOf(
                TaskFormRow(localKey = 82L, serverId = "legacy-grouped-task"),
            ),
        )

        assertTrue(returnToRampRowsNeedIdentityResolution(form))
        assertEquals(form, formForWorkOrderWizardMode(form, includeReturnToRamps = true))

        val reconciled = requireNotNull(
            reconcileLegacyReturnToRampRows(form, listOf(canonical)),
        )
        val occurrence = reconciled.returnToRamps.single()
        assertEquals("canonical-occurrence", occurrence.serverId)
        assertEquals("2026-07-11T13:55:00Z", occurrence.fromIso)
        assertEquals("2026-07-11T14:25:00Z", occurrence.toIso)
        assertEquals(
            listOf("existing-ramp-photo.jpg"),
            occurrence.serviceLines.single().existingAttachmentNames,
        )
        assertEquals(
            listOf("existing-ramp-note.pdf"),
            occurrence.tasks.single().existingAttachmentNames,
        )
        assertNull(reconcileLegacyReturnToRampRows(form, emptyList()))
    }

    @Test
    fun grouped_update_occurrences_win_over_legacy_flat_compatibility_aliases() {
        val alias = validForm().serviceLines.single().copy(
            localKey = 21L,
            serverId = "compatibility-alias",
            returnToRamp = true,
        )
        val grouped = ReturnToRampFormRow(
            localKey = 20L,
            serverId = "occurrence-20",
            fromIso = "2026-07-11T13:00:00Z",
            toIso = "2026-07-11T13:30:00Z",
            serviceLines = listOf(alias.copy(localKey = 22L, returnToRamp = false)),
        )
        val form = validForm().copy(
            serviceLines = validForm().serviceLines + alias,
            returnToRamps = listOf(grouped),
        )

        val normalized = formForWorkOrderWizardMode(form, includeReturnToRamps = true)

        assertEquals(validForm().serviceLines, normalized.serviceLines)
        assertEquals(listOf(grouped), normalized.returnToRamps)
    }

    @Test
    fun mixed_new_group_and_unmatched_flat_server_row_require_identity_resolution() {
        val protectedFlatRow = validForm().serviceLines.single().copy(
            localKey = 90L,
            serverId = "server-rtr-service",
            returnToRamp = true,
            existingAttachmentNames = listOf("do-not-drop.pdf"),
        )
        val newOccurrence = ReturnToRampFormRow(
            localKey = 91L,
            serverId = null,
            description = "New unsaved occurrence",
        )
        val form = validForm().copy(
            serviceLines = validForm().serviceLines + protectedFlatRow,
            returnToRamps = listOf(newOccurrence),
        )

        assertTrue(returnToRampRowsNeedIdentityResolution(form))
        assertEquals(form, formForWorkOrderWizardMode(form, includeReturnToRamps = true))
        assertNull(reconcileLegacyReturnToRampRows(form, emptyList()))
    }

    @Test
    fun idless_flat_return_to_ramp_row_is_grouped_without_losing_its_new_attachment() {
        val newAttachment = TaskAttachmentDraft(
            kind = "Image",
            contentType = "image/jpeg",
            fileName = "new-ramp-photo.jpg",
            base64 = "AQID",
            capturedAtIso = "2026-07-11T14:05:00Z",
            sizeBytes = 3L,
        )
        val idlessFlatRow = ServiceLineFormRow(
            localKey = 101L,
            serviceId = "service-1",
            employeeIds = listOf("employee-1"),
            fromIso = "2026-07-11T14:00:00Z",
            toIso = "2026-07-11T14:10:00Z",
            attachments = listOf(newAttachment),
            returnToRamp = true,
        )
        val canonicalOccurrence = ReturnToRampFormRow(
            localKey = 102L,
            serverId = "existing-occurrence",
            fromIso = "2026-07-11T13:00:00Z",
            toIso = "2026-07-11T13:15:00Z",
        )
        val form = validForm().copy(
            serviceLines = validForm().serviceLines + idlessFlatRow,
            returnToRamps = listOf(canonicalOccurrence),
        )

        assertFalse(returnToRampRowsNeedIdentityResolution(form))
        val normalized = formForWorkOrderWizardMode(form, includeReturnToRamps = true)

        assertEquals(validForm().serviceLines, normalized.serviceLines)
        assertEquals(canonicalOccurrence, normalized.returnToRamps.first())
        val migrated = normalized.returnToRamps.last()
        assertNull(migrated.serverId)
        assertEquals(listOf(newAttachment), migrated.serviceLines.single().attachments)
        assertFalse(migrated.serviceLines.single().returnToRamp)
    }

    @Test
    fun multiple_occurrences_validate_independently_with_nested_rows() {
        val first = ReturnToRampFormRow(
            localKey = 10,
            fromIso = "2026-07-11T12:30:00Z",
            toIso = "2026-07-11T13:00:00Z",
            description = "First occurrence",
            serviceLines = listOf(
                ServiceLineFormRow(
                    localKey = 11,
                    serviceId = "service-1",
                    employeeIds = listOf("employee-1"),
                    fromIso = "2026-07-11T12:30:00Z",
                    toIso = "2026-07-11T12:45:00Z",
                ),
            ),
        )
        val second = ReturnToRampFormRow(
            localKey = 20,
            fromIso = "2026-07-11T14:00:00Z",
            toIso = "2026-07-11T15:00:00Z",
            description = "Second occurrence",
            tasks = listOf(
                TaskFormRow(
                    localKey = 21,
                    employeeIds = listOf("employee-2"),
                    fromIso = "2026-07-11T14:10:00Z",
                    toIso = "2026-07-11T14:50:00Z",
                ),
            ),
        )
        assertNull(validationErrors(validForm().copy(returnToRamps = listOf(first, second))))

        val escaped = second.copy(
            tasks = listOf(second.tasks.single().copy(toIso = "2026-07-11T15:01:00Z")),
        )
        val errors = requireNotNull(
            validationErrors(validForm().copy(returnToRamps = listOf(first, escaped))),
        )
        assertTrue(
            errors.returnToRampsByKey.getValue(20).tasksByKey.getValue(21).to
                .orEmpty().contains("return-to-ramp To"),
        )
        assertNotNull(submitErrorsForWizardStep(errors, WorkOrderWizardStep.ReturnToRamps))
    }

    private fun validationErrors(
        form: CreateWorkOrderFormState,
        phase: WorkOrderValidationPhase = WorkOrderValidationPhase.Submission,
        dialogAtdIso: String? = null,
    ): CreateWorkOrderSubmitFieldErrors? = computeCreateWorkOrderSubmitErrors(
        form = form,
        dialogAtdIso = dialogAtdIso,
        validationPhase = phase,
        isAdHocScratch = false,
        selectedCustomerId = null,
        allowedPerformedServiceIds = setOf("service-1"),
    )

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
                employeeIds = listOf("employee-1", "employee-2"),
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
