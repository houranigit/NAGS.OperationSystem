package com.nags.operations.data.repo

import com.nags.operations.data.migrateLegacyCachedWorkOrderOnFlight
import com.nags.operations.ui.workorder.CreateWorkOrderFormState
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.decodeFromJsonElement

internal object WorkOrderDraftJson {
    private val json = Json {
        ignoreUnknownKeys = true
        encodeDefaults = true
    }

    fun encodeFlight(row: WorkOrderFlightRow): String =
        json.encodeToString(WorkOrderFlightRow.serializer(), row)

    fun decodeFlight(payload: String): WorkOrderFlightRow =
        json.decodeFromJsonElement<WorkOrderFlightRow>(
            migrateLegacyCachedWorkOrderOnFlight(json.parseToJsonElement(payload)),
        )

    fun encodeForm(form: CreateWorkOrderFormState): String =
        json.encodeToString(CreateWorkOrderFormState.serializer(), form)

    fun decodeForm(payload: String): CreateWorkOrderFormState =
        json.decodeFromJsonElement<CreateWorkOrderFormState>(
            migrateLegacyDraftServiceLinePerformers(json.parseToJsonElement(payload)),
        )

    private fun migrateLegacyDraftServiceLinePerformers(form: JsonElement): JsonElement {
        val formObject = form as? JsonObject ?: return form
        val serviceLines = formObject["serviceLines"] as? JsonArray ?: return form
        var changed = false
        val migratedLines = JsonArray(
            serviceLines.map { element ->
                val line = element as? JsonObject ?: return@map element
                if ("employeeIds" in line) return@map line
                val legacyId = (line["employeeId"] as? JsonPrimitive)
                    ?.contentOrNull
                    ?.takeIf { it.isNotBlank() }
                    ?: return@map line
                changed = true
                JsonObject(
                    line.toMutableMap().apply {
                        remove("employeeId")
                        put("employeeIds", JsonArray(listOf(JsonPrimitive(legacyId))))
                    },
                )
            },
        )
        return if (changed) JsonObject(formObject + ("serviceLines" to migratedLines)) else form
    }
}
