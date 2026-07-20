package com.nags.operations.data.repo

import com.nags.operations.ui.workorder.WorkOrderDraftSubmissionMode
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
        assertEquals(WorkOrderDraftSubmissionMode.Unknown, form.draftSubmissionMode)
        assertTrue(form.tasks.single().toolQuantities.isEmpty())
        assertEquals(listOf("staff-1"), form.serviceLines.single().employeeIds)
    }
}
