package com.nags.operations.data.db

import androidx.room.TypeConverter
import com.nags.operations.data.MobileMyWorkOrderWireDto
import kotlinx.serialization.decodeFromString
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

/**
 * Persists [MobileMyWorkOrderWireDto] as JSON on flight cache rows (same approach as
 * [FlightServiceConverters] — whole-object snapshot, replaced on each sync).
 */
class MyWorkOrderCacheConverters {
    @TypeConverter
    fun toJson(value: MobileMyWorkOrderWireDto?): String? =
        value?.let { json.encodeToString(it) }

    @TypeConverter
    fun fromJson(raw: String?): MobileMyWorkOrderWireDto? {
        if (raw.isNullOrBlank()) return null
        return runCatching { json.decodeFromString<MobileMyWorkOrderWireDto>(raw) }.getOrNull()
    }

    companion object {
        private val json = Json {
            ignoreUnknownKeys = true
            encodeDefaults = true
            isLenient = true
        }
    }
}
