package com.nags.operations.data.repo

import com.nags.operations.ui.workorder.CreateWorkOrderFormState
import kotlinx.serialization.decodeFromString
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

internal object WorkOrderDraftJson {
    private val json = Json {
        ignoreUnknownKeys = true
        encodeDefaults = true
    }

    fun encodeFlight(row: WorkOrderFlightRow): String =
        json.encodeToString(WorkOrderFlightRow.serializer(), row)

    fun decodeFlight(payload: String): WorkOrderFlightRow =
        json.decodeFromString<WorkOrderFlightRow>(payload)

    fun encodeForm(form: CreateWorkOrderFormState): String =
        json.encodeToString(CreateWorkOrderFormState.serializer(), form)

    fun decodeForm(payload: String): CreateWorkOrderFormState =
        json.decodeFromString<CreateWorkOrderFormState>(payload)
}
