package com.nags.operations.ui.workorder

import com.nags.operations.data.MobileCatalogsDto
import com.nags.operations.data.WorkOrderDetailWireDto
import com.nags.operations.data.WorkOrderTaskWireDto
import com.nags.operations.data.api.WorkOrderTaskInput
import com.nags.operations.data.db.entities.AtaChapterEntity
import com.nags.operations.data.outbox.OutboxPayload
import com.nags.operations.data.outbox.toWireTask
import com.nags.operations.data.repo.WorkOrderDraftJson
import com.nags.operations.data.sync.toAtaChapterEntities
import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class AtaChapterContractTest {
    private val json = Json { ignoreUnknownKeys = true; encodeDefaults = true }

    @Test
    fun pre_ata_tasks_and_drafts_remain_optional_and_old_retries_omit_the_new_field() {
        val cached = json.decodeFromString<WorkOrderTaskWireDto>(
            """{"id":"task-1","taskType":"Major","fromUtc":"$from","toUtc":"$to"}""",
        )
        val queued = json.decodeFromString<OutboxPayload.TaskInput>(
            """{"taskType":"Major","description":null,"fromIso":"$from","toIso":"$to","employeeIds":[]}""",
        )
        val form = WorkOrderDraftJson.decodeForm("""{"tasks":[{"localKey":1}]}""")
        assertNull(cached.ataChapterId)
        assertNull(cached.ataChapterCode)
        assertNull(cached.ataChapterTitle)
        assertNull(queued.ataChapterId)
        assertNull(form.tasks.single().ataChapterId)
        assertFalse(json.encodeToString(WorkOrderTaskInput.serializer(), queued.toWireTask()).contains("ataChapterId"))
    }

    @Test
    fun active_catalog_contract_is_cached_with_category_and_display_label() {
        val catalog = json.decodeFromString<MobileCatalogsDto>(
            """{
                "generatedAtUtc":"$from",
                "ataChapters":[{"id":"chapter-21","categoryId":"airframe","categoryName":"Airframe Systems (20s–50s)","code":"21","title":"Air Conditioning"}]
            }""",
        ).toAtaChapterEntities()
        assertEquals("chapter-21", catalog.single().id)
        assertEquals("airframe", catalog.single().categoryId)
        assertEquals("Airframe Systems (20s–50s)", catalog.single().categoryName)
        assertEquals("21. Air Conditioning", catalog.single().displayLabel)
        assertTrue(json.decodeFromString<MobileCatalogsDto>("""{"generatedAtUtc":"$from"}""").toAtaChapterEntities().isEmpty())
    }

    @Test
    fun normal_and_rtr_cached_tasks_keep_historical_chapters_through_draft_resume() {
        val workOrder = json.decodeFromString<WorkOrderDetailWireDto>(
            """{
                "id":"wo-1","flightId":"flight-1","type":"Completion","status":"Submitted",
                "ownerUserId":"user-1","customerId":"customer-1","customerName":"Customer",
                "stationId":"station-1","stationIata":"ORD","stationName":"Chicago",
                "operationTypeId":"operation-1","operationTypeName":"Transit","plannedFlightNumber":"MOB100",
                "scheduledArrivalUtc":"$from","scheduledDepartureUtc":"$to","actualFlightNumber":"MOB100",
                "createdAtUtc":"$from","rowVersion":"version-1",
                "tasks":[{"id":"task-1","taskType":"Major","fromUtc":"$from","toUtc":"$to",
                  "ataChapterId":"chapter-21","ataChapterCode":"21","ataChapterTitle":"Air Conditioning"}],
                "returnToRamps":[{"id":"rtr-1","fromUtc":"$from","toUtc":"$to","sequence":1,
                  "tasks":[{"id":"task-2","taskType":"Minor","fromUtc":"$from","toUtc":"$to",
                    "ataChapterId":"chapter-61","ataChapterCode":"61","ataChapterTitle":"Propellers / Propulsors"}]}]
            }""",
        )
        var key = 0L
        val mapped = workOrder.toPrefilledCreateFormState { ++key }
        val resumed = WorkOrderDraftJson.decodeForm(WorkOrderDraftJson.encodeForm(mapped))
        assertEquals("chapter-21", resumed.tasks.single().ataChapterId)
        assertEquals("21. Air Conditioning", resumed.tasks.single().ataChapterDisplayLabel(emptyList()))
        val rtrTask = resumed.returnToRamps.single().tasks.single()
        assertEquals("chapter-61", rtrTask.ataChapterId)
        assertEquals("61. Propellers / Propulsors", rtrTask.ataChapterDisplayLabel(emptyList()))
        assertEquals("task-2", rtrTask.serverId)
    }

    @Test
    fun saved_inactive_chapter_stays_readable_without_entering_active_options() {
        val options = listOf(AtaChapterEntity("chapter-21", "airframe", "Airframe", "21", "Air Conditioning"))
        val saved = TaskFormRow(localKey = 1, ataChapterId = "chapter-61", ataChapterCode = "61", ataChapterTitle = "Propellers / Propulsors")
        assertEquals("61. Propellers / Propulsors", saved.ataChapterDisplayLabel(options))
        assertEquals(listOf("chapter-21"), options.map { it.id })
        assertEquals("", saved.copy(ataChapterId = null, ataChapterCode = null, ataChapterTitle = null).ataChapterDisplayLabel(options))
        assertEquals("21. Air Conditioning", saved.copy(ataChapterId = "chapter-21", ataChapterCode = null, ataChapterTitle = null).ataChapterDisplayLabel(options))
    }

    @Test
    fun saved_snapshot_title_survives_catalog_rename_while_options_offer_current_title() {
        val current = AtaChapterEntity("chapter-21", "airframe", "Airframe", "21", "Renamed Air Conditioning")
        val saved = TaskFormRow(
            localKey = 1, ataChapterId = current.id,
            ataChapterCode = "21", ataChapterTitle = "Air Conditioning",
        )
        val resumed = WorkOrderDraftJson.decodeForm(WorkOrderDraftJson.encodeForm(CreateWorkOrderFormState(tasks = listOf(saved))))
        assertEquals("21. Air Conditioning", resumed.tasks.single().ataChapterDisplayLabel(listOf(current)))
        assertEquals("21. Renamed Air Conditioning", current.displayLabel)
        // A newly added task captures the current label when its catalog choice is made.
        val selected = TaskFormRow(localKey = 2, ataChapterId = current.id, ataChapterCode = current.code, ataChapterTitle = current.title)
        assertEquals("21. Renamed Air Conditioning", selected.ataChapterDisplayLabel(listOf(current)))
    }

    @Test
    fun queued_normal_and_rtr_tasks_preserve_selection_in_wire_submission() {
        val task = OutboxPayload.TaskInput(
            taskType = "Minor", description = null, fromIso = from, toIso = to,
            employeeIds = emptyList(), ataChapterId = "chapter-21",
        )
        val queued = json.decodeFromString<OutboxPayload.TaskInput>(json.encodeToString(OutboxPayload.TaskInput.serializer(), task))
        assertEquals("chapter-21", queued.toWireTask().ataChapterId)
        val occurrence = OutboxPayload.ReturnToRampInput(fromIso = from, toIso = to, tasks = listOf(task))
        val restored = json.decodeFromString<OutboxPayload.ReturnToRampInput>(
            json.encodeToString(OutboxPayload.ReturnToRampInput.serializer(), occurrence),
        )
        val wire = restored.tasks.single().toWireTask()
        assertEquals("chapter-21", wire.ataChapterId)
        assertTrue(json.encodeToString(WorkOrderTaskInput.serializer(), wire).contains("\"ataChapterId\":\"chapter-21\""))
    }

    companion object {
        private const val from = "2026-09-20T10:00:00Z"
        private const val to = "2026-09-20T11:00:00Z"
    }
}
