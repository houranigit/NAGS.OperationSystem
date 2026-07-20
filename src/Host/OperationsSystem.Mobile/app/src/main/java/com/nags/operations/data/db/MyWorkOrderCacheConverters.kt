package com.nags.operations.data.db

import androidx.room.TypeConverter
import com.nags.operations.data.WorkOrderDetailWireDto
import com.nags.operations.data.migrateLegacyWorkOrderServiceLinePerformers
import kotlinx.serialization.json.decodeFromJsonElement
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

/**
 * Persists [WorkOrderDetailWireDto] as JSON on flight cache rows (same approach as
 * [FlightServiceConverters] — whole-object snapshot, replaced on each sync).
 */
class MyWorkOrderCacheConverters {
    @TypeConverter
    fun toJson(value: WorkOrderDetailWireDto?): String? =
        value?.let { json.encodeToString(it) }

    @TypeConverter
    fun fromJson(raw: String?): WorkOrderDetailWireDto? {
        if (raw.isNullOrBlank()) return null
        return runCatching {
            json.decodeFromJsonElement<WorkOrderDetailWireDto>(
                migrateLegacyWorkOrderServiceLinePerformers(json.parseToJsonElement(raw)),
            )
        }.getOrNull()
    }

    companion object {
        private val json = Json {
            ignoreUnknownKeys = true
            encodeDefaults = true
            isLenient = true
        }
    }
}
