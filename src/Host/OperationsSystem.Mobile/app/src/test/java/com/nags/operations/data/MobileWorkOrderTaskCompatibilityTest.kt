package com.nags.operations.data

import com.nags.operations.data.api.WorkOrderTaskInput
import kotlinx.serialization.json.Json
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class MobileWorkOrderTaskCompatibilityTest {
    private val json = Json { ignoreUnknownKeys = true }

    @Test
    fun legacy_cached_and_request_tasks_default_return_to_ramp_to_false() {
        val cached = json.decodeFromString<WorkOrderTaskWireDto>(
            """{
                "id":"task-1",
                "taskType":"Major",
                "fromUtc":"2026-07-11T10:00:00Z",
                "toUtc":"2026-07-11T11:00:00Z"
            }""".trimIndent(),
        )
        val request = json.decodeFromString<WorkOrderTaskInput>(
            """{
                "taskType":"Major",
                "fromUtc":"2026-07-11T10:00:00Z",
                "toUtc":"2026-07-11T11:00:00Z"
            }""".trimIndent(),
        )

        assertFalse(cached.isReturnToRamp)
        assertFalse(request.isReturnToRamp)
    }

    @Test
    fun request_task_round_trips_return_to_ramp_flag() {
        val request = WorkOrderTaskInput(
            taskType = "Major",
            fromUtc = "2026-07-11T10:00:00Z",
            toUtc = "2026-07-11T11:00:00Z",
            isReturnToRamp = true,
        )

        val decoded = json.decodeFromString<WorkOrderTaskInput>(
            json.encodeToString(WorkOrderTaskInput.serializer(), request),
        )

        assertTrue(decoded.isReturnToRamp)
    }
}
