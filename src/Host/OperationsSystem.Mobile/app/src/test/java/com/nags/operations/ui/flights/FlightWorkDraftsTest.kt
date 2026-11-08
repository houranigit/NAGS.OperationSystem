package com.nags.operations.ui.flights

import androidx.compose.runtime.saveable.SaverScope
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Test

class FlightWorkDraftsTest {
    private val scope = object : SaverScope {
        override fun canBeSaved(value: Any) = true
    }

    @Test
    fun creationTimesAndIdsAreExplicitEvenWhenTheyMatchDefaults() {
        val service = FlightServiceDraft(employees = listOf(FlightEmployeeDraft()))
        val task = FlightTaskDraft(tools = listOf(FlightResourceDraft()))
        val original = FlightWorkDrafts(services = listOf(service), tasks = listOf(task))
        val saved = with(FlightWorkDraftsSaver) { scope.save(original) }!!
        val json = Json.parseToJsonElement(saved).jsonObject
        val savedService = json.getValue("services").jsonArray.single().jsonObject
        val savedTask = json.getValue("tasks").jsonArray.single().jsonObject

        assertEquals(service.id, savedService.getValue("id").jsonPrimitive.content)
        assertEquals(service.period.start, savedService.getValue("period").jsonObject.getValue("start").jsonPrimitive.content)
        assertEquals(service.employees.single().period.start,
            savedService.getValue("employees").jsonArray.single().jsonObject.getValue("period").jsonObject.getValue("start").jsonPrimitive.content)
        assertEquals(task.tools.single().period.start,
            savedTask.getValue("tools").jsonArray.single().jsonObject.getValue("period").jsonObject.getValue("start").jsonPrimitive.content)
        assertEquals(original, FlightWorkDraftsSaver.restore(saved))
    }

    @Test
    fun repeatedCatalogItemsKeepIndependentNestedDataThroughRestoration() {
        val first = FlightServiceDraft(serviceId = "same-service", reference = "first",
            employees = listOf(FlightEmployeeDraft(employeeId = "same-employee")),
            attachments = listOf(FlightAttachmentDraft(uri = "content://test/one", name = "photo.jpg",
                mimeType = "image/jpeg", ownedCapture = true)))
        val second = FlightServiceDraft(serviceId = "same-service", reference = "second",
            employees = listOf(FlightEmployeeDraft(employeeId = "same-employee")))
        val task = FlightTaskDraft(taskType = "Minor", remarks = "task",
            attachments = listOf(FlightAttachmentDraft(uri = "content://test/voice", name = "voice.m4a",
                mimeType = "audio/mp4", durationMillis = 3240, ownedCapture = true)),
            materials = listOf(FlightResourceDraft(resourceId = "material", remarks = "material")),
            generalSupports = listOf(FlightResourceDraft(resourceId = "support", period = FlightWorkPeriod("2026-11-08T10:00", "2026-11-08T11:00"))))
        val original = FlightWorkDrafts(listOf(first, second), listOf(task))
        val saved = with(FlightWorkDraftsSaver) { scope.save(original) }!!
        val restored = FlightWorkDraftsSaver.restore(saved)

        assertNotNull(restored)
        assertEquals(original, restored)
        val afterRemovingSecond = restored!!.copy(services = restored.services.filterNot { it.id == second.id })
        assertEquals(listOf(first), afterRemovingSecond.services)
        assertEquals(listOf(task), afterRemovingSecond.tasks)
    }
}
