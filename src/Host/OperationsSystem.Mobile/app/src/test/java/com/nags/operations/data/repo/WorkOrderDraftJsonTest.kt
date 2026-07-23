package com.nags.operations.data.repo

import com.nags.operations.ui.workorder.WorkOrderDraftSubmissionMode
import com.nags.operations.ui.workorder.CreateWorkOrderFormState
import com.nags.operations.ui.workorder.ServiceLineFormRow
import com.nags.operations.ui.workorder.TaskFormRow
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class WorkOrderDraftJsonTest {
    @Test
    fun legacy_form_json_decodes_with_safe_new_defaults() {
        val legacyJson = """
            {
              "flightNumber":"MOB100",
              "aircraftTypeId":"aircraft-1",
              "ataIso":"2026-07-11T10:00:00Z",
              "serviceLines":[{
                "localKey":2,
                "serviceId":"service-1",
                "employeeId":"staff-1"
              }],
              "tasks":[{
                "localKey":1,
                "taskType":"Major",
                "toolIds":["tool-1"]
              }]
            }
        """.trimIndent()

        val form = WorkOrderDraftJson.decodeForm(legacyJson)

        assertEquals("", form.atdIso)
        assertEquals("", form.serviceLines.single().fromIso)
        assertEquals("", form.serviceLines.single().toIso)
        assertEquals("", form.tasks.single().fromIso)
        assertEquals("", form.tasks.single().toIso)
        assertEquals(WorkOrderDraftSubmissionMode.Unknown, form.draftSubmissionMode)
        assertTrue(form.tasks.single().toolQuantities.isEmpty())
        assertEquals(listOf("staff-1"), form.serviceLines.single().employeeIds)
    }

    @Test
    fun current_form_round_trip_preserves_atd_and_mixed_line_timestamps() {
        val original = CreateWorkOrderFormState(
            flightNumber = "MOB200",
            ataIso = "2026-07-11T10:00:00-05:00",
            atdIso = "2026-07-11T12:15:00-05:00",
            serviceLines = listOf(
                ServiceLineFormRow(
                    localKey = 1L,
                    fromIso = "2026-07-11T10:05:00-05:00",
                    toIso = "",
                ),
            ),
            tasks = listOf(
                TaskFormRow(
                    localKey = 2L,
                    fromIso = "",
                    toIso = "2026-07-11T11:45:00-05:00",
                ),
            ),
        )

        val restored = WorkOrderDraftJson.decodeForm(WorkOrderDraftJson.encodeForm(original))

        assertEquals(original.atdIso, restored.atdIso)
        assertEquals(original.serviceLines.single().fromIso, restored.serviceLines.single().fromIso)
        assertEquals(original.serviceLines.single().toIso, restored.serviceLines.single().toIso)
        assertEquals(original.tasks.single().fromIso, restored.tasks.single().fromIso)
        assertEquals(original.tasks.single().toIso, restored.tasks.single().toIso)
    }
}
