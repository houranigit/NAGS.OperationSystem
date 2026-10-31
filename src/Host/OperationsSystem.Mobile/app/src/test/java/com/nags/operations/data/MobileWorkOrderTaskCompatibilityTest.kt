package com.nags.operations.data

import com.nags.operations.data.api.WorkOrderTaskInput
import com.nags.operations.data.api.WorkOrderWireRequest
import com.nags.operations.data.api.WorkOrderServiceLineInput
import kotlinx.serialization.json.encodeToJsonElement
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import org.junit.Assert.assertEquals
import kotlinx.serialization.json.Json
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class MobileWorkOrderTaskCompatibilityTest {
    private val json = Json { ignoreUnknownKeys = true }

    @Test
    fun reference_fields_preserve_old_wire_shape_until_supplied() {
        val wireJson = Json { encodeDefaults = true }
        val service = WorkOrderServiceLineInput(serviceId = "service-1", fromUtc = "2026-07-11T10:00:00Z", toUtc = "2026-07-11T11:00:00Z")
        val task = WorkOrderTaskInput(taskType = "Major", fromUtc = service.fromUtc, toUtc = service.toUtc)
        val request = WorkOrderWireRequest(type = "Completion", serviceLines = listOf(service), tasks = listOf(task))
        assertFalse("workPackageNumber" in wireJson.encodeToJsonElement(request).jsonObject)
        assertFalse("customerReferenceNumber" in wireJson.encodeToJsonElement(service).jsonObject)
        assertFalse("customerReferenceNumber" in wireJson.encodeToJsonElement(task).jsonObject)
        assertEquals("WP-0001", wireJson.encodeToJsonElement(request.copy(workPackageNumber = "WP-0001"))
            .jsonObject.getValue("workPackageNumber").jsonPrimitive.content)
        assertEquals("SVC-0002", wireJson.encodeToJsonElement(service.copy(customerReferenceNumber = "SVC-0002"))
            .jsonObject.getValue("customerReferenceNumber").jsonPrimitive.content)
        assertEquals("TASK-0003", wireJson.encodeToJsonElement(task.copy(customerReferenceNumber = "TASK-0003"))
            .jsonObject.getValue("customerReferenceNumber").jsonPrimitive.content)
    }

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
