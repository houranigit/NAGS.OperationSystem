package com.nags.operations.data

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.contentOrNull

/** Upgrades cached service-line response JSON written before service performers became a list. */
internal fun migrateLegacyWorkOrderServiceLinePerformers(workOrder: JsonElement): JsonElement {
    val workOrderObject = workOrder as? JsonObject ?: return workOrder
    val serviceLines = workOrderObject["serviceLines"] as? JsonArray ?: return workOrder
    var changed = false
    val migratedLines = JsonArray(
        serviceLines.map { element ->
            val line = element as? JsonObject ?: return@map element
            val currentPerformers = line["performedBy"] as? JsonArray
            if (!currentPerformers.isNullOrEmpty()) return@map line
            val legacyStaffMemberId = (line["performedByStaffMemberId"] as? JsonPrimitive)
                ?.contentOrNull
                ?.takeIf { it.isNotBlank() }
                ?: return@map line
            val legacyName = (line["performedByName"] as? JsonPrimitive)
                ?.contentOrNull
                .orEmpty()
            changed = true
            JsonObject(
                line.toMutableMap().apply {
                    remove("performedByStaffMemberId")
                    remove("performedByName")
                    put(
                        "performedBy",
                        JsonArray(
                            listOf(
                                JsonObject(
                                    mapOf(
                                        "staffMemberId" to JsonPrimitive(legacyStaffMemberId),
                                        "fullName" to JsonPrimitive(legacyName),
                                        // The legacy response did not carry the employee number.
                                        "employeeId" to JsonPrimitive(""),
                                    ),
                                ),
                            ),
                        ),
                    )
                },
            )
        },
    )
    return if (changed) {
        JsonObject(workOrderObject + ("serviceLines" to migratedLines))
    } else {
        workOrder
    }
}

/** Applies [migrateLegacyWorkOrderServiceLinePerformers] to a serialized flight draft. */
internal fun migrateLegacyCachedWorkOrderOnFlight(flight: JsonElement): JsonElement {
    val flightObject = flight as? JsonObject ?: return flight
    val cached = flightObject["cachedMyWorkOrder"] ?: return flight
    val migrated = migrateLegacyWorkOrderServiceLinePerformers(cached)
    return if (migrated === cached) flight else JsonObject(flightObject + ("cachedMyWorkOrder" to migrated))
}
