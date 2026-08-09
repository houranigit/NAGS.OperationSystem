package com.nags.operations.ui.workorder

import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Test

class ReturnToRampAllowanceTest {
    @Test
    fun occurrence_is_validated_only_against_its_new_nested_activity() {
        val occurrence = ReturnToRampFormRow(
            localKey = 1,
            fromIso = "2026-08-08T10:00:00Z",
            toIso = "2026-08-08T11:00:00Z",
            serviceLines = listOf(
                ServiceLineFormRow(
                    localKey = 2,
                    serviceId = "allowed",
                    employeeIds = listOf("employee"),
                    fromIso = "2026-08-08T10:10:00Z",
                    toIso = "2026-08-08T10:50:00Z",
                ),
            ),
        )

        assertNull(computeReturnToRampErrors(occurrence, setOf("allowed")))
    }

    @Test
    fun nested_activity_cannot_escape_occurrence_window() {
        val occurrence = ReturnToRampFormRow(
            localKey = 1,
            fromIso = "2026-08-08T10:00:00Z",
            toIso = "2026-08-08T11:00:00Z",
            tasks = listOf(
                TaskFormRow(
                    localKey = 2,
                    employeeIds = listOf("employee"),
                    fromIso = "2026-08-08T09:59:00Z",
                    toIso = "2026-08-08T11:01:00Z",
                ),
            ),
        )

        val errors = computeReturnToRampErrors(occurrence, emptySet())
        assertNotNull(errors?.tasksByKey?.get(2)?.from)
        assertNotNull(errors?.tasksByKey?.get(2)?.to)
    }
}
