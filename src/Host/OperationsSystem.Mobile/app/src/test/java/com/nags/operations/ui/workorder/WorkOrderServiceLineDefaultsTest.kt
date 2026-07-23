package com.nags.operations.ui.workorder

import com.nags.operations.data.db.entities.FlightServiceSummary
import com.nags.operations.data.repo.WorkOrderFlightRow
import com.nags.operations.ui.components.initialSubmitAtdIso
import java.time.Clock
import java.time.Instant
import java.time.ZoneId
import java.time.ZoneOffset
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class WorkOrderServiceLineDefaultsTest {
    @Test
    fun per_landing_work_order_starts_without_service_lines() {
        var allocatedKeys = 0

        val result = serviceLinesToPrefill(
            flight(isPerLanding = true),
            allowedPerformedServiceIds = setOf("service-1"),
            nextKey = { (++allocatedKeys).toLong() },
        )

        assertTrue(result.isEmpty())
        assertEquals(0, allocatedKeys)
    }

    @Test
    fun normal_work_order_prefills_service_identity_with_blank_times() {
        val result = serviceLinesToPrefill(
            flight(isPerLanding = false),
            allowedPerformedServiceIds = setOf("service-1"),
            nextKey = { 7L },
        )

        val line = result.single()
        assertEquals(7L, line.localKey)
        assertEquals("service-1", line.serviceId)
        assertEquals("", line.fromIso)
        assertEquals("", line.toIso)
    }

    @Test
    fun normal_work_order_omits_planned_services_not_allowed_for_the_author() {
        var allocatedKeys = 0

        val result = serviceLinesToPrefill(
            flight(isPerLanding = false),
            allowedPerformedServiceIds = emptySet(),
            nextKey = { (++allocatedKeys).toLong() },
        )

        assertTrue(result.isEmpty())
        assertEquals(0, allocatedKeys)
    }

    @Test
    fun current_timestamp_uses_flight_offset_and_minute_precision() {
        val clock = Clock.fixed(
            Instant.parse("2026-07-18T15:42:37.987Z"),
            ZoneId.of("UTC"),
        )

        assertEquals(
            "2026-07-18T10:42-05:00",
            currentWorkOrderTimestamp(clock, "2026-07-18T08:00:00-05:00"),
        )
        assertEquals(
            "2026-07-18T15:42Z",
            currentWorkOrderTimestamp(clock, "invalid"),
        )
    }

    @Test
    fun section_entry_sets_only_blank_from_values() {
        val form = CreateWorkOrderFormState(
            serviceLines = listOf(
                ServiceLineFormRow(localKey = 1L),
                ServiceLineFormRow(localKey = 2L, fromIso = "2026-07-18T09:00:00Z"),
            ),
            tasks = listOf(TaskFormRow(localKey = 3L)),
        )

        val serviceResult = initializeBlankFromTimes(
            form,
            WorkOrderWizardStep.ServiceLines,
            "2026-07-18T10:00Z",
        )

        assertEquals("2026-07-18T10:00Z", serviceResult.serviceLines[0].fromIso)
        assertEquals("2026-07-18T09:00:00Z", serviceResult.serviceLines[1].fromIso)
        assertEquals("", serviceResult.tasks.single().fromIso)

        val taskResult = initializeBlankFromTimes(
            serviceResult,
            WorkOrderWizardStep.Tasks,
            "2026-07-18T10:30Z",
        )
        assertEquals("2026-07-18T10:30Z", taskResult.tasks.single().fromIso)
    }

    @Test
    fun next_sets_only_blank_to_values_for_the_current_section() {
        val form = CreateWorkOrderFormState(
            serviceLines = listOf(
                ServiceLineFormRow(localKey = 1L),
                ServiceLineFormRow(localKey = 2L, toIso = "2026-07-18T11:00:00Z"),
            ),
            tasks = listOf(TaskFormRow(localKey = 3L)),
        )

        val result = finalizeBlankToTimes(
            form,
            WorkOrderWizardStep.ServiceLines,
            "2026-07-18T12:00Z",
        )

        assertEquals("2026-07-18T12:00Z", result.serviceLines[0].toIso)
        assertEquals("2026-07-18T11:00:00Z", result.serviceLines[1].toIso)
        assertEquals("", result.tasks.single().toIso)
    }

    @Test
    fun next_sets_only_blank_task_to_values_and_preserves_service_times() {
        val form = CreateWorkOrderFormState(
            serviceLines = listOf(
                ServiceLineFormRow(
                    localKey = 1L,
                    fromIso = "2026-07-18T09:00Z",
                    toIso = "2026-07-18T09:30Z",
                ),
            ),
            tasks = listOf(
                TaskFormRow(localKey = 2L, fromIso = "2026-07-18T10:00Z"),
                TaskFormRow(
                    localKey = 3L,
                    fromIso = "2026-07-18T10:15Z",
                    toIso = "2026-07-18T10:45Z",
                ),
            ),
        )

        val result = finalizeBlankToTimes(
            form,
            WorkOrderWizardStep.Tasks,
            "2026-07-18T11:00Z",
        )

        assertEquals(form.serviceLines, result.serviceLines)
        assertEquals("2026-07-18T11:00Z", result.tasks[0].toIso)
        assertEquals("2026-07-18T10:45Z", result.tasks[1].toIso)
    }

    @Test
    fun manually_added_rows_use_add_click_time_and_leave_to_blank() {
        val service = newServiceLineAt(
            localKey = 10L,
            employeeIds = listOf("employee-1"),
            timestampIso = "2026-07-18T10:42-05:00",
        )
        val task = newTaskAt(
            localKey = 11L,
            employeeIds = listOf("employee-1"),
            timestampIso = "2026-07-18T10:43-05:00",
        )

        assertEquals("2026-07-18T10:42-05:00", service.fromIso)
        assertEquals("", service.toIso)
        assertEquals("2026-07-18T10:43-05:00", task.fromIso)
        assertEquals("", task.toIso)
    }

    @Test
    fun restored_explicit_from_and_to_values_are_never_overwritten() {
        val form = CreateWorkOrderFormState(
            serviceLines = listOf(
                ServiceLineFormRow(
                    localKey = 1L,
                    fromIso = "2026-07-18T09:00Z",
                    toIso = "2026-07-18T09:30Z",
                ),
            ),
            tasks = listOf(
                TaskFormRow(
                    localKey = 2L,
                    fromIso = "2026-07-18T10:00Z",
                    toIso = "2026-07-18T10:30Z",
                ),
            ),
        )

        val afterEntry = initializeBlankFromTimes(
            initializeBlankFromTimes(
                form,
                WorkOrderWizardStep.ServiceLines,
                "2026-07-18T12:00Z",
            ),
            WorkOrderWizardStep.Tasks,
            "2026-07-18T12:00Z",
        )
        val afterNext = finalizeBlankToTimes(
            finalizeBlankToTimes(
                afterEntry,
                WorkOrderWizardStep.ServiceLines,
                "2026-07-18T12:30Z",
            ),
            WorkOrderWizardStep.Tasks,
            "2026-07-18T12:30Z",
        )

        assertEquals(form, afterNext)
    }

    @Test
    fun atd_dialog_preserves_restored_value_or_uses_frozen_flight_local_minute() {
        val clock = Clock.fixed(
            Instant.parse("2026-07-18T15:42:37.987Z"),
            ZoneId.of("UTC"),
        )

        assertEquals(
            "2026-07-18T08:05:00-05:00",
            initialSubmitAtdIso(
                restoredAtdIso = "2026-07-18T08:05:00-05:00",
                flightOffset = ZoneOffset.ofHours(-5),
                clock = clock,
            ),
        )
        assertEquals(
            "2026-07-18T10:42-05:00",
            initialSubmitAtdIso(
                restoredAtdIso = "",
                flightOffset = ZoneOffset.ofHours(-5),
                clock = clock,
            ),
        )
    }

    private fun flight(isPerLanding: Boolean) = WorkOrderFlightRow(
        id = "flight-1",
        flightNumber = "100",
        operationTypeName = "Transit",
        sta = "2026-07-18T10:00:00Z",
        std = "2026-07-18T12:00:00Z",
        aircraftTypeId = null,
        aircraftTypeModel = null,
        customerName = "Customer",
        customerIataCode = "NA",
        stationIata = "ORD",
        isPerLanding = isPerLanding,
        isAdHoc = false,
        plannedServices = listOf(
            FlightServiceSummary("per-landing", "Aircraft Per Landing", isAircraftPerLanding = true),
            FlightServiceSummary("service-1", "Baggage"),
        ),
    )
}
