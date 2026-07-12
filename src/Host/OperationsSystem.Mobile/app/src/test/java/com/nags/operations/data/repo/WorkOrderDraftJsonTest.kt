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
    }
}
